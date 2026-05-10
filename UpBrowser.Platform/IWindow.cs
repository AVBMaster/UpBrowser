using UpBrowser.Platform;

namespace UpBrowser.Platform;

public interface IWindow : IDisposable
{
    int Width { get; }
    int Height { get; }

    Action<char>? OnChar { get; set; }
    Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    Action<Key>? OnKeyDown { get; set; }
    Action<float, float>? OnMouseMove { get; set; }
    Action<float, float, bool>? OnMouseClick { get; set; }
    Action<double>? OnMouseWheel { get; set; }
    Action<bool, bool>? OnScrollbarClick { get; set; }
    Action<float, float>? OnScrollbarDrag { get; set; }
    Action<float>? OnDpiChanged { get; set; }

    (int width, int height) GetClientSize();
    void Run(Action<double> onFrame);
    void Render(byte[] pixels, int width, int height);
    void Close();
}
