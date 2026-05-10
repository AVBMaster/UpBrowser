namespace UpBrowser.Platform.Linux;

public class LinuxWindow : IWindow
{
    private bool _disposed;
    private int _width;
    private int _height;

    public Action<char>? OnChar { get; set; }
    public Action<char>? OnImeChar { get; set; }
    public Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    public Action<Key>? OnKeyDown { get; set; }
    public Action<float, float>? OnMouseMove { get; set; }
    public Action<float, float, bool>? OnMouseClick { get; set; }
    public Action<double>? OnMouseWheel { get; set; }
    public Action<float>? OnDpiChanged { get; set; }

    public int Width => _width;
    public int Height => _height;

    public IImeHandler? ImeHandler => null;

    public LinuxWindow(int width, int height, string title)
    {
        _width = width;
        _height = height;
    }

    public (int width, int height) GetClientSize() => (_width, _height);

    public void Run(Action<double> onFrame)
    {
    }

    public void Render(byte[] pixels, int width, int height)
    {
    }

    public void Close()
    {
    }

    public bool PumpPendingMessage() => false;

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
