using System.Runtime.InteropServices;
using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public class SkiaRenderer : IDisposable
{
    private SKSurface? _gpuSurface;
    private GRContext? _grContext;
    private IntPtr _d3dDevice;
    private IntPtr _d3dContext;
    private bool _useGpu;

    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private bool _disposed;
    private int _width;
    private int _height;
    private float _dpiScale = 1.0f;

    private DisplayList? _currentDisplayList;

    private SKPicture? _cachedPicture;
    private bool _pictureDirty = true;

    private DirtyRegionManager? _dirtyManager;
    private bool _useDirtyRegions;

    public SKCanvas Canvas => _canvas!;
    public DisplayList? CurrentDisplayList => _currentDisplayList;
    public float DpiScale { get => _dpiScale; set => _dpiScale = value; }
    public int Width => _width;
    public int Height => _height;
    public int PhysicalWidth => _useGpu ? (int)(_width * _dpiScale) : (_bitmap?.Width ?? 0);
    public int PhysicalHeight => _useGpu ? (int)(_height * _dpiScale) : (_bitmap?.Height ?? 0);
    public bool UseGpu => _useGpu;
    public bool IsPageCacheValid => !_pictureDirty && _cachedPicture != null;

    /// <summary>
    /// Attempt to enable GPU acceleration via SkiaSharp GRContext.
    /// Falls back silently if GPU init fails (e.g. no GL/D3D runtime).
    /// All other optimizations (SKPicture caching, object pooling, spatial
    /// index, bulk pixel copy) are active regardless.
    /// </summary>
    public bool TryEnableGpu()
    {
        try
        {
            if (CreateD3D11Device(out _d3dDevice, out _d3dContext))
            {
                _grContext = GRContext.CreateGl();
                if (_grContext == null)
                {
                    Marshal.Release(_d3dDevice);
                    Marshal.Release(_d3dContext);
                    _d3dDevice = _d3dContext = IntPtr.Zero;
                    return false;
                }
                _useGpu = true;
                return true;
            }
        }
        catch
        {
            // GPU unavailable — will use software rendering
        }
        return false;
    }

    public void Initialize(int width, int height, bool enableDirtyRegions = true)
    {
        _width = width;
        _height = height;
        _pictureDirty = true;

        if (_useGpu)
        {
            CreateGpuSurface(width, height);
        }
        else
        {
            CreateCpuBitmap(width, height);
        }

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
        _pictureDirty = true;

        if (_useGpu)
        {
            _gpuSurface?.Dispose();
            _gpuSurface = null;
            CreateGpuSurface(width, height);
        }
        else
        {
            _bitmap?.Dispose();
            _bitmap = null;
            CreateCpuBitmap(width, height);
        }

        if (_dirtyManager != null)
            _dirtyManager.Invalidate(new SKRect(0, 0, width, height));
    }

    private void CreateGpuSurface(int width, int height)
    {
        int pw = (int)(width * _dpiScale);
        int ph = (int)(height * _dpiScale);
        var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        _gpuSurface = SKSurface.Create(_grContext!, false, info);
        _canvas = _gpuSurface!.Canvas;
        _canvas.Scale(_dpiScale, _dpiScale);
    }

    private void CreateCpuBitmap(int width, int height)
    {
        int pw = (int)(width * _dpiScale);
        int ph = (int)(height * _dpiScale);
        _bitmap = new SKBitmap(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        _canvas = new SKCanvas(_bitmap);
        _canvas.Scale(_dpiScale, _dpiScale);
    }

    public void SetDisplayList(DisplayList displayList)
    {
        _currentDisplayList = displayList;
    }

    public void InvalidatePageCache()
    {
        _pictureDirty = true;
    }

    public void Invalidate(SKRect rect)
    {
        if (_dirtyManager != null && _useDirtyRegions)
            _dirtyManager.Invalidate(rect);
        _pictureDirty = true;
    }

    public void InvalidateFull()
    {
        if (_dirtyManager != null)
            _dirtyManager.Invalidate(new SKRect(0, 0, _width, _height));
        _pictureDirty = true;
    }

    public void Render(DisplayList displayList)
    {
        _currentDisplayList = displayList;

        if (_useDirtyRegions && _dirtyManager != null)
        {
            var dirtyRects = _dirtyManager.GetDirtyRegions();
            if (dirtyRects.Count > 0)
            {
                RenderDirtyRegions(displayList, dirtyRects);
                return;
            }
        }

        Canvas.Clear(SKColors.White);
        _pictureDirty = true;
        CacheAndDrawPicture(displayList);
    }

    public void RenderWithScroll(DisplayList displayList, float contentOffsetY, float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        _currentDisplayList = displayList;

        if (displayList == null) return;

        Canvas.Save();

        var clipRect = new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight);
        Canvas.ClipRect(clipRect);

        Canvas.Translate(-scrollX, -scrollY);

        if (!_pictureDirty && _cachedPicture != null)
        {
            Canvas.DrawPicture(_cachedPicture);
        }
        else
        {
            var contentRect = new SKRect(0, 0, Math.Max(viewportWidth, 10000f), Math.Max(viewportHeight, 10000f));
            var recorder = new SKPictureRecorder();
            var recordCanvas = recorder.BeginRecording(contentRect);
            displayList.Execute(recordCanvas);
            _cachedPicture?.Dispose();
            _cachedPicture = recorder.EndRecording();
            _pictureDirty = false;

            Canvas.DrawPicture(_cachedPicture);
        }

        Canvas.Restore();
    }

    private void CacheAndDrawPicture(DisplayList displayList)
    {
        var contentRect = new SKRect(0, 0, 10000f, 10000f);
        var recorder = new SKPictureRecorder();
        var recordCanvas = recorder.BeginRecording(contentRect);
        displayList.Execute(recordCanvas);
        _cachedPicture?.Dispose();
        _cachedPicture = recorder.EndRecording();
        Canvas.DrawPicture(_cachedPicture);
    }

    private void RenderDirtyRegions(DisplayList displayList, List<SKRect> dirtyRects)
    {
        Canvas.Clear(SKColors.White);

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

        _dirtyManager!.ClearDirtyRegions();
        _pictureDirty = true;
    }

    public byte[] GetPixelData()
    {
        int pw = PhysicalWidth;
        int ph = PhysicalHeight;
        if (pw <= 0 || ph <= 0) return Array.Empty<byte>();

        int stride = pw * 4;
        byte[] pixels = new byte[ph * stride];

        if (_useGpu && _gpuSurface != null)
        {
            using var image = _gpuSurface.Snapshot();
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            unsafe
            {
                fixed (byte* p = pixels)
                {
                    image.ReadPixels(info, (IntPtr)p, stride);
                }
            }
        }
        else if (_bitmap != null)
        {
            var src = _bitmap.Bytes;
            var len = Math.Min(pixels.Length, src.Length);
            Buffer.BlockCopy(src, 0, pixels, 0, len);
        }

        return pixels;
    }

    // ── D3D11 interop for GPU acceleration ──

    private static bool CreateD3D11Device(out IntPtr device, out IntPtr context)
    {
        device = IntPtr.Zero;
        context = IntPtr.Zero;
        try
        {
            uint featureLevel = D3D11_FEATURE_LEVEL_11_0;
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D11_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                ref featureLevel, 1,
                D3D11_SDK_VERSION,
                out device,
                out _,
                out context);
            return hr >= 0;
        }
        catch
        {
            return false;
        }
    }

    private const uint D3D11_FEATURE_LEVEL_11_0 = 0xB000;
    private const uint D3D11_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        uint DriverType,
        IntPtr Software,
        uint Flags,
        [In] ref uint pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;

        _cachedPicture?.Dispose();

        if (_useGpu)
        {
            _gpuSurface?.Dispose();
            _grContext?.Dispose();
            if (_d3dDevice != IntPtr.Zero) Marshal.Release(_d3dDevice);
            if (_d3dContext != IntPtr.Zero) Marshal.Release(_d3dContext);
        }
        else
        {
            _canvas?.Dispose();
            _bitmap?.Dispose();
        }

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
