namespace UpBrowser.Platform.Linux;

public class LinuxWindow : IWindow
{
    private bool _disposed;
    private int _width;
    private int _height;

    public Action<char>? OnChar { get; set; }
    public Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    public Action<Key>? OnKeyDown { get; set; }
    public Action<float, float>? OnMouseMove { get; set; }
    public Action<float, float, bool>? OnMouseClick { get; set; }
    public Action<double>? OnMouseWheel { get; set; }
    public Action<bool, bool>? OnScrollbarClick { get; set; }
    public Action<float, float>? OnScrollbarDrag { get; set; }
    public Action<float>? OnDpiChanged { get; set; }

    public int Width => _width;
    public int Height => _height;

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

    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
