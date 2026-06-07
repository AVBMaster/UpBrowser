using System.Runtime.CompilerServices;
using SkiaSharp;
using UpBrowser.Core.Performance;
using UpBrowser.Core.Performance.Compositor;
using UpBrowser.Core.Performance.Memory;
using UpBrowser.Core.Performance.Diagnostics;

namespace UpBrowser.Rendering;

/// <summary>
/// Browser tile compositor. Owns all coordinate transforms internally so the
/// caller only needs to provide the physical (device-pixel) viewport rectangle
/// and the combined DPR×resolution scale factor.
///
/// Key design decisions:
///   * <c>Render</c> saves, resets, and clips the canvas — it takes full control
///     of the transform stack.
///   * All tile positions are expressed in <em>physical device pixels</em>
///     (<c>tx * _tileSize * physicalScale</c>). The caller's canvas matrix is
///     irrelevant.
///   * The number of tiles synchronously rasterised per frame is capped at
///     <see cref="MaxTilesPerFrame"/>; additional visible tiles are simply not
///     drawn (they appear as white until a future frame fills them).
///   * Stale-tile detection uses a <c>_displayListVersion</c> counter: when the
///     display list changes, all cached tiles are discarded on the next render.
///   * An LRU eviction pass runs periodically so the cache does not grow
///     unbounded.
/// </summary>
public sealed class TiledCompositor : IDisposable
{
    /// <summary>Tile priority. Lower numeric value = higher priority.</summary>
    public enum TilePriority : byte
    {
        Visible = 0,
        Overscan = 1,
        Predictive = 2,
        Distant = 3,
    }

    private sealed class Tile : IDisposable
    {
        public int X;
        public int Y;
        public SKImage? Image;
        public SKRect PageRect;
        public ulong Version;
        public ulong DisplayListVersion;
        public long LastAccessTick;
        public long EstimatedBytes;
        public TilePriority Priority;
        public CompositorDisplayList? Recorded;
        public bool Disposed;

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Image?.Dispose();
            Image = null;
            Recorded?.Reset();
            Recorded = null;
        }
    }

    public const int DefaultTileSize = 512;
    public const int MaxTiles = 1024;
    public const long DefaultMaxBytes = 96L * 1024 * 1024;
    private const int MaxBackgroundTilesPerFrame = 16;  // non-visible tiles per frame
    private const long CleanupIntervalMs = 5000;         // evict stale tiles every 5 s
    private const long StaleTileAgeMs = 30000;           // tile unused for 30 s is stale

    private readonly int _tileSize;
    private readonly int _overscanRings;
    private readonly bool _recordCommands;
    private DisplayList _displayList;
    private readonly CompositorReplayer _replayer = new();
    private ulong _displayListVersion;
    private ulong _lastRenderedDisplayListVersion;
    private ulong _nextTileVersion;

    private readonly Dictionary<long, Tile> _tiles = new();
    private readonly LinkedList<long> _lru = new();
    private long _currentBytes;
    private long _bytesHighWater;
    private int _drawCalls;
    private int _tilesReused;
    private int _tilesRasterized;
    private int _backgroundTilesCreatedThisFrame;
    private int _tilesSkippedThisFrame;
    private int _tilesEvictedLru;
    private int _tilesEvictedBudget;
    private int _overscanTilesRasterized;
    private int _predictiveTilesRasterized;
    private int _visibleTilesRasterized;
    private long _rasterNanos;
    private long _recordingsProduced;

    private float _lastPhysicalScale = 1f;
    private SKRect _predictedViewport;
    private long _lastCleanupTick;

    private readonly TileManager? _hubTiles;
    private readonly PredictiveTileScheduler? _hubPredictor;
    private readonly MemoryBudget? _memoryBudget;

    public int TileSize => _tileSize;
    public int DrawCalls => _drawCalls;
    public int TilesReused => _tilesReused;
    public int TilesRasterized => _tilesRasterized;
    public int TilesSkippedThisFrame => _tilesSkippedThisFrame;
    public int TilesEvictedLru => _tilesEvictedLru;
    public int TilesEvictedBudget => _tilesEvictedBudget;
    public int VisibleTilesRasterized => _visibleTilesRasterized;
    public int OverscanTilesRasterized => _overscanTilesRasterized;
    public int PredictiveTilesRasterized => _predictiveTilesRasterized;
    public int CachedTileCount => _tiles.Count;
    public long CachedBytes => _currentBytes;
    public long BytesHighWater => _bytesHighWater;
    public ulong Version => _displayListVersion;
    public long RasterNanos => _rasterNanos;
    public long RecordingsProduced => _recordingsProduced;
    public SKRect PredictedViewport => _predictedViewport;
    public CompositorReplayer Replayer => _replayer;

    public TiledCompositor(
        int tileSize = DefaultTileSize,
        int overscanRings = 1,
        bool adaptiveTileSize = false,
        int minTileSize = 128,
        int maxTileSize = 1024,
        bool recordCommands = false,
        DisplayList? displayList = null,
        Func<SKRect, IEnumerable<PaintOp>>? opsProvider = null,
        TileManager? hubTiles = null,
        PredictiveTileScheduler? hubPredictor = null,
        MemoryBudget? memoryBudget = null)
    {
        if (tileSize < 64) tileSize = 64;
        if (tileSize > 2048) tileSize = 2048;
        _tileSize = tileSize;
        _overscanRings = Math.Clamp(overscanRings, 0, 4);
        _recordCommands = recordCommands;
        _displayList = displayList ?? new DisplayList();
        _displayListVersion = 1;
        _nextTileVersion = 1;
        _hubTiles = hubTiles;
        _hubPredictor = hubPredictor;
        _memoryBudget = memoryBudget;
        _lastCleanupTick = Environment.TickCount64;
    }

    public void UpdateScrollVelocity(float vx, float vy)
    {
        _hubPredictor?.UpdateVelocity(vx, vy);
    }

    /// <summary>Report the viewport that the compositor should aim at (in physical device pixels).</summary>
    public void SetPredictedViewport(SKRect physicalViewport)
    {
        _predictedViewport = physicalViewport;
    }

    /// <summary>
    /// Replace the display list. When the list identity changes we bump
    /// <c>_displayListVersion</c> so the next render pass discards stale tiles
    /// before rasterising.
    /// </summary>
    public void SetDisplayList(DisplayList displayList)
    {
        if (ReferenceEquals(_displayList, displayList)) return;
        _displayList = displayList ?? new DisplayList();
        _displayListVersion++;
    }

    public void InvalidateAll()
    {
        _nextTileVersion++;
        foreach (var t in _tiles.Values) t.Dispose();
        _tiles.Clear();
        _lru.Clear();
        _currentBytes = 0;
        _hubTiles?.Clear();
    }

    /// <summary>
    /// Invalidate only the tiles that intersect <paramref name="pageRect"/>.
    /// </summary>
    public void InvalidateRect(SKRect pageRect)
    {
        if (pageRect.Width <= 0 || pageRect.Height <= 0)
        {
            InvalidateAll();
            return;
        }
        int minX = PageXToTile(pageRect.Left);
        int maxX = PageXToTile(pageRect.Right - 0.01f);
        int minY = PageYToTile(pageRect.Top);
        int maxY = PageYToTile(pageRect.Bottom - 0.01f);
        _nextTileVersion++;
        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                long key = TileKey(tx, ty);
                if (_tiles.TryGetValue(key, out var t))
                {
                    t.Dispose();
                    _tiles.Remove(key);
                    _lru.Remove(key);
                    _currentBytes -= t.EstimatedBytes;
                }
            }
        }
    }

    /// <summary>
    /// Composite the viewport. Fully owns the canvas transform:
    ///   <c>canvas.Save() → ResetMatrix() → ClipRect(physicalViewport)</c>
    /// All tile drawing uses physical device-pixel coordinates.
    /// </summary>
    public void Render(SKCanvas canvas, SKRect physicalViewport, float physicalScale, DisplayList? displayList = null)
    {
        if (displayList != null && !ReferenceEquals(_displayList, displayList))
        {
            _displayList = displayList;
            _displayListVersion++;
        }
        if (physicalScale <= 0.01f) physicalScale = 1f;

        // ---- version check: discard cache when display list changed ----
        PeriodicallyCleanup();
        if (_lastRenderedDisplayListVersion < _displayListVersion)
        {
            InvalidateAll();
            _lastRenderedDisplayListVersion = _displayListVersion;
        }

        _drawCalls = 0;
        _tilesReused = 0;
        _tilesRasterized = 0;
        _backgroundTilesCreatedThisFrame = 0;
        _tilesSkippedThisFrame = 0;
        _overscanTilesRasterized = 0;
        _predictiveTilesRasterized = 0;
        _visibleTilesRasterized = 0;

        _lastPhysicalScale = physicalScale;

        // The compositor takes full control of the canvas transform.
        canvas.Save();
        canvas.ResetMatrix();
        canvas.ClipRect(physicalViewport);

        // Tile indices from the physical viewport.
        float tilePhysical = _tileSize * physicalScale;
        int visMinX = (int)Math.Floor(physicalViewport.Left / tilePhysical);
        int visMaxX = (int)Math.Floor((physicalViewport.Right - 0.01f) / tilePhysical);
        int visMinY = (int)Math.Floor(physicalViewport.Top / tilePhysical);
        int visMaxY = (int)Math.Floor((physicalViewport.Bottom - 0.01f) / tilePhysical);

        int pixelSize = Math.Max(1, (int)Math.Ceiling(_tileSize * physicalScale));

        // 1. Visible tiles.
        for (int ty = visMinY; ty <= visMaxY; ty++)
        {
            for (int tx = visMinX; tx <= visMaxX; tx++)
            {
                if (DrawOrBuildTile(canvas, tx, ty, pixelSize, physicalScale, TilePriority.Visible))
                    _visibleTilesRasterized++;
            }
        }

        // 2. Overscan tiles.
        if (_overscanRings > 0)
        {
            int expand = _overscanRings;
            for (int ring = 1; ring <= expand; ring++)
            {
                int top = visMinY - ring;
                int bottom = visMaxY + ring;
                int left = visMinX - ring;
                int right = visMaxX + ring;
                for (int tx = left; tx <= right; tx++)
                {
                    if (DrawOrBuildTile(canvas, tx, top, pixelSize, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                    if (DrawOrBuildTile(canvas, tx, bottom, pixelSize, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                }
                for (int ty = top + 1; ty <= bottom - 1; ty++)
                {
                    if (DrawOrBuildTile(canvas, left, ty, pixelSize, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                    if (DrawOrBuildTile(canvas, right, ty, pixelSize, physicalScale, TilePriority.Overscan))
                        _overscanTilesRasterized++;
                }
            }
        }

        // 3. Predictive tiles.
        if (_hubPredictor != null && !_predictedViewport.IsEmpty)
        {
            try { _hubPredictor.SchedulePreRasters(_predictedViewport, layerId: 0); }
            catch { }
        }

        EnforceBudget();
        canvas.Restore();

        if (PipelineTimings.TilesRasterized != null)
        {
            PipelineTimings.TilesRasterized.AddSample(_tilesRasterized);
            PipelineTimings.TilesReused.AddSample(_tilesReused);
        }
    }

    /// <summary>
    /// Read-back step for the predictive scheduler.
    /// </summary>
    public int PumpPredictiveTiles(SKCanvas canvas, int maxTiles)
    {
        if (_hubPredictor == null) return 0;
        if (_predictedViewport.IsEmpty) return 0;
        int pumped = 0;
        float tilePhysical = _tileSize * _lastPhysicalScale;
        int pixelSize = Math.Max(1, (int)Math.Ceiling(_tileSize * _lastPhysicalScale));

        for (int i = 0; i < maxTiles; i++)
        {
            if (_predictedViewport.IsEmpty) break;
            _hubPredictor.SchedulePreRasters(_predictedViewport, layerId: 0);
            pumped++;
        }

        if (pumped > 0 && !_predictedViewport.IsEmpty)
        {
            int predMinX = (int)Math.Floor(_predictedViewport.Left / tilePhysical);
            int predMaxX = (int)Math.Floor((_predictedViewport.Right - 0.01f) / tilePhysical);
            int predMinY = (int)Math.Floor(_predictedViewport.Top / tilePhysical);
            int predMaxY = (int)Math.Floor((_predictedViewport.Bottom - 0.01f) / tilePhysical);
            int added = 0;
            for (int ty = predMinY; ty <= predMaxY && added < maxTiles; ty++)
            {
                for (int tx = predMinX; tx <= predMaxX && added < maxTiles; tx++)
                {
                    if (DrawOrBuildTile(canvas, tx, ty, pixelSize, _lastPhysicalScale, TilePriority.Predictive))
                    {
                        _predictiveTilesRasterized++;
                        added++;
                    }
                }
            }
        }
        return pumped;
    }

    private IEnumerable<PaintOp> OpsFor(SKRect tilePageRect)
    {
        return _displayList.GetOpsInRect(tilePageRect);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool DrawOrBuildTile(SKCanvas canvas, int tx, int ty, int pixelSize, float physicalScale, TilePriority priority)
    {
        long key = TileKey(tx, ty);
        if (_tiles.TryGetValue(key, out var tile)
            && tile.Image != null
            && !tile.Disposed
            && tile.DisplayListVersion == _displayListVersion)
        {
            Touch(tile);
            canvas.DrawImage(tile.Image, tx * _tileSize * physicalScale, ty * _tileSize * physicalScale);
            _drawCalls++;
            _tilesReused++;
            return false;
        }

        // Per-frame cap for non-visible tiles – visible tiles are always
        // rasterised immediately. Background tiles (overscan / predictive)
        // are capped so the main thread does not stall.
        bool isBackground = priority is TilePriority.Overscan or TilePriority.Predictive;
        if (isBackground && _backgroundTilesCreatedThisFrame >= MaxBackgroundTilesPerFrame)
        {
            _tilesSkippedThisFrame++;
            return false;
        }

        var tilePageRect = new SKRect(
            tx * _tileSize, ty * _tileSize,
            (tx + 1) * _tileSize, (ty + 1) * _tileSize);

        var sw = Clock.NowNanos();
        SKImage? img = RasterizeTile(tilePageRect, pixelSize, physicalScale, out var recorded);
        _rasterNanos += Clock.NowNanos() - sw;
        if (img == null) return false;

        if (isBackground) _backgroundTilesCreatedThisFrame++;
        tile = new Tile
        {
            X = tx,
            Y = ty,
            Image = img,
            PageRect = tilePageRect,
            Version = _nextTileVersion,
            DisplayListVersion = _displayListVersion,
            LastAccessTick = Environment.TickCount,
            EstimatedBytes = (long)pixelSize * pixelSize * 4,
            Priority = priority,
            Recorded = recorded,
        };
        if (recorded != null) _recordingsProduced++;
        _tiles[key] = tile;
        _lru.AddFirst(key);
        _currentBytes += tile.EstimatedBytes;
        if (_currentBytes > _bytesHighWater) _bytesHighWater = _currentBytes;
        _tilesRasterized++;

        canvas.DrawImage(img, tx * _tileSize * physicalScale, ty * _tileSize * physicalScale);
        _drawCalls++;
        return true;
    }

    private SKImage? RasterizeTile(SKRect tilePageRect, int pixelSize, float physicalScale, out CompositorDisplayList? recorded)
    {
        recorded = null;
        var info = new SKImageInfo(pixelSize, pixelSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        try
        {
            using var bmp = new SKBitmap(info);
            using var tc = new SKCanvas(bmp);
            tc.Clear(SKColors.Transparent);
            tc.Scale(physicalScale, physicalScale);
            tc.Translate(-tilePageRect.Left, -tilePageRect.Top);

            CompositorDisplayList? rec = _recordCommands ? new CompositorDisplayList() : null;
            if (rec != null)
            {
                rec.Add(CompositorCommand.Save());
                rec.Add(CompositorCommand.Translate(-tilePageRect.Left, -tilePageRect.Top));
                rec.Add(CompositorCommand.ClipRect(tilePageRect));
            }

            foreach (var op in OpsFor(tilePageRect))
            {
                try { op.AlignBounds(tc); } catch { }
                try { op.Execute(tc); } catch { }
                if (rec != null)
                    rec.Add(CompositorCommand.DrawRect(op.Bounds, SKColors.Transparent));
            }

            if (rec != null)
            {
                rec.Add(CompositorCommand.Restore());
                recorded = rec;
            }

            return SKImage.FromBitmap(bmp);
        }
        catch
        {
            recorded?.Reset();
            return null;
        }
    }

    private void Touch(Tile t)
    {
        long key = TileKey(t.X, t.Y);
        _lru.Remove(key);
        _lru.AddFirst(key);
        t.LastAccessTick = Environment.TickCount;
    }

    private void EnforceBudget()
    {
        long cap = GetEffectiveByteCap();
        int maxTiles = GetEffectiveTileCap();
        while ((_tiles.Count > maxTiles || _currentBytes > cap) && _lru.Count > 0)
        {
            long evictKey = _lru.Last!.Value;
            if (_tiles.TryGetValue(evictKey, out var t))
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(evictKey);
                _tilesEvictedBudget++;
            }
            _lru.RemoveLast();
        }
    }

    /// <summary>Evict tiles untouched for more than <see cref="StaleTileAgeMs"/>.</summary>
    private void PeriodicallyCleanup()
    {
        long now = Environment.TickCount64;
        if (now - _lastCleanupTick < CleanupIntervalMs) return;
        _lastCleanupTick = now;

        int evicted = 0;
        var keys = _tiles.Keys.ToArray();
        foreach (var key in keys)
        {
            if (_tiles.TryGetValue(key, out var t)
                && (now - t.LastAccessTick) > StaleTileAgeMs)
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(key);
                _lru.Remove(key);
                evicted++;
            }
        }
        if (evicted > 0) _tilesEvictedLru += evicted;
    }

    private long GetEffectiveByteCap()
    {
        if (_memoryBudget == null) return DefaultMaxBytes;
        return _memoryBudget.TileMemorySoftLimit;
    }

    private int GetEffectiveTileCap()
    {
        if (_memoryBudget == null) return MaxTiles;
        long bytesPerTile = (long)_tileSize * _tileSize * 4;
        long capFromBytes = _memoryBudget.TileMemorySoftLimit / Math.Max(1, bytesPerTile);
        return (int)Math.Min(MaxTiles, capFromBytes);
    }

    public void EvictLru(int count)
    {
        for (int i = 0; i < count && _lru.Count > 0; i++)
        {
            long evictKey = _lru.Last!.Value;
            if (_tiles.TryGetValue(evictKey, out var t))
            {
                _currentBytes -= t.EstimatedBytes;
                t.Dispose();
                _tiles.Remove(evictKey);
                _tilesEvictedLru++;
            }
            _lru.RemoveLast();
        }
    }

    public void Dispose()
    {
        foreach (var t in _tiles.Values) t.Dispose();
        _tiles.Clear();
        _lru.Clear();
        _currentBytes = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TileKey(int tx, int ty) => ((long)tx << 32) | (uint)ty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PageXToTile(float x) => (int)Math.Floor(x / _tileSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PageYToTile(float y) => (int)Math.Floor(y / _tileSize);
}
