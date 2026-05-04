using System.Runtime.InteropServices;
using UpBrowser.Platform.Windows;

namespace UpBrowser.Platform;

public class BrowserWindow : IDisposable
{
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _isRunning;
    private Action<double>? _onFrame;
    private Action<double>? _onMouseWheel;
    private Action<bool, bool>? _onScrollbarClick;
    private Action<float, float>? _onScrollbarDrag;
    private bool _isDraggingVertical;
    private bool _isDraggingHorizontal;
    private float _dragStartY;
    private Action<Key>? _onKeyDown;
    private DateTime _lastFrameTime;
    private int _width;
    private int _height;
    private NativeWindow.WndProc? _wndProc;

    public Action<double>? OnMouseWheel
    {
        get => _onMouseWheel;
        set => _onMouseWheel = value;
    }
    
    public Action<bool, bool>? OnScrollbarClick
    {
        get => _onScrollbarClick;
        set => _onScrollbarClick = value;
    }

    public Action<float, float>? OnScrollbarDrag
    {
        get => _onScrollbarDrag;
        set => _onScrollbarDrag = value;
    }

    public Action<Key>? OnKeyDown
    {
        get => _onKeyDown;
        set => _onKeyDown = value;
    }

    private void TriggerFrameRender()
    {
        if (_onFrame != null)
        {
            var now = DateTime.Now;
            var dt = (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;
            _onFrame(dt);
        }
    }

    public IntPtr Handle => _hwnd;
    public int Width => _width;
    public int Height => _height;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    public (int width, int height) GetClientSize()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0);
        GetClientRect(_hwnd, out RECT rect);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static BrowserWindow? _instance;

    public static BrowserWindow Create(int width, int height, string title)
    {
        var instance = new BrowserWindow();
        instance._width = width;
        instance._height = height;
        instance.Initialize(width, height, title);
        return instance;
    }

    private unsafe void Initialize(int width, int height, string title)
    {
        var hInstance = NativeWindow.GetModuleHandleW(null);

        _wndProc = new NativeWindow.WndProc(WndProc);
        NativeWindow.GetOrRegisterClass(_wndProc, hInstance);

        _hwnd = NativeWindow.CreateWindowExW(
            0,
            NativeWindow.GetClassName(),
            title,
            NativeWindow.WS_OVERLAPPEDWINDOW | NativeWindow.WS_VISIBLE,
            NativeWindow.CW_USEDEFAULT,
            NativeWindow.CW_USEDEFAULT,
            width,
            height,
            NativeWindow.HWND_DESKTOP,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create window");
        }

        NativeWindow.ShowWindow(_hwnd, NativeWindow.SW_SHOWNORMAL);
        NativeWindow.UpdateWindow(_hwnd);

        _instance = this;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeWindow.WM_DESTROY:
                _isRunning = false;
                NativeWindow.PostQuitMessage(0);
                return IntPtr.Zero;

            case NativeWindow.WM_PAINT:
            case NativeWindow.WM_ERASEBKGND:
            case NativeWindow.WM_NCPAINT:
                return IntPtr.Zero;

            case NativeWindow.WM_SIZE:
                _width = (int)(lParam.ToInt64() & 0xFFFF);
                _height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                TriggerFrameRender();
                return IntPtr.Zero;

            case NativeWindow.WM_ENTERSIZEMOVE:
            case NativeWindow.WM_EXITSIZEMOVE:
                TriggerFrameRender();
                return IntPtr.Zero;

            case NativeWindow.WM_MOUSEWHEEL:
                // 正确提取滚轮delta：wParam高16位是滚轮值，通常120的倍数
                // 正数表示向上滚动（滚轮远离用户）
                int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                _onMouseWheel?.Invoke(rawDelta);
                return IntPtr.Zero;

            case NativeWindow.WM_KEYDOWN:
                int virtualKey = wParam.ToInt32();
                _onKeyDown?.Invoke((Key)virtualKey);
                return IntPtr.Zero;

            case NativeWindow.WM_LBUTTONDOWN:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    
                    if (_width > 0 && _height > 0)
                    {
                        float contentOffset = 75;
                        float statusBarHeight = 20;
                        float scrollbarWidth = 12;
                        
                        float scrollbarLeft = _width - scrollbarWidth;
                        float contentTop = contentOffset;
                        float contentBottom = _height - statusBarHeight;
                        float trackHeight = contentBottom - contentTop;
                        
                        // 垂直滚动条区域
                        if (mouseX >= scrollbarLeft && mouseY >= contentTop && mouseY <= contentBottom)
                        {
                            NativeWindow.SetCapture(_hwnd);
                            
                            // 简化处理：点击上半部分PageUp，下半部分PageDown，中间区域拖拽
                            float middleTop = contentTop + trackHeight * 0.2f;
                            float middleBottom = contentTop + trackHeight * 0.8f;
                            
                            if (mouseY < middleTop)
                            {
                                _onScrollbarClick?.Invoke(true, true); // PageUp
                            }
                            else if (mouseY > middleBottom)
                            {
                                _onScrollbarClick?.Invoke(true, false); // PageDown
                            }
                            else
                            {
                                // 点击中间区域，拖拽
                                _isDraggingVertical = true;
                                _dragStartY = mouseY;
                            }
                        }
                        
                        // 水平滚动条区域
                        float horizontalBarTop = contentBottom - scrollbarWidth;
                        if (mouseY >= horizontalBarTop && mouseY <= contentBottom && mouseX < scrollbarLeft)
                        {
                            NativeWindow.SetCapture(_hwnd);
                            float trackWidth = scrollbarLeft;
                            float thumbWidth = Math.Max(20, trackWidth * 0.3f);
                            
                            if (mouseX < thumbWidth)
                                _onScrollbarClick?.Invoke(false, true); // PageLeft
                            else if (mouseX > scrollbarLeft - thumbWidth)
                                _onScrollbarClick?.Invoke(false, false); // PageRight
                            else
                            {
                                _isDraggingHorizontal = true;
                                _dragStartY = mouseX;
                            }
                        }
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEMOVE:
                {
                    if (_isDraggingVertical && _onScrollbarDrag != null)
                    {
                        int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                        float deltaY = mouseY - _dragStartY;
                        _dragStartY = mouseY;
                        _onScrollbarDrag(0, deltaY);
                    }
                    else if (_isDraggingHorizontal && _onScrollbarDrag != null)
                    {
                        int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                        float deltaX = mouseX - _dragStartY;
                        _dragStartY = mouseX;
                        _onScrollbarDrag(deltaX, 0);
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_LBUTTONUP:
                {
                    _isDraggingVertical = false;
                    _isDraggingHorizontal = false;
                    NativeWindow.ReleaseCapture();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_CLOSE:
                _isRunning = false;
                NativeWindow.DestroyWindow(_hwnd);
                return IntPtr.Zero;

            default:
                return NativeWindow.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    public void Run(Action<double> onFrame)
    {
        if (_hwnd == IntPtr.Zero) return;

        _onFrame = onFrame;
        _lastFrameTime = DateTime.Now;
        _isRunning = true;

        NativeWindow.MSG msg;
        while (_isRunning)
        {
            if (NativeWindow.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1))
            {
                if (msg.message == NativeWindow.WM_QUIT)
                {
                    break;
                }

                NativeWindow.TranslateMessage(ref msg);
                NativeWindow.DispatchMessageW(ref msg);
            }

            if (_onFrame != null)
            {
                var now = DateTime.Now;
                var dt = (now - _lastFrameTime).TotalSeconds;
                _lastFrameTime = now;
                _onFrame(dt);
            }
        }
    }

    public unsafe void Render(byte[] pixels, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (pixels.Length == 0) return;

        var hdc = NativeWindow.GetDC(_hwnd);
        var memDC = NativeWindow.CreateCompatibleDC(hdc);

        var bmi = new NativeWindow.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(NativeWindow.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        IntPtr bits;
        var hBitmap = NativeWindow.CreateDIBSection(hdc, ref bmi, NativeWindow.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
        
        if (hBitmap != IntPtr.Zero && bits != IntPtr.Zero)
        {
            // Convert RGBA to BGRA and flip vertically
            byte* dst = (byte*)bits.ToPointer();
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * rowBytes;
                int dstRow = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = srcRow + x * 4;
                    int dstIdx = dstRow + x * 4;
                    dst[dstIdx] = pixels[srcIdx + 2];     // B <- R
                    dst[dstIdx + 1] = pixels[srcIdx + 1]; // G <- G
                    dst[dstIdx + 2] = pixels[srcIdx];     // R <- B
                    dst[dstIdx + 3] = pixels[srcIdx + 3]; // A <- A
                }
            }
            
            var oldBitmap = NativeWindow.SelectObject(memDC, hBitmap);
            NativeWindow.BitBlt(hdc, 0, 0, width, height, memDC, 0, 0, NativeWindow.SRCCOPY);
            NativeWindow.SelectObject(memDC, oldBitmap);
            NativeWindow.DeleteObject(hBitmap);
        }

        NativeWindow.DeleteDC(memDC);
        NativeWindow.ReleaseDC(_hwnd, hdc);
    }

    public void Close()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeWindow.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _isRunning = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}