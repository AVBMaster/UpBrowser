using System.Runtime.InteropServices;
using UpBrowser.Core;

namespace UpBrowser.Platform.Linux;

/// <summary>
/// Linux window implementation using X11.
/// Provides native window creation, event handling, and pixel buffer rendering.
/// </summary>
public class LinuxWindow : IWindow
{
    private IntPtr _display;
    private IntPtr _window;
    private IntPtr _gc;
    private IntPtr _image;
    private bool _disposed;
    private int _width;
    private int _height;
    private string _title;
    private byte[]? _pixelBuffer;
    private int _bufferSize;

    public Action<char>? OnChar { get; set; }
    public Action<char>? OnImeChar { get; set; }
    public Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    public Action<Key>? OnKeyDown { get; set; }
    public Action<float, float>? OnMouseMove { get; set; }
    public Action<float, float, bool>? OnMouseClick { get; set; }
    public Action<double>? OnMouseWheel { get; set; }
    public Action<float>? OnDpiChanged { get; set; }
    public Action? OnSetFocus { get; set; }
    public Action? OnKillFocus { get; set; }

    public int Width => _width;
    public int Height => _height;
    public IImeHandler? ImeHandler => null;

    public LinuxWindow(int width, int height, string title)
    {
        _width = width;
        _height = height;
        _title = title;
    }

    public (int width, int height) GetClientSize() => (_width, _height);

    public void SetImeTarget(IImeSupport? target) { }
    public void UpdateImeCompositionWindow() { }

    public void Run(Action<double> onFrame)
    {
        InitX11();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;
        const double frameInterval = 1.0 / 60.0;

        XFlush(_display);

        while (true)
        {
            while (XPending(_display) > 0)
            {
                XNextEvent(_display, out var xevent);

                switch (xevent.type)
                {
                    case 2: // KeyPress
                        var keySym = XLookupString(ref xevent, null, 0, out _, out _);
                        var ch = (char)keySym;
                        if (ch >= 32 && ch <= 126)
                            OnChar?.Invoke(ch);
                        else
                            OnKeyDown?.Invoke(MapKey(keySym));
                        break;

                    case 4: // ButtonPress
                        var bx = (float)xevent.xbutton.x;
                        var by = (float)xevent.xbutton.y;
                        if (xevent.xbutton.button == 4)
                            OnMouseWheel?.Invoke(-1.0);
                        else if (xevent.xbutton.button == 5)
                            OnMouseWheel?.Invoke(1.0);
                        else
                            OnMouseClick?.Invoke(bx, by, xevent.xbutton.button == 1);
                        break;

                    case 6: // MotionNotify
                        OnMouseMove?.Invoke((float)xevent.xmotion.x, (float)xevent.xmotion.y);
                        break;

                    case 12: // Expose
                        if (_pixelBuffer != null)
                            Render(_pixelBuffer, _width, _height);
                        break;

                    case 17: // DestroyNotify
                        goto end;

                    case 22: // ConfigureNotify
                        if (xevent.xconfigure.width != _width || xevent.xconfigure.height != _height)
                        {
                            _width = xevent.xconfigure.width;
                            _height = xevent.xconfigure.height;
                            _bufferSize = _width * _height * 4;
                            _pixelBuffer = new byte[_bufferSize];
                            XResizeWindow(_display, _window, _width, _height);
                        }
                        break;
                }
            }

            var elapsed = stopwatch.Elapsed.TotalSeconds;
            if (elapsed - lastFrameTime >= frameInterval)
            {
                lastFrameTime = elapsed;
                onFrame(elapsed);
            }
            else
            {
                Thread.Sleep(1);
            }
        }

    end:
        Cleanup();
    }

    public void Render(byte[] pixels, int width, int height)
    {
        if (_display == IntPtr.Zero || _window == IntPtr.Zero) return;

        _pixelBuffer = pixels;
        _bufferSize = pixels.Length;

        var xImage = XCreateImage(_display, IntPtr.Zero, 24, 2, 0, pixels, width * height * 4, width, height, 32, 0);
        if (xImage != IntPtr.Zero)
        {
            XPutImage(_display, _window, _gc, xImage, 0, 0, 0, 0, (uint)width, (uint)height);
            XFlush(_display);
        }
    }

    public void Close()
    {
        if (_display != IntPtr.Zero)
        {
            XDestroyWindow(_display, _window);
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    public bool PumpPendingMessage()
    {
        if (_display == IntPtr.Zero) return false;
        return XPending(_display) > 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void InitX11()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
            throw new InvalidOperationException("Cannot open X display. Set DISPLAY environment variable.");

        var screen = XDefaultScreen(_display);
        var root = XRootWindow(_display, screen);

        var visual = XDefaultVisual(_display, screen);
        var depth = (int)XDefaultDepth(_display, screen);

        var attribMask = XEventMask.ExposureMask | XEventMask.KeyPressMask |
                         XEventMask.ButtonPressMask | XEventMask.ButtonReleaseMask |
                         XEventMask.PointerMotionMask | XEventMask.StructureNotifyMask;

        _window = XCreateSimpleWindow(_display, root, 0, 0, _width, _height, 1,
            (IntPtr)(long)XBlackPixel(_display, screen), (IntPtr)(long)XWhitePixel(_display, screen));

        XStoreName(_display, _window, _title);
        XSelectInput(_display, _window, (long)attribMask);
        XMapWindow(_display, _window);

        _gc = XCreateGC(_display, _window, 0, IntPtr.Zero);

        _bufferSize = _width * _height * 4;
        _pixelBuffer = new byte[_bufferSize];
    }

    private void Cleanup()
    {
        if (_gc != IntPtr.Zero)
        {
            XFreeGC(_display, _gc);
            _gc = IntPtr.Zero;
        }
        if (_window != IntPtr.Zero)
        {
            XDestroyWindow(_display, _window);
            _window = IntPtr.Zero;
        }
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private static Key MapKey(int keySym)
    {
        return keySym switch
        {
            0xFF0D or 0xFF8D => Key.Enter,
            0xFF09 => Key.Tab,
            0xFF1B => Key.Escape,
            0xFF08 => Key.Backspace,
            0xFF50 => Key.Home,
            0xFF57 => Key.End,
            0xFF55 => Key.PageUp,
            0xFF56 => Key.PageDown,
            0xFF51 => Key.Left,
            0xFF52 => Key.Up,
            0xFF53 => Key.Right,
            0xFF54 => Key.Down,
            0xFF63 or 0xFFFF => Key.Delete,
            0xFFBE => Key.F1,
            0xFFBF => Key.F2,
            0xFFC0 => Key.F3,
            0xFFC1 => Key.F4,
            0xFFC2 => Key.F5,
            0xFFC3 => Key.F6,
            0xFFC4 => Key.F7,
            0xFFC5 => Key.F8,
            0xFFC6 => Key.F9,
            0xFFC7 => Key.F10,
            0xFFC8 => Key.F11,
            0xFFC9 => Key.F12,
            _ => (Key)keySym
        };
    }

    // X11 P/Invoke
    private const string libX11 = "libX11.so.6";

    [DllImport(libX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(libX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(libX11)]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport(libX11)]
    private static extern IntPtr XRootWindow(IntPtr display, int screenNumber);

    [DllImport(libX11)]
    private static extern IntPtr XDefaultVisual(IntPtr display, int screenNumber);

    [DllImport(libX11)]
    private static extern uint XDefaultDepth(IntPtr display, int screenNumber);

    [DllImport(libX11)]
    private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y,
        int width, int height, int borderWidth, IntPtr border, IntPtr background);

    [DllImport(libX11)]
    private static extern int XStoreName(IntPtr display, IntPtr window, string windowName);

    [DllImport(libX11)]
    private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport(libX11)]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport(libX11)]
    private static extern IntPtr XCreateGC(IntPtr display, IntPtr drawable, ulong mask, IntPtr values);

    [DllImport(libX11)]
    private static extern int XFreeGC(IntPtr display, IntPtr gc);

    [DllImport(libX11)]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport(libX11)]
    private static extern int XResizeWindow(IntPtr display, IntPtr window, int width, int height);

    [DllImport(libX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(libX11)]
    private static extern int XPending(IntPtr display);

    [DllImport(libX11)]
    private static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport(libX11)]
    private static extern int XPutImage(IntPtr display, IntPtr drawable, IntPtr gc, IntPtr image,
        int srcX, int srcY, int destX, int destY, uint width, uint height);

    [DllImport(libX11)]
    private static extern IntPtr XCreateImage(IntPtr display, IntPtr visual, uint depth, int format,
        int offset, byte[] data, int bytesPerLine, int width, int height, int bitmapPad, int bytesPerPixel);

    [DllImport(libX11)]
    private static extern int XLookupString(ref XEvent xevent, byte[] bufferReturn, int bytesBuffer,
        out int keysymReturn, out IntPtr statusReturn);

    [DllImport(libX11)]
    private static extern ulong XBlackPixel(IntPtr display, int screenNumber);

    [DllImport(libX11)]
    private static extern ulong XWhitePixel(IntPtr display, int screenNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct XEvent
    {
        public int type;
        public XClientMessageEvent xclient;
        public XKeyEvent xkey;
        public XButtonEvent xbutton;
        public XMotionEvent xmotion;
        public XExposeEvent xexpose;
        public XConfigureEvent xconfigure;
        public XDestroyWindowEvent xdestroywindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    { public int type; ulong serial; int send_event; IntPtr display; IntPtr window; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type; ulong serial; int send_event; IntPtr display; IntPtr window;
        IntPtr root, subwindow; int x, y, x_root, y_root; uint state; uint keycode;
        int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XButtonEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display; public IntPtr window;
        public IntPtr root, subwindow; public int x, y, x_root, y_root; public uint state; public uint button;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XMotionEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display; public IntPtr window;
        public IntPtr root, subwindow; public int x, y, x_root, y_root; public uint state; public int is_hint;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XExposeEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display; public IntPtr window;
        public int x, y, width, height, count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XConfigureEvent
    {
        public int type; public ulong serial; public int send_event; public IntPtr display; public IntPtr window;
        public int x, y, width, height, border_width; public IntPtr above; public int override_redirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XDestroyWindowEvent
    {
        public int type; ulong serial; int send_event; IntPtr display; IntPtr window;
    }

    [Flags]
    private enum XEventMask : long
    {
        ExposureMask = 1L << 15,
        KeyPressMask = 1L << 0,
        ButtonPressMask = 1L << 2,
        ButtonReleaseMask = 1L << 3,
        PointerMotionMask = 1L << 6,
        StructureNotifyMask = 1L << 17
    }
}
