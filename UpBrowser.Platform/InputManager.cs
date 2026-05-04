using System.Runtime.InteropServices;
using UpBrowser.Platform.Windows;

namespace UpBrowser.Platform;

public class InputManager
{
    private readonly List<KeyEvent> _keyEvents = new();
    private readonly List<MouseButtonEvent> _mouseButtonEvents = new();
    private readonly List<MouseMoveEvent> _mouseMoveEvents = new();
    private readonly List<ScrollEvent> _scrollEvents = new();

    public IReadOnlyList<KeyEvent> KeyEvents => _keyEvents;
    public IReadOnlyList<MouseButtonEvent> MouseButtonEvents => _mouseButtonEvents;
    public IReadOnlyList<MouseMoveEvent> MouseMoveEvents => _mouseMoveEvents;
    public IReadOnlyList<ScrollEvent> ScrollEvents => _scrollEvents;

    private int _lastMouseX;
    private int _lastMouseY;
    private bool _trackingMouse;

    public InputManager()
    {
    }

    public bool ProcessMessage(NativeWindow.MSG msg)
    {
        bool handled = false;

        switch (msg.message)
        {
            case NativeWindow.WM_KEYDOWN:
                var virtualKey = msg.wParam.ToInt32();
                _keyEvents.Add(new KeyEvent((Key)virtualKey, KeyState.Down));
                handled = true;
                break;

            case NativeWindow.WM_KEYUP:
                var virtualKeyUp = msg.wParam.ToInt32();
                _keyEvents.Add(new KeyEvent((Key)virtualKeyUp, KeyState.Up));
                handled = true;
                break;

            case NativeWindow.WM_CHAR:
                handled = true;
                break;

            case NativeWindow.WM_MOUSEMOVE:
                int x = (msg.lParam.ToInt32() & 0xFFFF);
                int y = ((msg.lParam.ToInt32() >> 16) & 0xFFFF);
                _mouseMoveEvents.Add(new MouseMoveEvent(x, y));
                _lastMouseX = x;
                _lastMouseY = y;
                if (!_trackingMouse)
                {
                    _trackingMouse = true;
                }
                handled = true;
                break;

            case NativeWindow.WM_LBUTTONDOWN:
                _mouseButtonEvents.Add(new MouseButtonEvent(MouseButton.Left, ButtonState.Down));
                handled = true;
                break;

            case NativeWindow.WM_LBUTTONUP:
                _mouseButtonEvents.Add(new MouseButtonEvent(MouseButton.Left, ButtonState.Up));
                handled = true;
                break;

            case NativeWindow.WM_RBUTTONDOWN:
                _mouseButtonEvents.Add(new MouseButtonEvent(MouseButton.Right, ButtonState.Down));
                handled = true;
                break;

            case NativeWindow.WM_RBUTTONUP:
                _mouseButtonEvents.Add(new MouseButtonEvent(MouseButton.Right, ButtonState.Up));
                handled = true;
                break;

            case NativeWindow.WM_MOUSEWHEEL:
                int wheelDelta = (int)(msg.wParam.ToInt64() >> 16);
                _scrollEvents.Add(new ScrollEvent(0, wheelDelta / 120.0));
                handled = true;
                break;
        }

        return handled;
    }

    public void ProcessEvents()
    {
        _keyEvents.Clear();
        _mouseButtonEvents.Clear();
        _mouseMoveEvents.Clear();
        _scrollEvents.Clear();
    }
}

public enum KeyState { Down, Up }
public enum ButtonState { Down, Up }

public enum Key
{
    Unknown = 0,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72,
    I = 73, J = 74, K = 75, L = 76, M = 77, N = 78, O = 79, P = 80,
    Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86, W = 87, X = 88,
    Y = 89, Z = 90,
    F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117,
    F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123,
    Escape = 27,
    Space = 32,
    Enter = 13,
    Tab = 9,
    Backspace = 8,
    Delete = 46,
    Left = 37, Up = 38, Right = 39, Down = 40,
}

public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public record KeyEvent(Key Key, KeyState State);
public record MouseButtonEvent(MouseButton Button, ButtonState State);
public record MouseMoveEvent(double X, double Y);
public record ScrollEvent(double OffsetX, double OffsetY);