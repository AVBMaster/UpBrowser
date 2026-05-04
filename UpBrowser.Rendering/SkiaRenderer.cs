using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public class SkiaRenderer : IDisposable
{
    private SKBitmap? _bitmap;
    private bool _disposed;
    private int _width;
    private int _height;
    private DisplayList? _currentDisplayList;
    private DirtyRegionManager? _dirtyManager;
    private bool _useDirtyRegions;

    public SKCanvas Canvas { get; private set; } = null!;
    public DisplayList? CurrentDisplayList => _currentDisplayList;

    public void Initialize(int width, int height, bool enableDirtyRegions = true)
    {
        _width = width;
        _height = height;
        _bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Canvas = new SKCanvas(_bitmap);

        if (enableDirtyRegions)
        {
            _dirtyManager = new DirtyRegionManager();
            _useDirtyRegions = true;
        }
    }

    public void Resize(int width, int height)
    {
        if (_width == width && _height == height) return;

        _width = width;
        _height = height;
        _bitmap?.Dispose();
        _bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        Canvas = new SKCanvas(_bitmap);

        if (_dirtyManager != null)
        {
            _dirtyManager.Invalidate(new SKRect(0, 0, width, height));
        }
    }

    public int Width => _width;
    public int Height => _height;

    public void SetDisplayList(DisplayList displayList)
    {
        _currentDisplayList = displayList;
    }

    public void Render(DisplayList displayList)
    {
        _currentDisplayList = displayList;

        if (_useDirtyRegions && _dirtyManager != null)
        {
            RenderDirtyRegions(displayList);
        }
        else
        {
            Canvas.Clear(SKColors.White);
            displayList.Execute(Canvas);
        }
    }

    private void RenderDirtyRegions(DisplayList displayList)
    {
        Canvas.Clear(SKColors.White);

        var dirtyRects = _dirtyManager!.GetDirtyRegions();

        if (dirtyRects.Count == 0)
        {
            displayList.Execute(Canvas);
            return;
        }

        foreach (var dirtyRect in dirtyRects)
        {
            Canvas.Save();
            Canvas.ClipRect(dirtyRect);

            var opsInRegion = displayList.GetOpsInRect(dirtyRect);
            foreach (var op in opsInRegion)
            {
                op.Execute(Canvas);
            }

            Canvas.Restore();
        }

        _dirtyManager.ClearDirtyRegions();
    }

    public void Invalidate(SKRect rect)
    {
        if (_dirtyManager != null && _useDirtyRegions)
        {
            _dirtyManager.Invalidate(rect);
        }
    }

    public void InvalidateFull()
    {
        if (_dirtyManager != null)
        {
            _dirtyManager.Invalidate(new SKRect(0, 0, _width, _height));
        }
    }

    public byte[] GetPixelData()
    {
        if (_bitmap == null) return Array.Empty<byte>();
        return _bitmap.Bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Canvas.Dispose();
        _bitmap?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class DirtyRegionManager
{
    private readonly List<SKRect> _dirtyRects = new();
    private readonly object _lock = new();

    public void Invalidate(SKRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        lock (_lock)
        {
            _dirtyRects.Add(rect);
        }
    }

    public void Invalidate(IEnumerable<SKRect> rects)
    {
        lock (_lock)
        {
            _dirtyRects.AddRange(rects);
        }
    }

    public List<SKRect> GetDirtyRegions()
    {
        lock (_lock)
        {
            return MergeRegions(_dirtyRects);
        }
    }

    private List<SKRect> MergeRegions(List<SKRect> rects)
    {
        if (rects.Count <= 1) return rects;

        var merged = new List<SKRect>();
        var toProcess = new List<SKRect>(rects);

        while (toProcess.Count > 0)
        {
            var current = toProcess[0];
            toProcess.RemoveAt(0);

            bool mergedAny = false;
            for (int i = 0; i < toProcess.Count; i++)
            {
                if (current.IntersectsWith(toProcess[i]))
                {
                    current = SKRect.Union(current, toProcess[i]);
                    toProcess.RemoveAt(i);
                    mergedAny = true;
                    i--;
                }
            }

            if (mergedAny)
            {
                toProcess.Add(current);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    public void ClearDirtyRegions()
    {
        lock (_lock)
        {
            _dirtyRects.Clear();
        }
    }
}