using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.JavaScript;
using UpBrowser.Platform;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsPanel
{
    private readonly DevToolsConsole _console;
    private readonly DevToolsElements _elements;
    private readonly DevToolsSource _source;

    private bool _visible;
    private int _activeTab;
    private readonly string[] _tabNames = { "Console", "Elements", "Source" };
    private float _tabBarHeight = 28;
    private float _panelHeight = 250;
    private float _panelTop;
    private float _panelLeft;
    private float _panelWidth;
    private float _dragStartY;
    private float _dragStartHeight;
    private bool _dragging;

    private int _thumbDragTab = -1;

    public bool Visible => _visible;
    public float PanelHeight => _panelHeight;
    public Action? OnChanged;

    public DevToolsPanel()
    {
        _console = new DevToolsConsole();
        _elements = new DevToolsElements();
        _source = new DevToolsSource();
    }

    public void Toggle()
    {
        _visible = !_visible;
        OnChanged?.Invoke();
    }
    public void Show() { _visible = true; OnChanged?.Invoke(); }
    public void Hide() { _visible = false; _dragging = false; _thumbDragTab = -1; OnChanged?.Invoke(); }

    public void SetJavaScriptEngine(JavaScriptEngine engine) { _console.SetJavaScriptEngine(engine); }
    public void SetSourceChangeHandler(Action<string> handler) { _source.OnHtmlChanged = handler; }

    public void SetDocument(Document? document, string htmlSource)
    {
        _elements.SetDocument(document);
        _source.SetHtml(htmlSource);
    }

    public bool TickCursorBlink()
    {
        bool c = _console.TickCursorBlink();
        bool s = _activeTab == 2 && _source.TickCursorBlink();
        return c || s;
    }

    public bool HandleWheel(double delta, float mouseX, float mouseY)
    {
        if (!_visible) return false;
        if (mouseX < _panelLeft || mouseX > _panelLeft + _panelWidth || mouseY < _panelTop || mouseY > _panelTop + _panelHeight)
            return false;
        return _activeTab switch
        {
            0 => _console.HandleWheel(delta),
            1 => _elements.HandleWheel(delta),
            2 => _source.HandleWheel(delta),
            _ => false
        };
    }

    public bool HandleClick(float x, float y)
    {
        if (!_visible) return false;

        if (x < _panelLeft || x > _panelLeft + _panelWidth || y < _panelTop || y > _panelTop + _panelHeight)
            return false;

        if (CloseButtonRect.Contains(x, y))
        {
            Hide();
            return true;
        }

        for (int i = 0; i < TabRects.Count; i++)
        {
            if (TabRects[i].Contains(x, y))
            {
                _activeTab = i;
                OnChanged?.Invoke();
                return true;
            }
        }

        float contentTop = _panelTop + _tabBarHeight;

        bool thumbHit = false;
        switch (_activeTab)
        {
            case 0: thumbHit = _console.HandleThumbDragStart(y); break;
            case 1: thumbHit = _elements.HandleThumbDragStart(y); break;
            case 2: thumbHit = _source.HandleThumbDragStart(y); break;
        }
        if (thumbHit) { _thumbDragTab = _activeTab; return true; }

        switch (_activeTab)
        {
            case 0:
                _console.HandleClick(x, y);
                OnChanged?.Invoke();
                break;
            case 2:
                _source.HandleClick(x, y);
                OnChanged?.Invoke();
                break;
        }

        return true;
    }

    public bool HandleDragStart(float x, float y)
    {
        if (!_visible) return false;
        float handleTop = _panelTop - 5;
        if (y >= handleTop && y <= handleTop + 10)
        {
            _dragging = true;
            _dragStartY = y;
            _dragStartHeight = _panelHeight;
            return true;
        }
        return false;
    }

    public bool HandleDragMove(float x, float y)
    {
        if (_dragging)
        {
            float delta = y - _dragStartY;
            _panelHeight = Math.Max(80, Math.Min(800, _dragStartHeight - delta));
            OnChanged?.Invoke();
            return true;
        }
        if (_thumbDragTab >= 0)
        {
            bool moved = false;
            switch (_thumbDragTab)
            {
                case 0: moved = _console.HandleThumbDrag(y); break;
                case 1: moved = _elements.HandleThumbDrag(y); break;
                case 2: moved = _source.HandleThumbDrag(y); break;
            }
            if (moved) OnChanged?.Invoke();
            return moved;
        }
        return false;
    }

    public void HandleDragEnd()
    {
        _dragging = false;
        _thumbDragTab = -1;
        _console.HandleThumbDragEnd();
        _elements.HandleThumbDragEnd();
        _source.HandleThumbDragEnd();
    }

    public bool HandleKeyPress(char keyChar, Key key)
    {
        if (!_visible) return false;

        if (_activeTab == 2)
        {
            if (_source.HandleKeyPress(keyChar, key))
            {
                OnChanged?.Invoke();
                return true;
            }
            return false;
        }

        if (_activeTab == 0)
            return _console.HandleKeyPress(keyChar, key);

        return false;
    }

    public bool HandleImeChar(char c)
    {
        if (!_visible) return false;
        switch (_activeTab)
        {
            case 0:
                _console.HandleImeChar(c);
                OnChanged?.Invoke();
                return true;
            case 2:
                _source.HandleImeChar(c);
                OnChanged?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public string GetActiveTabSelectedText()
    {
        return _activeTab switch
        {
            0 => _console.GetSelectedText(),
            2 => _source.GetSelectedText(),
            _ => ""
        };
    }

    public bool IsInputField(float x, float y)
    {
        if (!_visible) return false;
        if (_activeTab == 0)
        {
            float inputY = _console.InputFieldY;
            return y >= inputY && y <= inputY + 24;
        }
        return _activeTab == 2;
    }

    public SKPoint? GetCaretScreenPosition(float windowWidth, float windowHeight)
    {
        if (!_visible) return null;

        if (_activeTab == 2 && _source.IsEditing)
        {
            float py = _panelTop + _tabBarHeight;
            float sourceLineY = (_source.EditLine * 18) - _source.ScrollOffset + py;
            return new SKPoint(50 + _source.EditCol * 8, sourceLineY);
        }
        return null;
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentTop)
    {
        if (!_visible) return;

        _panelLeft = 0;
        _panelWidth = windowWidth;
        _panelTop = windowHeight - _panelHeight;

        using var bg = new SKPaint { Color = SKColor.Parse("#1E1E1E"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(_panelLeft, _panelTop - 1, _panelWidth, _panelHeight + 1, bg);

        using var div = new SKPaint { Color = SKColor.Parse("#3C3C3C"), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawLine(_panelLeft, _panelTop, _panelLeft + _panelWidth, _panelTop, div);

        using var dragPaint = new SKPaint { Color = SKColor.Parse("#252526"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(_panelLeft, _panelTop - 5, _panelWidth, 10, dragPaint);

        using var dragLine = new SKPaint { Color = SKColor.Parse("#555555"), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        float cx1 = _panelWidth / 2 - 20;
        float cy1 = _panelTop - 1;
        canvas.DrawLine(cx1, cy1, cx1 + 40, cy1, dragLine);

        float tabBarTop = _panelTop;
        float tabX = 10;
        using var tabFont = FontHelper.CreateMonoPaint(12);
        using var tabSkFont = FontHelper.CreateMonoFont(12);
        using var tabActive = new SKPaint { Color = SKColor.Parse("#569CD6"), Style = SKPaintStyle.Fill, StrokeWidth = 2 };

        TabRects.Clear();
        for (int i = 0; i < _tabNames.Length; i++)
        {
            float tw = tabSkFont.MeasureText(_tabNames[i]) + 24;
            var tr = new SKRect(tabX, tabBarTop, tabX + tw, tabBarTop + _tabBarHeight);
            TabRects.Add(tr);

            float tx = tabX + 12;
            float ty = tabBarTop + _tabBarHeight * 0.7f;

            if (i == _activeTab)
            {
                using var tabBg = new SKPaint { Color = SKColor.Parse("#2D2D2D"), Style = SKPaintStyle.Fill };
                canvas.DrawRect(tr.Left, tr.Top, tr.Width, tr.Height, tabBg);
                canvas.DrawLine(tr.Left, tr.Bottom - 1, tr.Right, tr.Bottom - 1, tabActive);
                tabFont.Color = SKColors.White;
            }
            else tabFont.Color = SKColor.Parse("#808080");

            canvas.DrawText(_tabNames[i], tx, ty, SKTextAlign.Left, tabSkFont, tabFont);
            tabX += tw;
        }

        float cx_close = _panelWidth - 30;
        float cy_close = tabBarTop + 4;
        CloseButtonRect = new SKRect(cx_close, cy_close, cx_close + 20, cy_close + 20);
        using var cp = new SKPaint { Color = SKColor.Parse("#808080"), IsAntialias = true };
        using var cpFont = FontHelper.CreateFont(14);
        canvas.DrawText("✕", cx_close + 3, cy_close + 15, SKTextAlign.Left, cpFont, cp);

        float cl = 0, ct = tabBarTop + _tabBarHeight, cw = _panelWidth, ch = _panelHeight - _tabBarHeight;
        switch (_activeTab)
        {
            case 0: _console.Render(canvas, cl, ct, cw, ch); break;
            case 1: _elements.Render(canvas, cl, ct, cw, ch); break;
            case 2: _source.Render(canvas, cl, ct, cw, ch); break;
        }
    }

    public List<SKRect> TabRects { get; } = new();
    public SKRect CloseButtonRect { get; private set; }
}
