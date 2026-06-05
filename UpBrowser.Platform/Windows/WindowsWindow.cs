using System.Text;
using UpBrowser.Core;
using UpBrowser.Native.Windows;

namespace UpBrowser.Platform.Windows;

public class WindowsWindow : IWindow
{
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _isRunning;
    private Action<double>? _onFrame;
    private Action<double, double>? _onMouseWheel;
    private Action<Key>? _onKeyDown;
    private Action<Key>? _onKeyUp;
    private DateTime _lastFrameTime;
    private int _width;
    private int _height;
    private NativeWindow.WndProc? _wndProc;

    private WindowsImeHandler? _imeHandler;
    private IImeSupport? _imeTarget;
    private IntPtr _detachedImeContext;
    private bool _imeContextDetached;
    private float _dpiScale = 1.0f;
    private float _targetFrameTimeMs = 16.0f;

    private Action<char>? _onChar;
    private Action<char>? _onImeChar;
    private Func<char, Key, bool>? _onKeyDownWithChar;
    private Action<float, float>? _onMouseMove;
    private Action<float, float, bool>? _onMouseClick;
    private Action<float>? _onDpiChanged;
    private Action? _onSetFocus;
    private Action? _onKillFocus;

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

    public Action<double, double>? OnMouseWheel
    {
        get => _onMouseWheel;
        set => _onMouseWheel = value;
    }

    public Action<Key>? OnKeyDown
    {
        get => _onKeyDown;
        set => _onKeyDown = value;
    }

    public Action<Key>? OnKeyUp
    {
        get => _onKeyUp;
        set => _onKeyUp = value;
    }

    public float TargetFrameTimeMs
    {
        get => _targetFrameTimeMs;
        set => _targetFrameTimeMs = Math.Clamp(value, 1f, 100f);
    }

    public Action<float>? OnDpiChanged
    {
        get => _onDpiChanged;
        set => _onDpiChanged = value;
    }

    public Action? OnSetFocus
    {
        get => _onSetFocus;
        set => _onSetFocus = value;
    }

    public Action? OnKillFocus
    {
        get => _onKillFocus;
        set => _onKillFocus = value;
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

    public void SetImeTarget(IImeSupport? target)
    {
        bool changed = _imeTarget != target;
        _imeTarget = target;
        UpdateInputMethodAssociation();
        if (changed && _imeTarget != null)
        {
            UpdateImeCompositionWindow();
        }
    }

    private void UpdateInputMethodAssociation()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        bool shouldEnableIme = _imeTarget != null;

        if (shouldEnableIme)
        {
            if (!_imeContextDetached)
                return;

            if (_detachedImeContext != IntPtr.Zero)
            {
                _ = Imm32Interop.ImmAssociateContext(_hwnd, _detachedImeContext);
            }
            else
            {
                _ = Imm32Interop.ImmAssociateContextEx(_hwnd, IntPtr.Zero, 0x0010);
            }

            _detachedImeContext = IntPtr.Zero;
            _imeContextDetached = false;
            return;
        }

        if (_imeContextDetached)
            return;

        _detachedImeContext = Imm32Interop.ImmAssociateContext(_hwnd, IntPtr.Zero);
        _imeContextDetached = true;
    }

    public void UpdateImeCompositionWindow()
    {
        if (_imeTarget == null || _hwnd == IntPtr.Zero)
            return;

        var caretPos = _imeTarget.GetImeCaretPosition();

        IntPtr hIMC = Imm32Interop.ImmGetContext(_hwnd);
        if (hIMC == IntPtr.Zero)
            return;

        try
        {
            int caretX = (int)Math.Round(caretPos.X * _dpiScale);
            int caretY = (int)Math.Round(caretPos.Y * _dpiScale);

            var compForm = new Imm32Interop.COMPOSITIONFORM
            {
                dwStyle = Imm32Interop.CFS_POINT,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                }
            };

            Imm32Interop.ImmSetCompositionWindow(hIMC, ref compForm);

            var candForm = new Imm32Interop.CANDIDATEFORM
            {
                dwIndex = 0,
                dwStyle = Imm32Interop.CFS_CANDIDATEPOS,
                ptCurrentPos = new Imm32Interop.POINT
                {
                    X = caretX,
                    Y = caretY
                }
            };

            Imm32Interop.ImmSetCandidateWindow(hIMC, ref candForm);
        }
        finally
        {
            Imm32Interop.ImmReleaseContext(_hwnd, hIMC);
        }
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

        _detachedImeContext = Imm32Interop.ImmAssociateContext(_hwnd, IntPtr.Zero);
        _imeContextDetached = true;

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
                {
                    NativeWindow.BeginPaint(hWnd, out var ps);
                    NativeWindow.EndPaint(hWnd, ref ps);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_ERASEBKGND:
                return new IntPtr(1);

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
                    _dpiScale = dpi / 96.0f;
                    _onDpiChanged?.Invoke(_dpiScale);

                    unsafe
                    {
                        NativeWindow.RECT* suggestedRect = (NativeWindow.RECT*)lParam;
                        NativeWindow.SetWindowPos(_hwnd, IntPtr.Zero,
                            suggestedRect->Left, suggestedRect->Top,
                            suggestedRect->Right - suggestedRect->Left,
                            suggestedRect->Bottom - suggestedRect->Top,
                            0x0040);
                    }

                    UpdateImeCompositionWindow();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_MOUSEWHEEL:
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(0, rawDelta);
                    return IntPtr.Zero;
                }

            case 0x020E: // WM_MOUSEHWHEEL
                {
                    int rawDelta = (int)((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    _onMouseWheel?.Invoke(rawDelta, 0);
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_SETFOCUS:
                {
                    _onSetFocus?.Invoke();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_KILLFOCUS:
                {
                    _onKillFocus?.Invoke();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_STARTCOMPOSITION:
                {
                    _imeTarget?.OnImeCompositionStart();
                    UpdateImeCompositionWindow();
                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_COMPOSITION:
                {
                    if (_imeHandler == null)
                        return IntPtr.Zero;

                    int flags = (int)lParam;
                    var state = _imeHandler.GetCompositionState();

                    if ((flags & Imm32Interop.GCS_RESULTSTR) != 0)
                    {
                        if (!string.IsNullOrEmpty(state.CommittedText))
                        {
                            if (_imeTarget != null)
                            {
                                _imeTarget.OnImeCompositionEnd(state.CommittedText);
                            }
                            else
                            {
                                foreach (char c in state.CommittedText)
                                {
                                    _onImeChar?.Invoke(c);
                                }
                            }
                        }
                    }

                    if ((flags & Imm32Interop.GCS_COMPSTR) != 0)
                    {
                        if (!string.IsNullOrEmpty(state.CompositionText))
                        {
                            if (_imeTarget != null)
                            {
                                _imeTarget.OnImeCompositionUpdate(state.CompositionText, state.CursorPosition);
                                UpdateImeCompositionWindow();
                            }
                        }
                    }

                    return IntPtr.Zero;
                }

            case NativeWindow.WM_IME_ENDCOMPOSITION:
                {
                    _imeTarget?.OnImeCompositionEnd(null);
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

            case NativeWindow.WM_KEYUP:
                {
                    int virtualKey = wParam.ToInt32();
                    var key = (Key)virtualKey;
                    _onKeyUp?.Invoke(key);
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
            bool hasMessage = false;
            while (NativeWindow.PeekMessageW(out msg, IntPtr.Zero, 0, 0, 1))
            {
                hasMessage = true;
                if (msg.message == NativeWindow.WM_QUIT)
                {
                    _isRunning = false;
                    return;
                }
                NativeWindow.TranslateMessage(ref msg);
                NativeWindow.DispatchMessageW(ref msg);
            }

            if (_onFrame != null)
            {
                var now = DateTime.Now;
                var dt = (now - _lastFrameTime).TotalSeconds;
                double targetDt = _targetFrameTimeMs / 1000.0;

                if (dt >= targetDt)
                {
                    _lastFrameTime = now;
                    _onFrame(dt);
                }
                else if (!hasMessage)
                {
                    var elapsed = (DateTime.Now - _lastFrameTime).TotalMilliseconds;
                    int sleepMs = Math.Max(1, (int)(_targetFrameTimeMs + 0.5 - elapsed));
                    if (sleepMs > 0)
                        Thread.Sleep(sleepMs);
                }
            }
        }
    }

    public (int width, int height) GetClientSize() => (_width, _height);

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
        if (_hwnd == IntPtr.Zero || pixels.Length == 0 || width <= 0 || height <= 0) return;

        var bmi = new NativeWindow.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(NativeWindow.BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        var hdc = NativeWindow.GetDC(_hwnd);

        // Stretch to full window client area to handle ResolutionScale < 1.0
        NativeWindow.GetClientRect(_hwnd, out var clientRect);
        int destW = clientRect.Right - clientRect.Left;
        int destH = clientRect.Bottom - clientRect.Top;

        NativeWindow.StretchDIBits(hdc, 0, 0, destW, destH,
            0, 0, width, height, pixels, ref bmi,
            NativeWindow.DIB_RGB_COLORS, NativeWindow.SRCCOPY);

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