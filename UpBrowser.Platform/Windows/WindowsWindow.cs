using System.Runtime.InteropServices;
using System.Text;
using UpBrowser.Native.Windows;

namespace UpBrowser.Platform.Windows;

public class WindowsWindow : IWindow
{
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _isRunning;
    private Action<double>? _onFrame;
    private Action<double>? _onMouseWheel;
    private Action<Key>? _onKeyDown;
    private DateTime _lastFrameTime;
    private int _width;
    private int _height;
    private NativeWindow.WndProc? _wndProc;

    private WindowsImeHandler? _imeHandler;

    private Action<char>? _onChar;
    private Action<char>? _onImeChar;
    private Func<char, Key, bool>? _onKeyDownWithChar;
    private Action<float, float>? _onMouseMove;
    private Action<float, float, bool>? _onMouseClick;
    private Action<float>? _onDpiChanged;

    public Action<char>? OnChar
    {
        get => _onChar;
        set => _onChar = value;
    }

    public Action<char>? OnImeChar
    {
        get => _onImeChar;
        set => _onImeChar = value;
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

    public Action<Key>? OnKeyDown
    {
        get => _onKeyDown;
        set => _onKeyDown = value;
    }

    public Action<float>? OnDpiChanged
    {
        get => _onDpiChanged;
        set => _onDpiChanged = value;
    }

    public int Width => _width;
    public int Height => _height;
    public IntPtr Handle => _hwnd;

    public IImeHandler? ImeHandler => _imeHandler;

    public WindowsWindow(int width, int height, string title)
    {
        _width = width;
        _height = height;
        Initialize(width, height, title);
    }

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

        _imeHandler = new WindowsImeHandler(_hwnd);

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
                {
                    _width = (int)(lParam.ToInt64() & 0xFFFF);
                    _height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    NativeWindow.InvalidateRect(_hwnd, IntPtr.Zero, true);
                    NativeWindow.UpdateWindow(_hwnd);

                    if (_onFrame != null && _width > 0 && _height > 0)
                    {
                        var now = DateTime.Now;
                        _lastFrameTime = now;
                        _onFrame(0.016);
                    }
                    return IntPtr.Zero;
                }

            case 0x02E0:
                {
                    uint dpi = (uint)(wParam.ToInt64() & 0xFFFF);
                    float newDpiScale = dpi / 96.0f;
                    _onDpiChanged?.Invoke(newDpiScale);

                    unsafe
                    {
                        RECT* suggestedRect = (RECT*)lParam;
                        NativeWindow.SetWindowPos(_hwnd, IntPtr.Zero,
                            suggestedRect->Left, suggestedRect->Top,
                            suggestedRect->Right - suggestedRect->Left,
                            suggestedRect->Bottom - suggestedRect->Top,
                            0x0040);
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEWHEEL:
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(rawDelta);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_STARTCOMPOSITION:
                {
                    _imeHandler?.GetCompositionState();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_COMPOSITION:
                {
                    if (_imeHandler != null)
                    {
                        var state = _imeHandler.GetCompositionState();
                        if (!state.IsComposing)
                        {
                            if (!string.IsNullOrEmpty(state.CommittedText))
                            {
                                foreach (char c in state.CommittedText)
                                {
                                    _onImeChar?.Invoke(c);
                                }
                            }
                        }
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_ENDCOMPOSITION:
                {
                    _imeHandler?.Reset();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_NOTIFY:
                return IntPtr.Zero;

            case NativeWindow.WM_CHAR:
                {
                    char charCode = (char)(wParam.ToInt32() & 0xFFFF);

                    if (_onKeyDownWithChar != null)
                    {
                        _onKeyDownWithChar(charCode, Key.Unknown);
                    }
                    else if (_onChar != null)
                    {
                        _onChar(charCode);
                    }
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_KEYDOWN:
                {
                    int virtualKey = wParam.ToInt32();
                    var key = (Key)virtualKey;

                    if (_onKeyDownWithChar != null)
                    {
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
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEMOVE:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseMove?.Invoke(mouseX, mouseY);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_LBUTTONUP:
                {
                    int mouseX = (int)(lParam.ToInt64() & 0xFFFF);
                    int mouseY = (int)((lParam.ToInt64() >> 16) & 0xFFFF);

                    _onMouseClick?.Invoke(mouseX, mouseY, false);
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

    public bool PumpPendingMessage()
    {
        NativeWindow.MSG msg;
        if (NativeWindow.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1))
        {
            if (msg.message == NativeWindow.WM_QUIT)
            {
                _isRunning = false;
                return false;
            }
            NativeWindow.TranslateMessage(ref msg);
            NativeWindow.DispatchMessageW(ref msg);
            return true;
        }
        return false;
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