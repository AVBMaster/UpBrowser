using UpBrowser.Platform;
using UpBrowser.Rendering;

namespace UpBrowser;

public class InputHandler
{
    private readonly ChromeRenderer _chrome;
    private readonly ScrollManager _scroll;
    private readonly IWindow _window;
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private float _mouseX, _mouseY;
    private bool _mouseDown;

    private bool _pageThumbDragging;
    private float _pageThumbDragStartY;
    private float _pageThumbDragStartScroll;

    public bool NeedsRedraw { get; set; } = true;

    public Action<float, float>? OnDomClick { get; set; }
    public Action? OnDevToolsKey { get; set; }
    public Func<char, Key, bool>? OnDevToolsInput { get; set; }
    public Func<float, float, bool, bool>? OnDevToolsClick { get; set; }
    public Func<double, float, float, bool>? OnDevToolsWheel { get; set; }
    public Func<float, float, bool>? OnDialogClick { get; set; }
    public Action<char>? OnImeChar { get; set; }
    public Action? OnCopy { get; set; }
    public Action? OnPaste { get; set; }
    public Action? OnCut { get; set; }
    public Action? OnSelectAll { get; set; }

    public InputHandler(ChromeRenderer chrome, ScrollManager scroll, IWindow window, float dpiScale)
    {
        _chrome = chrome;
        _scroll = scroll;
        _window = window;
        _dpiScale = dpiScale;
        _contentOffset = chrome.GetContentOffset();
    }

    public void WireEvents()
    {
        _window.OnMouseMove = OnMouseMove;
        _window.OnMouseClick = OnMouseClick;
        _window.OnKeyDownWithChar = OnKeyDownWithChar;
        _window.OnKeyDown = OnKeyDown;
        _window.OnMouseWheel = OnMouseWheel;
        _window.OnImeChar = OnImeChar;

        _chrome.OnUrlBarFocus = () => _window.SetImeTarget(_chrome);
        _chrome.OnUrlBarBlur = () => _window.SetImeTarget(null);
    }

    private void OnMouseMove(float x, float y)
    {
        float logicalX = x / _dpiScale;
        float logicalY = y / _dpiScale;
        _mouseX = logicalX;
        _mouseY = logicalY;
        _chrome.HandleMouseMove(logicalX, logicalY);
    }

    private void OnMouseClick(float x, float y, bool isDown)
    {
        float logicalX = x / _dpiScale;
        float logicalY = y / _dpiScale;
        _mouseX = logicalX;
        _mouseY = logicalY;

        if (isDown)
        {
            _mouseDown = true;

            if (OnDialogClick?.Invoke(logicalX, logicalY) == true)
            {
                NeedsRedraw = true;
                return;
            }

            bool handled = _chrome.HandleMouseClick(logicalX, logicalY);

            if (!handled)
                handled = OnDevToolsClick?.Invoke(logicalX, logicalY, false) ?? false;

            NeedsRedraw = true;

            if (!handled)
                {
                    if (!HandleScrollbarClick(logicalX, logicalY))
                    {
                        OnDomClick?.Invoke(logicalX, logicalY);
                    }
                }
        }
        else
        {
            _mouseDown = false;
            _pageThumbDragging = false;
        }
    }

    // Handles scrollbar clicks: thumb drag or track click (page up/down)
    private bool HandleScrollbarClick(float logicalX, float logicalY)
    {
        float statusBarHeight = _chrome.GetStatusBarHeight();
        float viewportHeight = _window.Height / _dpiScale - _contentOffset - statusBarHeight;

        if (!_scroll.CanScrollY) return false;

        float scrollbarLeft = _window.Width / _dpiScale - ScrollManager.ScrollbarWidth;
        if (logicalX < scrollbarLeft ||
            logicalY < _contentOffset ||
            logicalY > _contentOffset + viewportHeight)
            return false;

        float trackHeight = viewportHeight;
        float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
            trackHeight * viewportHeight / _scroll.ContentHeight);
        float maxScrollY = _scroll.ContentHeight - viewportHeight;
        float thumbTop = maxScrollY > 0
            ? (_scroll.ScrollY / maxScrollY) * (trackHeight - thumbHeight)
            : 0;
        float thumbEnd = thumbTop + thumbHeight;

        // Check if clicking ON the thumb to start dragging
        if (logicalY >= _contentOffset + thumbTop && logicalY <= _contentOffset + thumbEnd)
        {
            _pageThumbDragging = true;
            _pageThumbDragStartY = logicalY;
            _pageThumbDragStartScroll = _scroll.ScrollY;
            return true;
        }

        // Otherwise page up/down on track click
        if (logicalY < _contentOffset + thumbTop)
            _scroll.PageUp();
        else
            _scroll.PageDown();
        return true;
    }

    public void UpdatePageThumbDrag()
    {
        if (!_pageThumbDragging || !_mouseDown) return;

        float statusBarHeight = _chrome.GetStatusBarHeight();
        float viewportHeight = _window.Height / _dpiScale - _contentOffset - statusBarHeight;
        if (!_scroll.CanScrollY || viewportHeight <= 0) return;

        float trackHeight = viewportHeight;
        float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
            trackHeight * viewportHeight / _scroll.ContentHeight);
        float maxScrollY = _scroll.ContentHeight - viewportHeight;
        if (maxScrollY <= 0) return;

        float delta = (_mouseY - _pageThumbDragStartY) / (trackHeight - thumbHeight) * maxScrollY;
        _scroll.ScrollTo(_pageThumbDragStartScroll + delta);
    }

    private bool OnKeyDownWithChar(char charCode, Key key)
    {
        if (OnDevToolsInput != null && !_chrome.IsUrlBarFocused())
        {
            if (OnDevToolsInput(charCode, key))
            {
                NeedsRedraw = true;
                return true;
            }
        }

        if (key == Key.Unknown && charCode != '\0')
        {
            if (charCode == 1) { OnSelectAll?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 3) { OnCopy?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 22) { OnPaste?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 24) { OnCut?.Invoke(); NeedsRedraw = true; return true; }
            if (charCode == 26) { return true; }

            _chrome.HandleKeyPress(charCode, SKKey.None);
            NeedsRedraw = true;
            return true;
        }

        SKKey chromeKey = key switch
        {
            Key.Enter => SKKey.Enter,
            Key.Escape => SKKey.Escape,
            Key.Left => SKKey.Left,
            Key.Up => SKKey.Up,
            Key.Right => SKKey.Right,
            Key.Down => SKKey.Down,
            Key.Home => SKKey.Home,
            Key.End => SKKey.End,
            Key.Backspace => SKKey.Backspace,
            Key.Delete => SKKey.Delete,
            Key.Tab => SKKey.Tab,
            Key.Space => SKKey.Space,
            _ => SKKey.None
        };

        bool handledByChrome = _chrome.HandleKeyPress(charCode, chromeKey);
        NeedsRedraw = true;

        if (handledByChrome)
            return true;

        if (_chrome.IsUrlBarFocused())
            return false;

        switch (key)
        {
            case Key.Tab:
                _chrome.NextTab();
                return true;
            case Key.F5:
                _chrome.OnRefresh?.Invoke();
                return true;
            case Key.PageUp:
                _scroll.PageUp();
                return true;
            case Key.PageDown:
                _scroll.PageDown();
                return true;
            case Key.Home:
                _scroll.ScrollHome();
                return true;
            case Key.End:
                _scroll.ScrollEnd();
                return true;
            case Key.Up:
                _scroll.ScrollBy(0, -40);
                return true;
            case Key.Down:
                _scroll.ScrollBy(0, 40);
                return true;
            case Key.Left:
                _scroll.ScrollBy(-40, 0);
                return true;
            case Key.Right:
                _scroll.ScrollBy(40, 0);
                return true;
            default:
                return false;
        }
    }

    private void OnKeyDown(Key key)
    {
        if (key == Key.F12)
        {
            OnDevToolsKey?.Invoke();
            NeedsRedraw = true;
            return;
        }

        if (!_chrome.IsUrlBarFocused())
        {
            if (key == Key.F5)
            {
                _chrome.OnRefresh?.Invoke();
            }
        }
        else if (key == Key.Tab)
        {
            _chrome.NextTab();
        }
    }

    private void OnMouseWheel(double delta)
    {
        if (OnDevToolsWheel != null && OnDevToolsWheel(delta, _mouseX, _mouseY))
        {
            NeedsRedraw = true;
            return;
        }

        if (!_chrome.IsUrlBarFocused())
        {
            _scroll.ScrollBy((float)delta);
            NeedsRedraw = true;
        }
    }

    public (float x, float y) GetMousePosition() => (_mouseX, _mouseY);
    public bool IsMouseDown() => _mouseDown;

    public void HandleChar(char charCode)
    {
        if (charCode == '\0') return;

        if (OnDevToolsInput != null && !_chrome.IsUrlBarFocused())
        {
            if (OnDevToolsInput(charCode, Key.Unknown))
            {
                NeedsRedraw = true;
                return;
            }
        }

        _chrome.HandleKeyPress(charCode, SKKey.None);
        NeedsRedraw = true;
    }

    private void HandleImeChar(char charCode)
    {
        OnImeChar?.Invoke(charCode);
    }
}
