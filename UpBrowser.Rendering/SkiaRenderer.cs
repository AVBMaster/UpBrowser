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
    private float _scrollX;
    private float _scrollY;
    private float _contentOffsetY;

    public SKCanvas Canvas { get; private set; } = null!;
    public DisplayList? CurrentDisplayList => _currentDisplayList;

    public float DpiScale { get; set; } = 1.0f;

    public float ScrollX
    {
        get => _scrollX;
        set { _scrollX = value; InvalidateContent(); }
    }

    public float ScrollY
    {
        get => _scrollY;
        set { _scrollY = value; InvalidateContent(); }
    }

    public float ContentOffsetY
    {
        get => _contentOffsetY;
        set { _contentOffsetY = value; InvalidateContent(); }
    }

    private void InvalidateContent()
    {
        if (_dirtyManager != null)
        {
            _dirtyManager.Invalidate(new SKRect(0, _contentOffsetY, _width, _height));
        }
    }

    public void Initialize(int width, int height, bool enableDirtyRegions = true)
    {
        _width = width;
        _height = height;
        // 根据DPI缩放创建更大的bitmap，让内容在高DPI下更清晰
        int physicalWidth = (int)(width * DpiScale);
        int physicalHeight = (int)(height * DpiScale);
        _bitmap = new SKBitmap(physicalWidth, physicalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        Canvas = new SKCanvas(_bitmap);
        // 应用DPI缩放变换，这样所有绘制都会自动缩放
        Canvas.Scale(DpiScale, DpiScale);

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
        // 根据DPI缩放创建更大的bitmap
        int physicalWidth = (int)(width * DpiScale);
        int physicalHeight = (int)(height * DpiScale);
        _bitmap = new SKBitmap(physicalWidth, physicalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        Canvas = new SKCanvas(_bitmap);
        // 应用DPI缩放变换
        Canvas.Scale(DpiScale, DpiScale);

        if (_dirtyManager != null)
        {
            _dirtyManager.Invalidate(new SKRect(0, 0, width, height));
        }
    }

    public int Width => _width;
    public int Height => _height;
    public int PhysicalWidth => _bitmap?.Width ?? 0;
    public int PhysicalHeight => _bitmap?.Height ?? 0;

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

    public void RenderWithScroll(DisplayList displayList, float contentOffsetY, float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        _currentDisplayList = displayList;

        if (displayList == null)
            return;

        // 保存状态（Canvas已经有DPI缩放变换）
        Canvas.Save();

        // 裁剪到内容区域（视口）- viewportHeight 已经包含了 status bar 的偏移
        // 因为Canvas已经应用了DPI缩放，所以这里使用逻辑坐标
        var clipRect = new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight);
        Canvas.ClipRect(clipRect);

        // 应用滚动变换（使用逻辑坐标）
        Canvas.Translate(-scrollX, -scrollY);

        // 执行显示列表
        displayList.Execute(Canvas);

        // 恢复状态
        Canvas.Restore();
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