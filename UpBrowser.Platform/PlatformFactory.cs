using System.Runtime.InteropServices;

namespace UpBrowser.Platform;

public static class PlatformFactory
{
    public static IWindow CreateWindow(int width, int height, string title)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Windows.WindowsWindow(width, height, title);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new Linux.LinuxWindow(width, height, title);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new Mac.MacWindow(width, height, title);
        }

        throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    public static Windows.WindowsWindow? CreateWindowsWindow(int width, int height, string title)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Windows.WindowsWindow(width, height, title);
        }
        return null;
    }

    public static float GetDpiScale()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsDpiScale();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacDpiScale();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxDpiScale();
        }

        return 1.0f;
    }

    private static float GetWindowsDpiScale()
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            SetProcessDPIAware();
        }

        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, hdc);
            return dpiX / 96.0f;
        }
        return 1.0f;
    }

    private static float GetMacDpiScale()
    {
        try
        {
            var nsScreenClass = objc_getClass("NSScreen");
            var mainScreen = objc_msgSend_ptr(nsScreenClass, sel_registerName("mainScreen"));
            if (mainScreen != IntPtr.Zero)
            {
                var backingScaleFactor = objc_msgSend_float(mainScreen, sel_registerName("backingScaleFactor"));
                return backingScaleFactor;
            }
        }
        catch { }
        return 2.0f;
    }

    private static float GetLinuxDpiScale()
    {
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display != IntPtr.Zero)
            {
                int screen = XDefaultScreen(display);
                int widthMm = XDisplayWidthMM(display, screen);
                int widthPx = XDisplayWidth(display, screen);

                XCloseDisplay(display);

                if (widthMm > 0)
                {
                    float dpi = widthPx * 25.4f / widthMm;
                    return dpi / 96.0f;
                }
            }
        }
        catch { }
        return 1.0f;
    }

    // Windows P/Invoke
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern int SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    // macOS P/Invoke
    private const string libObjC = "/usr/lib/libobjc.dylib";

    [DllImport(libObjC)]
    private static extern IntPtr objc_getClass(string className);

    [DllImport(libObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(libObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector);

    [DllImport(libObjC, EntryPoint = "objc_msgSend")]
    private static extern float objc_msgSend_float(IntPtr receiver, IntPtr selector);

    // Linux X11 P/Invoke
    private const string libX11 = "libX11.so.6";

    [DllImport(libX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(libX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(libX11)]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport(libX11)]
    private static extern int XDisplayWidthMM(IntPtr display, int screen);

    [DllImport(libX11)]
    private static extern int XDisplayWidth(IntPtr display, int screen);
}
