using UpBrowser.Core;
using UpBrowser.Platform;

namespace UpBrowser.Platform;

public interface IWindow : IDisposable
{
    int Width { get; }
    int Height { get; }

    Action<char>? OnChar { get; set; }
    Action<char>? OnImeChar { get; set; }
    Func<char, Key, bool>? OnKeyDownWithChar { get; set; }
    Action<Key>? OnKeyDown { get; set; }
    Action<float, float>? OnMouseMove { get; set; }
    Action<float, float, bool>? OnMouseClick { get; set; }
    /// <summary>
    /// 滚动事件参数: deltaX (水平), deltaY (垂直)
    /// </summary>
    Action<double, double>? OnMouseWheel { get; set; }
    Action<float>? OnDpiChanged { get; set; }
    Action? OnSetFocus { get; set; }
    Action? OnKillFocus { get; set; }

    IImeHandler? ImeHandler { get; }

    void SetImeTarget(IImeSupport? target);
    void UpdateImeCompositionWindow();

    (int width, int height) GetClientSize();
    void Run(Action<double> onFrame);
    void Render(byte[] pixels, int width, int height);
    void Close();
    bool PumpPendingMessage();
}