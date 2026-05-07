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

    private Action<char>? _onChar;
    private Func<char, Key, bool>? _onKeyDownWithChar;
    private Action<float, float>? _onMouseMove;
    private Action<float, float, bool>? _onMouseClick;

    public Action<char>? OnChar
    {
        get => _onChar;
        set => _onChar = value;
    }

    public Func<char, Key, bool>? OnKeyDownWithChar
    {
        get => _onKeyDownWithChar;
        set => _onKeyDownWithChar = value;
    }

    public Action<float, float>? OnMouseMove
    {
        get => _onMouseMove;
        set => _onMouseMove = value;
    }

    public Action<float, float, bool>? OnMouseClick
    {
        get => _onMouseClick;
        set => _onMouseClick = value;
    }

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

    public (int width, int height) GetClientSize()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0);
        GetClientRect(_hwnd, out RECT rect);
        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

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
            throw new InvalidOperationException("Failed to create window");

        NativeWindow.ShowWindow(_hwnd, NativeWindow.SW_SHOWNORMAL);
        NativeWindow.UpdateWindow(_hwnd);
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
                return IntPtr.Zero;

            case NativeWindow.WM_MOUSEWHEEL:
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(rawDelta);
                    return IntPtr.Zero;
                }

            // ========== 关键修复：WM_CHAR 处理字符输入 ==========
            case NativeWindow.WM_CHAR:
                {
                    char charCode = (char)(wParam.ToInt32() & 0xFFFF);
                    // 过滤掉控制字符（除了退格、Tab等）
                    // WM_CHAR 中 Backspace 不会出现，它走 WM_KEYDOWN

                    if (_onKeyDownWithChar != null)
                    {
                        // 将 WM_CHAR 作为带字符的按键事件发送
                        // key 传 Unknown(0) 表示这是一个字符输入
                        _onKeyDownWithChar(charCode, Key.Unknown);
                    }
                    else if (_onChar != null)
                    {
                        _onChar(charCode);
                    }
                    return IntPtr.Zero;
                }

            // ========== 关键修复：WM_KEYDOWN 处理非字符按键 ==========
            case NativeWindow.WM_KEYDOWN:
                {
                    int virtualKey = wParam.ToInt32();
                    var key = (Key)virtualKey;

                    // 记录按键用于调试
                    // Console.WriteLine($"WM_KEYDOWN: vk={virtualKey}, key={key}");

                    if (_onKeyDownWithChar != null)
                    {
                        // 对于非字符按键，传 '\0' 作为字符
                        bool handled = _onKeyDownWithChar('\0', key);
                        if (handled) return IntPtr.Zero;
                    }

                    _onKeyDown?.Invoke(key);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_LBUTTONDOWN:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseClick?.Invoke(mouseX, mouseY, true);

                    if (_width > 0 && _height > 0)
                    {
                        float contentOffset = 75;
                        float statusBarHeight = 20;
                        float scrollbarWidth = 12;

                        float scrollbarLeft = _width - scrollbarWidth;
                        float contentTop = contentOffset;
                        float contentBottom = _height - statusBarHeight;
                        float trackHeight = contentBottom - contentTop;

                        if (mouseX >= scrollbarLeft && mouseY >= contentTop && mouseY <= contentBottom)
                        {
                            NativeWindow.SetCapture(_hwnd);

                            float middleTop = contentTop + trackHeight * 0.2f;
                            float middleBottom = contentTop + trackHeight * 0.8f;

                            if (mouseY < middleTop)
                                _onScrollbarClick?.Invoke(true, true);
                            else if (mouseY > middleBottom)
                                _onScrollbarClick?.Invoke(true, false);
                            else
                            {
                                _isDraggingVertical = true;
                                _dragStartY = mouseY;
                            }
                        }

                        float horizontalBarTop = contentBottom - scrollbarWidth;
                        if (mouseY >= horizontalBarTop && mouseY <= contentBottom && mouseX < scrollbarLeft)
                        {
                            NativeWindow.SetCapture(_hwnd);
                            float trackWidth = scrollbarLeft;
                            float thumbWidth = Math.Max(20, trackWidth * 0.3f);

                            if (mouseX < thumbWidth)
                                _onScrollbarClick?.Invoke(false, true);
                            else if (mouseX > scrollbarLeft - thumbWidth)
                                _onScrollbarClick?.Invoke(false, false);
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
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseMove?.Invoke(mouseX, mouseY);

                    if (_isDraggingVertical && _onScrollbarDrag != null)
                    {
                        float deltaY = mouseY - _dragStartY;
                        _dragStartY = mouseY;
                        _onScrollbarDrag(0, deltaY);
                    }
                    else if (_isDraggingHorizontal && _onScrollbarDrag != null)
                    {
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
                    break;

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
        if (_hwnd == IntPtr.Zero || pixels.Length == 0) return;

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
                    dst[dstIdx] = pixels[srcIdx + 2];
                    dst[dstIdx + 1] = pixels[srcIdx + 1];
                    dst[dstIdx + 2] = pixels[srcIdx];
                    dst[dstIdx + 3] = pixels[srcIdx + 3];
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