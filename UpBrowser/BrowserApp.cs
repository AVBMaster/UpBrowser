using UpBrowser.Platform;
using UpBrowser.Platform.Windows;
using UpBrowser.Rendering;
using UpBrowser.Rendering.DevTools;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Css;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.EventLoop;
using SkiaSharp;
using System.Text;

namespace UpBrowser;

internal static class ClipboardHelper
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetClipboardData(uint uFormat, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    private const uint CF_UNICODETEXT = 13;

    public static void SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            IntPtr hMem = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(text);
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
        finally { CloseClipboard(); }
    }

    public static string? GetText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = GetClipboardData(CF_UNICODETEXT);
            return hData != IntPtr.Zero
                ? System.Runtime.InteropServices.Marshal.PtrToStringUni(hData)
                : null;
        }
        finally { CloseClipboard(); }
    }
}

public class BrowserApp : IDisposable
{
    private readonly IWindow _window;
    private readonly SkiaRenderer _skiaRenderer;
    private readonly ChromeRenderer _chrome;
    private readonly ScrollManager _scroll;
    private readonly DocumentManager _docManager;
    private readonly InputHandler _input;
    private readonly LayoutEngine _layout = new();
    private readonly JavaScriptEngine _jsEngine;
    private readonly EventLoop _eventLoop;
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private DocumentManager.DocumentLoadResult? _currentLoad;
    private PaintVisitor? _cachedPaintVisitor;
    private DisplayList _displayList = new();
    private float _lastLayoutWidth;
    private string _currentHtml = "";

    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private float _lastScrollX;
    private float _lastScrollY;
    private float _lastDevToolsHeight;
    private bool _lastDevToolsVisible;
    private readonly DevToolsPanel _devTools;

    private readonly ImageCache _sharedImageCache = new();
    private readonly Dictionary<string, SKTypeface> _sharedTypefaceCache = new();
    private static string[]? _fontFamilies;
    private bool _pendingRelayout;

    private DateTime _lastInputTime = DateTime.Now;
    private const double InputCooldownMs = 80;

    private string? _dialogResult;
    private string? _dialogInput;
    private bool _dialogActive;
    private string _dialogMessage = "";
    private string _dialogType = "";
    private SKRect _dialogOkRect;
    private SKRect _dialogCancelRect;
    private SKRect _dialogInputRect;

    // IME support: track focused input element
    private UpBrowser.Core.Dom.Element? _focusedElement;
    private StringBuilder _imeCommittedText = new();
    private bool _devToolsFocused;
    private const float ScrollbarDragThreshold = 5;

    public BrowserApp(int logicalWidth, int logicalHeight)
    {
        // Cache font families once at startup (avoids O(SKFontManager) enumeration per frame)
        _fontFamilies ??= SkiaSharp.SKFontManager.Default.FontFamilies.ToArray();

        _dpiScale = PlatformFactory.GetDpiScale();
        Console.WriteLine($"DPI Scale: {_dpiScale:F2} ({_dpiScale * 100}%)");

        int physicalWidth = (int)(logicalWidth * _dpiScale);
        int physicalHeight = (int)(logicalHeight * _dpiScale);

var winWindow = PlatformFactory.CreateWindowsWindow(physicalWidth, physicalHeight, "UpBrowser");
        _window = winWindow ?? throw new InvalidOperationException("Failed to create window");
        _docManager = new DocumentManager();
        _chrome = new ChromeRenderer();
        _scroll = new ScrollManager();
        _skiaRenderer = new SkiaRenderer();
        JsEngineConfig.DefaultEngineType = JsEngineType.Jint;
        JsEngineConfig.Initialize();
        _jsEngine = new JavaScriptEngine();
        _eventLoop = new EventLoop();
        _devTools = new DevToolsPanel();
        _contentOffset = _chrome.GetContentOffset();
        _input = new InputHandler(_chrome, _scroll, _window, _dpiScale);
        _input.OnDomClick = HandleDomClick;
        _input.OnDevToolsKey = () =>
        {
            _devTools.Toggle();
            _input.NeedsRedraw = true;
        };

        _input.OnDomClick = HandleDomClick;
        _input.OnDevToolsKey = () =>
        {
            _devTools.Toggle();
            _devToolsFocused = _devTools.Visible;
            _input.NeedsRedraw = true;
        };

        _devTools.SetJavaScriptEngine(_jsEngine);
        _devTools.SetSourceChangeHandler(async (html) =>
        {
            _currentHtml = html;
            _currentLoad = await _docManager.LoadHtmlAsync(html);
            var (pw, ph) = _window.GetClientSize();
            _jsEngine.LoadDocument(_currentLoad.Document);
            _devTools.SetDocument(_currentLoad.Document, html);
            _input.NeedsRedraw = true;
        });
        _devTools.OnChanged += () =>
        {
            _input.NeedsRedraw = true;
        };
        _input.OnDevToolsInput = (c, key) => DevToolsHandleInput(c, key);
        _input.OnDevToolsClick = (x, y, isDown) => HandleDevToolsClick(x, y, isDown);
        _input.OnDevToolsWheel = (delta, mx, my) => _devTools.HandleWheel(delta, mx, my);
        _input.OnImeChar = HandleImeChar;
        _input.OnCopy = PerformCopy;
        _input.OnPaste = PerformPaste;
        _input.OnCut = PerformCut;
        _input.OnSelectAll = PerformSelectAll;

        _jsEngine.ShowDialog = ShowDialog;
        _input.OnDialogClick = (x, y) =>
        {
            if (!_dialogActive) return false;

            if (_dialogOkRect.Contains(x, y))
            {
                _dialogResult = _dialogType.StartsWith("prompt:") ? _dialogInput : _dialogType == "confirm" ? "true" : "";
                _dialogActive = false;
                return true;
            }

            if (_dialogCancelRect.Contains(x, y) && (_dialogType.StartsWith("prompt:") || _dialogType == "confirm"))
            {
                _dialogResult = _dialogType.StartsWith("prompt:") ? null : "false";
                _dialogActive = false;
                return true;
            }

            if (_dialogInputRect.Contains(x, y) && _dialogType.StartsWith("prompt:"))
                return true;

            return true;
        };

        Initialize();
    }

    private void Initialize()
    {
        _chrome.Initialize();

        _skiaRenderer.Initialize(1024, 768, enableDirtyRegions: true);
        _skiaRenderer.DpiScale = _dpiScale;

        // Attempt GPU acceleration (OpenGL via SkiaSharp GRContext)
        if (_skiaRenderer.TryEnableGpu())
        {
            Console.WriteLine("GPU acceleration enabled (OpenGL)");
            _skiaRenderer.Initialize(1024, 768, enableDirtyRegions: false);
            _skiaRenderer.DpiScale = _dpiScale;
        }
        else
        {
            Console.WriteLine("GPU acceleration unavailable, using CPU rendering");
        }

        _eventLoop.Start();

        _input.WireEvents();
    }

    private string? ShowDialog(string message, string? type)
    {
        _dialogMessage = message;
        _dialogType = type ?? "";
        _dialogInput = (type ?? "").StartsWith("prompt:") ? (type ?? "")[7..] : "";
        _dialogResult = null;
        _dialogActive = true;

        var (pw, ph) = _window.GetClientSize();
        int ww = (int)(pw / _dpiScale);
        int wh = (int)(ph / _dpiScale);

        while (_dialogActive)
        {
            if (!_window.PumpPendingMessage())
                break;

            RenderDialogFrame(ww, wh);

            Thread.Sleep(10);
        }

        _dialogActive = false;
        return _dialogResult;
    }

    private void RenderDialogFrame(int windowWidth, int windowHeight)
    {
        _skiaRenderer.Canvas.Clear(SKColors.White);

        var title = _currentLoad?.AngleSharpDoc.Title ?? "UpBrowser";
        var currentUrl = _chrome.GetCurrentUrl();
        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl ?? "upbrowser://local", title);

        float devToolsHeight = _devTools.Visible ? _devTools.PanelHeight : 0;
        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight() - devToolsHeight;
        _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
            _scroll.ScrollX, _scroll.ScrollY,
            windowWidth, contentViewportHeight);

        RenderDialogOverlay(_skiaRenderer.Canvas, windowWidth, windowHeight);

        var pixels = _skiaRenderer.GetPixelData();
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);
    }

    private void RenderDialogOverlay(SKCanvas canvas, float windowWidth, float windowHeight)
    {
        using var overlay = new SKPaint { Color = new SKColor(0, 0, 0, 128), Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, windowWidth, windowHeight, overlay);

        float dlgW = 360, dlgH = 160;
        float dlgX = (windowWidth - dlgW) / 2;
        float dlgY = (windowHeight - dlgH) / 2;

        using var bg = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(dlgX, dlgY, dlgW, dlgH, 8, 8, bg);

        using var border = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(dlgX, dlgY, dlgW, dlgH, 8, 8, border);

        using var titlePaint = FontHelper.CreatePaint(14);
        using var titleFont = FontHelper.CreateFont(14);
        titlePaint.Color = SKColor.Parse("#333333");
        string titleText = _dialogType == "alert" ? "Alert" : _dialogType == "confirm" ? "Confirm" : "Prompt";
        canvas.DrawText(titleText, dlgX + 16, dlgY + 24, SKTextAlign.Left, titleFont, titlePaint);

        using var msgPaint = FontHelper.CreatePaint(13);
        using var msgFont = FontHelper.CreateFont(13);
        msgPaint.Color = SKColor.Parse("#666666");
        float msgY = dlgY + 55;
        float maxMsgW = dlgW - 32;
        string msg = _dialogMessage;
        if (msgFont.MeasureText(msg) > maxMsgW)
        {
            while (msgFont.MeasureText(msg + "…") > maxMsgW && msg.Length > 0)
                msg = msg[..^1];
            msg += "…";
        }
        canvas.DrawText(msg, dlgX + 16, msgY, SKTextAlign.Left, msgFont, msgPaint);

        bool isPrompt = _dialogType.StartsWith("prompt:");
        float btnY = dlgY + dlgH - 40;

        if (isPrompt)
        {
            using var inputPaint = FontHelper.CreatePaint(13);
            using var inputFont = FontHelper.CreateFont(13);
            inputPaint.Color = SKColor.Parse("#333333");
            using var inputBg = new SKPaint { Color = SKColor.Parse("#F5F5F5"), Style = SKPaintStyle.Fill };
            float inpX = dlgX + 16, inpY = dlgY + 80, inpW = dlgW - 32, inpH = 28;
            canvas.DrawRoundRect(inpX, inpY, inpW, inpH, 4, 4, inputBg);
            canvas.DrawText(_dialogInput ?? "", inpX + 8, inpY + inpH * 0.7f, SKTextAlign.Left, inputFont, inputPaint);
            _dialogInputRect = new SKRect(inpX, inpY, inpX + inpW, inpY + inpH);
        }

        float btnW = 70, btnH = 28;
        float btnSpacing = 10;
        float totalBtnW = (isPrompt || _dialogType == "confirm") ? btnW * 2 + btnSpacing : btnW;
        float btnStartX = dlgX + (dlgW - totalBtnW) / 2;

        if (isPrompt || _dialogType == "confirm")
        {
            _dialogCancelRect = new SKRect(btnStartX, btnY, btnStartX + btnW, btnY + btnH);
            using var cancelPaint = new SKPaint { Color = SKColor.Parse("#E0E0E0"), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(_dialogCancelRect, 4, 4, cancelPaint);
            using var cancelFontPaint = FontHelper.CreatePaint(12);
            using var cancelFont = FontHelper.CreateFont(12);
            cancelFontPaint.Color = SKColor.Parse("#333333");
            string cancelLabel = isPrompt ? "Cancel" : "Cancel";
            float cw = cancelFont.MeasureText(cancelLabel);
            canvas.DrawText(cancelLabel, _dialogCancelRect.Left + (btnW - cw) / 2, _dialogCancelRect.Top + btnH * 0.7f, SKTextAlign.Left, cancelFont, cancelFontPaint);
            btnStartX += btnW + btnSpacing;
        }

        _dialogOkRect = new SKRect(btnStartX, btnY, btnStartX + btnW, btnY + btnH);
        using var okPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(_dialogOkRect, 4, 4, okPaint);
        using var okFontPaint = FontHelper.CreatePaint(12);
        using var okFont = FontHelper.CreateFont(12);
        okFontPaint.Color = SKColors.White;
        string okLabel = isPrompt ? "OK" : "OK";
        float ow = okFont.MeasureText(okLabel);
        canvas.DrawText(okLabel, _dialogOkRect.Left + (btnW - ow) / 2, _dialogOkRect.Top + btnH * 0.7f, SKTextAlign.Left, okFont, okFontPaint);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("UpBrowser - Starting...");

        _currentHtml = DocumentManager.DefaultHtml;
        await NavigateToHtml(_currentHtml);

        var devTool = new LayoutDevTool();
        var debugReport = devTool.GenerateReport(_currentLoad!.Document, 1024, 768);
        File.WriteAllText("layout_debug.txt", debugReport);
        Console.WriteLine("Debug report saved to layout_debug.txt");
        Console.WriteLine(devTool.GenerateQuickReport(_currentLoad.Document));

        BuildDisplayList(1024, 768);
        _lastLayoutWidth = 1024;
        var bodyBox = _currentLoad.Document.Body?.LayoutBox;
        var lastContentHeight = bodyBox?.BorderBox.Height ?? 0;

        WireNavigation();

        _window.Run(RenderFrame);

        Console.WriteLine("UpBrowser closed.");
    }

    private void WireNavigation()
    {
        _chrome.OnNavigate = async (url) =>
        {
            Console.WriteLine($"Navigating to: {url}");
            _input.NeedsRedraw = true;

            if (url.StartsWith("upbrowser://"))
            {
                if (url == "upbrowser://newtab" || url == "upbrowser://local")
                {
                    _currentHtml = DocumentManager.DefaultHtml;
                    await NavigateToHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
            }
            else if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                await NavigateToHttp(url);
            }
            else
            {
                await NavigateToSearch(url);
            }
        };

        _chrome.OnRefresh = () =>
        {
            Console.WriteLine("Refreshing page...");
            _input.NeedsRedraw = true;
            if (!string.IsNullOrEmpty(_currentHtml))
            {
                Task.Run(async () =>
                {
                    await NavigateToHtml(_currentHtml);
                    _input.NeedsRedraw = true;
                });
            }
        };

        _chrome.OnHome = () =>
        {
            Console.WriteLine("Going home...");
            _chrome.NavigateToUrl("upbrowser://local");
        };
    }

    private async Task NavigateToHtml(string html)
    {
        _currentHtml = html;
        _currentLoad = await _docManager.LoadHtmlAsync(html);

        var (pw, ph) = _window.GetClientSize();
        int ww = (int)(pw / _dpiScale);
        int wh = (int)(ph / _dpiScale);

        _jsEngine.LoadDocument(_currentLoad.Document);

        _devTools.SetDocument(_currentLoad.Document, html);

        RunPageScripts();

        BuildDisplayList(ww, wh);
        _scroll.ScrollTo(0, 0);
    }

    private void RunPageScripts()
    {
        if (_currentLoad == null) return;

        var angleDoc = _currentLoad.AngleSharpDoc;
        var scriptElements = angleDoc.All.Where(e =>
            e.LocalName?.ToLowerInvariant() == "script");

        foreach (var scriptEl in scriptElements)
        {
            var src = scriptEl.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        using var client = new HttpClient();
                        var code = await client.GetStringAsync(src);
                        _jsEngine.Execute(code);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[JS] Failed to load script '{src}': {ex.Message}");
                    }
                });
                continue;
            }

            var code = scriptEl.TextContent;
            if (!string.IsNullOrWhiteSpace(code))
            {
                _jsEngine.Execute(code);
            }
        }
    }

    private async Task NavigateToHttp(string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var webHtml = await httpClient.GetStringAsync(url);
            await NavigateToHtml(webHtml);

            Console.WriteLine($"Loaded: {_currentLoad?.AngleSharpDoc.Title}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load URL: {ex.Message}");
            var errorHtml = $@"<!DOCTYPE html>
<html><head><title>Error</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #d32f2f;'>Navigation Error</h1>
    <p>Failed to load: {url}</p>
    <p style='color: #666;'>Error: {ex.Message}</p>
</body></html>";
            await NavigateToHtml(errorHtml);
        }
    }

    private async Task NavigateToSearch(string query)
    {
        var searchHtml = $@"<!DOCTYPE html>
<html><head><title>Search: {query}</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #1a73e8;'>Search</h1>
    <p>Searching for: <strong>{query}</strong></p>
    <p style='color: #666;'>Search functionality requires network access.</p>
</body></html>";
        await NavigateToHtml(searchHtml);
    }

    private void BuildDisplayList(float windowWidth, float windowHeight)
    {
        if (_currentLoad == null) return;

        _layout.Layout(_currentLoad.Document, windowWidth, windowHeight);

        // Return old display list ops to pool before creating new one (fixes memory leak)
        _displayList.Clear();

        _cachedPaintVisitor = new PaintVisitor(_contentOffset, _sharedTypefaceCache, _sharedImageCache, _fontFamilies);
        _cachedPaintVisitor.VisitDocument(_currentLoad.Document);
        _displayList = _cachedPaintVisitor.GetDisplayList();
        _displayList.SortByZIndex();
        _displayList.BuildSpatialGrid();

        // Invalidate the cached SKPicture so SkiaRenderer re-records on next frame
        _skiaRenderer.InvalidatePageCache();
    }

    private void RenderFrame(double dt)
    {
        _eventLoop.ProcessTasks();
        if (_jsEngine.HasTimers)
            _jsEngine.TickTimers();

        if (_input.IsMouseDown())
        {
            _input.UpdatePageThumbDrag();
        }

        float currentDevToolsHeight = _devTools.Visible ? _devTools.PanelHeight : 0;
        bool devToolsChanged = _devTools.Visible != _lastDevToolsVisible ||
                               Math.Abs(currentDevToolsHeight - _lastDevToolsHeight) > 0.5f;

        if (_devTools.Visible)
        {
            var (mx, my) = _input.GetMousePosition();
            if (_input.IsMouseDown())
            {
                bool dragMoved = _devTools.HandleDragMove(mx, my);
                if (dragMoved) _input.NeedsRedraw = true;
            }
            else
            {
                _devTools.HandleDragEnd();
            }
        }

        bool cursorChanged = _chrome.UpdateCursorBlink();
        bool devToolsCursorChanged = _devTools.Visible && _devTools.TickCursorBlink();
        bool cursorNeedsRedraw = (cursorChanged && _chrome.IsUrlBarFocused()) || devToolsCursorChanged;

        var (pw, ph) = _window.GetClientSize();
        int windowWidth = (int)(pw / _dpiScale);
        int windowHeight = (int)(ph / _dpiScale);

        bool sizeChanged = windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight;
        bool scrollChanged = Math.Abs(_scroll.ScrollX - _lastScrollX) > 0.5f ||
                             Math.Abs(_scroll.ScrollY - _lastScrollY) > 0.5f;

        bool inputRecently = (DateTime.Now - _lastInputTime).TotalMilliseconds < InputCooldownMs;

        // Accumulate pending relayout flag
        if (_jsEngine.NeedsReLayout)
            _pendingRelayout = true;

        bool needsRedraw = _input.NeedsRedraw || _pendingRelayout || devToolsChanged ||
                           (cursorNeedsRedraw && !inputRecently) || scrollChanged;

        if (!sizeChanged && !needsRedraw)
            return;

        if (windowWidth <= 0 || windowHeight <= 0 || _currentLoad == null)
            return;

        if (sizeChanged)
        {
            _skiaRenderer.Resize(windowWidth, windowHeight);
            _lastWindowWidth = windowWidth;
            _lastWindowHeight = windowHeight;
        }

        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight() - currentDevToolsHeight;

        if (sizeChanged || windowWidth != _lastLayoutWidth || _pendingRelayout || devToolsChanged)
        {
            _lastLayoutWidth = windowWidth;

            if (_pendingRelayout)
            {
                var styleComputer = new StyleComputer();
                styleComputer.ComputeStyles(_currentLoad.Document);
                _jsEngine.ClearDirty();
                _pendingRelayout = false;
            }

            BuildDisplayList(windowWidth, Math.Max(100, (int)contentViewportHeight));

            var bodyBox = _currentLoad.Document.Body?.LayoutBox;
            float contentWidth = bodyBox?.BorderBox.Width ?? windowWidth;
            float contentHeight = bodyBox?.BorderBox.Height ?? 0;

            _scroll.UpdateScroll(contentWidth, contentHeight, windowWidth, contentViewportHeight);

            PositionImeCaret();
        }

        if (_focusedElement != null && _focusedElement.IsFormElement)
        {
            PositionImeCaret();
        }

        _lastScrollX = _scroll.ScrollX;
        _lastScrollY = _scroll.ScrollY;
        _lastDevToolsHeight = currentDevToolsHeight;
        _lastDevToolsVisible = _devTools.Visible;

        _skiaRenderer.Canvas.Clear(SKColors.White);

        var title = _currentLoad.AngleSharpDoc.Title ?? "UpBrowser";
        var currentUrl = _chrome.GetCurrentUrl();
        if (string.IsNullOrEmpty(currentUrl))
            currentUrl = "upbrowser://local";

        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl, title);

        _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
            _scroll.ScrollX, _scroll.ScrollY,
            windowWidth, contentViewportHeight);

        _chrome.RenderScrollbars(_skiaRenderer.Canvas, windowWidth, windowHeight, _scroll);

        _devTools.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset);

        var pixels = _skiaRenderer.GetPixelData();
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);

        _input.NeedsRedraw = false;
        if (_input.IsMouseDown()) _input.NeedsRedraw = true;
    }

    private void OnKeyDown(Key key)
    {
        _lastInputTime = DateTime.Now;
        if (key == Key.F12)
        {
            _devTools.Toggle();
            _input.NeedsRedraw = true;
            return;
        }
        _chrome.HandleKeyPress('\0', key switch
        {
            Key.Escape => SKKey.Escape,
            Key.F5 => SKKey.None,
            _ => SKKey.None
        });
    }

    private bool HandleDevToolsClick(float x, float y, bool isDown)
    {
        if (_devTools.HandleDragStart(x, y))
        {
            _devToolsFocused = true;
            return true;
        }
        bool handled = _devTools.HandleClick(x, y);
        if (handled)
        {
            _devToolsFocused = true;
            _focusedElement = null;
            PositionImeCaret();
            _input.NeedsRedraw = true;
        }
        return handled;
    }

    private void HandleDomClick(float x, float y)
    {
        if (_currentLoad == null) return;

        float adjustedY = y - _contentOffset + _scroll.ScrollY;
        var element = HitTest(_currentLoad.Document, x, adjustedY);
        if (element != null)
        {
            _jsEngine.DispatchEvent(element, "click");

            // Track focus on form elements
            if (element.IsFormElement)
            {
                _focusedElement = element;
                _imeCommittedText.Clear();
                PositionImeCaret();
            }
            else
            {
                _focusedElement = null;
                _imeCommittedText.Clear();
            }
        }
    }

    private bool DevToolsHandleInput(char c, Key key)
    {
        if (key == Key.Unknown && c == 1) return false;
        if (key == Key.Unknown && c == 3) return false;
        if (key == Key.Unknown && c == 22) return false;
        if (key == Key.Unknown && c == 24) return false;
        if (key == Key.Unknown && c == 26) return false;

        return _devTools.HandleKeyPress(c, key);
    }

    private void HandleImeChar(char charCode)
    {
        if (charCode == '\0') return;

        if (_devTools.IsInputField(_input.GetMousePosition().x, _input.GetMousePosition().y))
        {
            if (_devTools.HandleImeChar(charCode))
            {
                _input.NeedsRedraw = true;
                return;
            }
        }

        if (_focusedElement != null && _focusedElement.IsFormElement)
        {
            _imeCommittedText.Append(charCode);
            _jsEngine.DispatchEvent(_focusedElement, "input");
            _input.NeedsRedraw = true;
        }
        else if (_chrome.IsUrlBarFocused())
        {
            _chrome.HandleKeyPress(charCode, SKKey.None);
            _input.NeedsRedraw = true;
        }
    }

    private void PositionImeCaret()
    {
        var winWindow = _window as WindowsWindow;
        if (winWindow?.ImeHandler == null) return;

        if (_devTools.Visible && _devToolsFocused)
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            var caretPos = _devTools.GetCaretScreenPosition(ww, wh);
            if (caretPos.HasValue)
            {
                winWindow.ImeHandler.SetCaretPosition(
                    new SKPoint(caretPos.Value.X * _dpiScale, caretPos.Value.Y * _dpiScale),
                    18 * _dpiScale);
                return;
            }
        }

        if (_focusedElement == null || _currentLoad == null) return;

        var box = _focusedElement.LayoutBox;
        if (box == null) return;

        float caretScreenX = box.BorderBox.Left * _dpiScale;
        float caretScreenY = (box.BorderBox.Top - _scroll.ScrollY) * _dpiScale;
        float lineHeight = box.LineHeight * _dpiScale;

        winWindow.ImeHandler.SetCaretPosition(
            new SKPoint(caretScreenX, caretScreenY),
            lineHeight);
    }

    private void PerformCopy()
    {
        var (mx, my) = _input.GetMousePosition();
        if (_devTools.Visible && _devTools.IsInputField(mx, my))
        {
            string sel = _devTools.GetActiveTabSelectedText();
            if (!string.IsNullOrEmpty(sel))
                ClipboardHelper.SetText(sel);
        }
        else if (_chrome.IsUrlBarFocused())
        {
            string url = _chrome.GetCurrentUrl() ?? "";
            if (!string.IsNullOrEmpty(url))
                ClipboardHelper.SetText(url);
        }
    }

    private void PerformPaste()
    {
        string? text = ClipboardHelper.GetText();
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text)
            HandleImeChar(c);
    }

    private void PerformCut()
    {
        PerformCopy();
        if (_devTools.Visible && _devTools.IsInputField(_input.GetMousePosition().x, _input.GetMousePosition().y))
            HandleImeChar('\b');
    }

    private void PerformSelectAll()
    {
    }

    public void InjectImeChar(char c)
    {
        HandleImeChar(c);
    }

    private static UpBrowser.Core.Dom.Element? HitTest(UpBrowser.Core.Dom.Document doc, float x, float y)
    {
        UpBrowser.Core.Dom.Element? result = null;
        float lastZ = float.MinValue;

        HitTestElement(doc.DocumentElement, x, y, ref result, ref lastZ);
        if (result == null) HitTestElement(doc.Body, x, y, ref result, ref lastZ);
        return result;
    }

    private static void HitTestElement(UpBrowser.Core.Dom.Element? element, float x, float y,
        ref UpBrowser.Core.Dom.Element? result, ref float lastZ)
    {
        if (element == null) return;

        var box = element.LayoutBox;
        if (box != null && box.BorderBox.Contains(x, y))
        {
            float z = element.ComputedStyle?.ZIndex ?? 0;
            if (result == null || z >= lastZ)
            {
                result = element;
                lastZ = z;
            }
        }

        foreach (var child in element.Children.OfType<UpBrowser.Core.Dom.Element>())
            HitTestElement(child, x, y, ref result, ref lastZ);
    }

    public void Dispose()
    {
        _chrome.Dispose();
        _skiaRenderer.Dispose();
        _window.Dispose();
        _jsEngine.Dispose();
        _eventLoop.Stop();
        GC.SuppressFinalize(this);
    }
}
