using System.Runtime.InteropServices;
using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Performance;

namespace UpBrowser.Rendering;

public class SkiaRenderer : IDisposable
{
    private SKSurface? _gpuSurface;
    private GRContext? _grContext;
    private bool _useGpu;
    private bool _gpuFailed;

    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private bool _disposed;
    private int _width;
    private int _height;
    private float _dpiScale = 1.0f;
    private float _resolutionScale = 1.0f;

    private DisplayList? _currentDisplayList;

    private SKPicture? _cachedPicture;
    private bool _pictureDirty = true;

    private DirtyRegionManager? _dirtyManager;
    private bool _useDirtyRegions;

    // OpenGL context for GPU backend
    private IntPtr _glDummyWindow;
    private IntPtr _glDC;
    private IntPtr _glRC;

    // FPS counter
    private long _lastFrameTick = Environment.TickCount64;
    private double _currentFps;
    private SKTypeface? _fpsTypeface;

    // Rendering settings
    private RenderingSettings? _settings;
    private AntiAliasMode _currentAaMode = AntiAliasMode.Normal;

    public SKCanvas Canvas => _canvas!;
    public DisplayList? CurrentDisplayList => _currentDisplayList;
    public float DpiScale { get => _dpiScale; set => _dpiScale = value; }
    public float ResolutionScale
    {
        get => _resolutionScale;
        set
        {
            if (Math.Abs(_resolutionScale - value) > 0.01f)
            {
                _resolutionScale = Math.Clamp(value, 0.25f, 3.0f);
                _pictureDirty = true;
            }
        }
    }
    public int Width => _width;
    public int Height => _height;
    public int PhysicalWidth => (int)(_width * _dpiScale);
    public int PhysicalHeight => (int)(_height * _dpiScale);
    public bool UseGpu => _useGpu;
    public bool IsPageCacheValid => !_pictureDirty && _cachedPicture != null;
    public RenderingSettings? Settings
    {
        get => _settings;
        set
        {
            if (_settings != null)
                _settings.OnChanged -= ApplySettings;
            _settings = value;
            if (_settings != null)
            {
                _settings.OnChanged += ApplySettings;
                ApplySettings();
            }
        }
    }

    private void ApplySettings()
    {
        if (_settings == null) return;

        bool resolutionChanged = Math.Abs(_resolutionScale - _settings.ResolutionScale) > 0.01f;
        _resolutionScale = _settings.ResolutionScale;
        _currentAaMode = _settings.AntiAliasing;
        _useDirtyRegions = _settings.DirtyRegions;

        if (!_settings.DirtyRegions)
            _dirtyManager?.ClearDirtyRegions();

        if (!_settings.PictureCaching)
        {
            _cachedPicture?.Dispose();
            _cachedPicture = null;
        }

        // Invalidate page cache on any setting change that affects rendering
        _pictureDirty = true;

        // Handle GPU toggle via settings
        if (_settings.GpuAcceleration && !_useGpu && !_gpuFailed)
        {
            if (TryEnableGpu())
            {
                Console.WriteLine("[Settings] GPU acceleration enabled");
            }
            else
            {
                _gpuFailed = true;
                Console.WriteLine("[Settings] GPU acceleration failed, staying on CPU");
            }
        }
        else if (!_settings.GpuAcceleration && _useGpu)
        {
            DisableGpu();
            Console.WriteLine("[Settings] GPU acceleration disabled");
        }

        // Only recreate surface for GPU toggle or AA mode change (AA needs surface format)
        if (resolutionChanged) return;
        RecreateSurface();
    }

    public bool TrySetGpu(bool enable)
    {
        if (enable == _useGpu) return true;
        if (!enable)
        {
            DisableGpu();
            return true;
        }
        return TryEnableGpu();
    }

    private void DisableGpu()
    {
        if (!_useGpu) return;

        _gpuSurface?.Dispose();
        _gpuSurface = null;
        _grContext?.Dispose();
        _grContext = null;
        CleanupGlContext();
        _useGpu = false;

        _canvas?.Dispose();
        _canvas = null;
        CreateCpuBitmap(_width, _height);
        _pictureDirty = true;
        Console.WriteLine("[GPU] GPU acceleration disabled, switched to CPU");
    }

    /// <summary>
    /// Attempt to enable GPU acceleration via SkiaSharp GRContext.
    /// Creates a platform-appropriate OpenGL context, then uses GRContext.CreateGl().
    /// Falls back to CPU software rendering if GPU init fails.
    /// </summary>
    public bool TryEnableGpu()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return TryEnableGpuWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return TryEnableGpuLinux();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return TryEnableGpuMac();

            Console.WriteLine("[GPU] No GPU support for this platform");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GPU] Init failed: {ex.GetType().Name}: {ex.Message}");
            CleanupGlContext();
            return false;
        }
    }

    private bool TryEnableGpuWindows()
    {
        const uint WS_POPUP = 0x80000000;
        var hInstance = GetModuleHandleW(null);
        _glDummyWindow = CreateWindowExW(0, "STATIC", "",
            WS_POPUP, 0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_glDummyWindow == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] Failed to create dummy window");
            return false;
        }

        _glDC = GetDC(_glDummyWindow);
        if (_glDC == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] Failed to get DC");
            CleanupGlWindow();
            return false;
        }

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 32,
            cAlphaBits = 8,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = 0
        };

        var pixelFormat = ChoosePixelFormat(_glDC, ref pfd);
        if (pixelFormat == 0)
        {
            Console.WriteLine("[GPU] ChoosePixelFormat failed");
            CleanupGlWindow();
            return false;
        }

        if (!SetPixelFormat(_glDC, pixelFormat, ref pfd))
        {
            Console.WriteLine("[GPU] SetPixelFormat failed");
            CleanupGlWindow();
            return false;
        }

        _glRC = wglCreateContext(_glDC);
        if (_glRC == IntPtr.Zero)
        {
            Console.WriteLine("[GPU] wglCreateContext failed");
            CleanupGlWindow();
            return false;
        }

        if (!wglMakeCurrent(_glDC, _glRC))
        {
            Console.WriteLine("[GPU] wglMakeCurrent failed");
            CleanupGlWindow();
            return false;
        }

        _grContext = GRContext.CreateGl();
        if (_grContext == null)
        {
            Console.WriteLine("[GPU] GRContext.CreateGl returned null");
            CleanupGlContext();
            return false;
        }

        _useGpu = true;
        Console.WriteLine("[GPU] OpenGL GPU acceleration enabled (Windows)");
        return true;
    }

    private bool TryEnableGpuLinux()
    {
        Console.WriteLine("[GPU] GPU acceleration on Linux not yet implemented, using CPU");
        return false;
    }

    private bool TryEnableGpuMac()
    {
        Console.WriteLine("[GPU] GPU acceleration on macOS not yet implemented, using CPU");
        return false;
    }

    public void Initialize(int width, int height, bool enableDirtyRegions = true)
    {
        _width = width;
        _height = height;
        _pictureDirty = true;

        if (_useGpu)
        {
            MakeGlCurrent();
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
            MakeGlCurrent();
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

    private void RecreateSurface()
    {
        if (_width <= 0 || _height <= 0) return;

        if (_useGpu)
        {
            MakeGlCurrent();
            _gpuSurface?.Dispose();
            _gpuSurface = null;
            CreateGpuSurface(_width, _height);
        }
        else
        {
            _bitmap?.Dispose();
            _bitmap = null;
            CreateCpuBitmap(_width, _height);
        }

        _pictureDirty = true;
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

    private void MakeGlCurrent()
    {
        if (_glRC != IntPtr.Zero && _glDC != IntPtr.Zero)
            wglMakeCurrent(_glDC, _glRC);
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

        if (_useGpu) MakeGlCurrent();

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

    public void RenderWithResolutionScale(DisplayList displayList, float contentOffsetY, float viewportWidth, float viewportHeight)
    {
        _currentDisplayList = displayList;
        if (_useGpu) MakeGlCurrent();

        float resScale = _settings?.ResolutionScale ?? 1.0f;

        Canvas.Save();
        Canvas.ClipRect(new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight));
        Canvas.Scale(resScale, resScale);
        Canvas.Translate(0, contentOffsetY * (1f / resScale - 1f));

        Canvas.Clear(SKColors.White);
        CacheAndDrawPicture(displayList);

        Canvas.Restore();
    }

    public void RenderWithScroll(DisplayList displayList, float contentOffsetY, float scrollX, float scrollY, float viewportWidth, float viewportHeight)
    {
        _currentDisplayList = displayList;

        if (displayList == null) return;

        if (_useGpu) MakeGlCurrent();

        var sw = Clock.NowNanos();

        float resScale = _settings?.ResolutionScale ?? 1.0f;

        Canvas.Save();

        // Clip to content area (logical coords in DPI-scaled space)
        Canvas.ClipRect(new SKRect(0, contentOffsetY, viewportWidth, contentOffsetY + viewportHeight));

        // Apply resolution scale only to page content, anchored at content origin
        // Scale then translate to keep content aligned with chrome bottom
        Canvas.Scale(resScale, resScale);
        Canvas.Translate(0, contentOffsetY * (1f / resScale - 1f));
        Canvas.Translate(-scrollX, -scrollY);

        bool useCaching = _settings?.PictureCaching ?? true;
        if (useCaching && !_pictureDirty && _cachedPicture != null)
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

        PipelineTimings.Composite.AddSample(Clock.NowNanos() - sw);
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

    public void TickFrame()
    {
        long now = Environment.TickCount64;
        double elapsed = now - _lastFrameTick;
        _lastFrameTick = now;

        if (elapsed <= 0 || elapsed > 1000) return;

        // Exponential smoothing for stable readings
        double instantFps = 1000.0 / elapsed;
        const double alpha = 0.05;
        _currentFps = _currentFps > 0
            ? alpha * instantFps + (1 - alpha) * _currentFps
            : instantFps;
    }

    public void RenderFpsCounter(SKCanvas canvas, float windowWidth, float windowHeight)
    {
        if (_settings == null || !_settings.ShowFps) return;

        _fpsTypeface ??= FontHelper.GetChineseTypeface() ?? SKTypeface.Default;

        using var font = new SKFont(_fpsTypeface, 13);
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 180),
            Style = SKPaintStyle.Fill
        };
        using var textPaint = new SKPaint
        {
            Color = _currentFps >= 55 ? SKColor.Parse("#4CAF50") :
                    _currentFps >= 30 ? SKColor.Parse("#FFC107") :
                    SKColor.Parse("#F44336"),
            IsAntialias = true
        };

        string fpsText = $"FPS: {_currentFps:F0}";
        if (_useGpu) fpsText += " GPU";
        else fpsText += " CPU";

        string resText = $"{_resolutionScale:F1}×";
        string info = $"{fpsText} | {resText}";

        // Append the live pipeline timings so the HUD doubles as a quick perf readout.
        if (PipelineTimings.Style.Count > 0 || PipelineTimings.Layout.Count > 0)
        {
            info += $" | S:{PipelineTimings.Style.MeanMillis:F1}ms L:{PipelineTimings.Layout.MeanMillis:F1}ms";
        }

        float pad = 6;
        float textW = font.MeasureText(info);
        float boxW = textW + pad * 2 + 4;
        float boxH = 22;
        float boxX = 8;
        float boxY = windowHeight - boxH - 8;

        canvas.DrawRoundRect(boxX, boxY, boxW, boxH, 4, 4, bgPaint);
        canvas.DrawText(info, boxX + pad + 2, boxY + boxH * 0.72f, SKTextAlign.Left, font, textPaint);
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
            MakeGlCurrent();
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

    // ── OpenGL P/Invoke ──

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    private const uint PFD_SUPPORT_OPENGL = 0x00000020;
    private const uint PFD_DOUBLEBUFFER = 0x00000001;
    private const byte PFD_TYPE_RGBA = 0;

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("gdi32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // ── Cleanup helpers ──

    private void CleanupGlContext()
    {
        if (_glRC != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_glRC);
            _glRC = IntPtr.Zero;
        }
        CleanupGlWindow();
    }

    private void CleanupGlWindow()
    {
        if (_glDC != IntPtr.Zero && _glDummyWindow != IntPtr.Zero)
        {
            ReleaseDC(_glDummyWindow, _glDC);
            _glDC = IntPtr.Zero;
        }
        if (_glDummyWindow != IntPtr.Zero)
        {
            DestroyWindow(_glDummyWindow);
            _glDummyWindow = IntPtr.Zero;
        }
    }

    // ── IDisposable ──

    public void Dispose()
    {
        if (_disposed) return;

        _cachedPicture?.Dispose();

        if (_useGpu)
        {
            _gpuSurface?.Dispose();
            _grContext?.Dispose();
            CleanupGlContext();
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
