using SkiaSharp;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using UpBrowser.Core;
using UpBrowser.Core.Css;
using UpBrowser.Core.Dom;
using UpBrowser.Core.EventLoop;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Layout;
using UpBrowser.Process;
using UpBrowser.Core.Performance;
using UpBrowser.Core.Performance.Diagnostics;
using UpBrowser.Core.Performance.Memory;
using UpBrowser.Core.Performance.Rendering;
using UpBrowser.Core.Performance.Resources;
using UpBrowser.Core.Performance.Scheduling;
using UpBrowser.Platform;
using UpBrowser.Rendering;
using UpBrowser.Rendering.DevTools;

namespace UpBrowser;

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
    private readonly UpBrowser.Core.Performance.Resources.StreamingHttpFetcher _httpFetcher = new();
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private DocumentManager.DocumentLoadResult? _currentLoad;
    private PaintVisitor? _cachedPaintVisitor;
    private DisplayList _displayList = new();
    private float _lastLayoutWidth;
    private string _currentHtml = "";

    // Performance-integrated layout engine (wraps the regular LayoutEngine with
    // LayoutCache + DirtyFlags so clean subtrees can skip work).
    private IncrementalLayoutEngine? _incrementalLayout;

    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private float _lastScrollX;
    private float _lastScrollY;
    private float _lastDevToolsHeight;
    private bool _lastDevToolsVisible;
    private long _lastTaskManagerRefresh;
    private readonly DevToolsPanel _devTools;

    private readonly ImageCache _sharedImageCache = new();
    private readonly Dictionary<string, SKTypeface> _sharedTypefaceCache = new();
    private static string[]? _fontFamilies;
    private bool _pendingRelayout;

    private readonly RenderingSettings _renderingSettings = new();
    private readonly RenderingSettingsPage _renderingSettingsPage;
    private readonly TaskManagerPage _taskManagerPage;
    private readonly ProcessManager _processManager;

    // Performance optimization layer. Lazily initialized in RunAsync so the
    // existing Chrome/loader flows are not perturbed on cold-start.
    private PerformanceHub? _perfHub;
    private SharedStyleCache? _sharedStyleCache;
    private LayoutCache? _layoutCache;

    private long _lastInputTimeTick = Environment.TickCount64;
    private const long InputCooldownMs = 80;

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
    private bool _devToolsFocused;
    private readonly PageInputImeHost _pageInputImeHost;
    private const float ScrollbarDragThreshold = 5;

    // Text selection support
    private bool _isSelecting;
    private SKPoint _selectionStart;
    private SKPoint _selectionEnd;
    private bool _hasSelection;

    public BrowserApp(int logicalWidth, int logicalHeight)
    {
        // Wire up SkiaSharp-based text measurement for accurate layout
        TextMeasurer.Instance = new Core.Layout.SkiaTextMeasurer();

        // Cache font families once at startup (avoids O(SKFontManager enumeration per frame)
        _fontFamilies ??= SkiaSharp.SKFontManager.Default.FontFamilies.ToArray();

        _dpiScale = PlatformFactory.GetDpiScale();
        Console.WriteLine($"DPI Scale: {_dpiScale:F2} ({_dpiScale * 100}%)");

        int physicalWidth = (int)(logicalWidth * _dpiScale);
        int physicalHeight = (int)(logicalHeight * _dpiScale);

        _window = PlatformFactory.CreateWindow(physicalWidth, physicalHeight, "UpBrowser");
        _docManager = new DocumentManager();
        _chrome = new ChromeRenderer();
        _scroll = new ScrollManager();
        _skiaRenderer = new SkiaRenderer();
        JsEngineConfig.DefaultEngineType = JsEngineType.Jint;
        JsEngineConfig.Initialize();
        _jsEngine = new JavaScriptEngine();
        _eventLoop = new EventLoop();
        _devTools = new DevToolsPanel();
        _pageInputImeHost = new PageInputImeHost(this);
        // Load persisted config, then apply default GPU
        RenderingSettingsConfig.Load(_renderingSettings);
        if (!_skiaRenderer.TrySetGpu(_renderingSettings.GpuAcceleration))
            Console.WriteLine("[Startup] GPU init failed, using CPU");

        _renderingSettingsPage = new RenderingSettingsPage(_renderingSettings, _dpiScale);
        _taskManagerPage = new TaskManagerPage();
        _processManager = new ProcessManager(_fontFamilies!, _eventLoop, _dpiScale, _chrome.GetContentOffset());
        _contentOffset = _chrome.GetContentOffset();
        _input = new InputHandler(_chrome, _scroll, _window, _dpiScale);
        _input.OnDomClick = HandleDomClick;
        _input.OnDevToolsKey = () =>
        {
            _devTools.Toggle();
            _devToolsFocused = _devTools.Visible;
            UpdateImeTarget();
            _input.NeedsRedraw = true;
        };

        _processManager.OnProcessUpdated += (proc) =>
        {
            if (proc.TabIndex == _chrome.ActiveTabIndex && proc.HasNewContent)
            {
                proc.HasNewContent = false;
                _input.NeedsRedraw = true;
            }
        };

        _chrome.OnSettingsClick = () =>
        {
            _renderingSettingsPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _chrome.OnTaskManagerClick = () =>
        {
            _taskManagerPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _taskManagerPage.OnChanged += () =>
        {
            _input.NeedsRedraw = true;
        };

        _renderingSettingsPage.OnChanged += () =>
        {
            _input.NeedsRedraw = true;
            _skiaRenderer.InvalidatePageCache();
        };

        _renderingSettings.OnChanged += () =>
        {
            RenderingSettingsConfig.Save(_renderingSettings);
            _window.TargetFrameTimeMs = _renderingSettings.TargetFps > 0
                ? (float)(1000.0 / _renderingSettings.TargetFps)
                : 1f;
        };

        _renderingSettings.OnGpuChanged += (enable) =>
        {
            _skiaRenderer.TrySetGpu(enable);
        };

        _chrome.OnChanged += () => _input.NeedsRedraw = true;
        _devTools.SetJavaScriptEngine(_jsEngine);
        _devTools.SetSourceChangeHandler(async (html) =>
        {
            _currentHtml = html;

            // Dispose old AngleSharp document to free memory
            if (_currentLoad != null)
            {
                _currentLoad.AngleSharpDoc?.Dispose();
            }

            _currentLoad = await _docManager.LoadHtmlAsync(html);
            var (pw, ph) = _window.GetClientSize();
            _jsEngine.LoadDocument(_currentLoad.Document);
            _devTools.SetDocument(_currentLoad.Document, html);
            _input.NeedsRedraw = true;
        });
        _devTools.OnChanged += () =>
        {
            UpdateImeTarget();
            _input.NeedsRedraw = true;
        };
        _input.OnDevToolsInput = (c, key, shift) => DevToolsHandleInput(c, key, shift);
        _input.OnDevToolsClick = (x, y, isDown) => HandleDevToolsClick(x, y, isDown);
        _input.OnTaskManagerKey = () =>
        {
            _taskManagerPage.Toggle();
            _input.NeedsRedraw = true;
        };

        _input.OnDevToolsWheel = (delta, mx, my) => _devTools.HandleWheel(delta, mx, my);
        _input.OnDevToolsMouseMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            _devTools.HandleMouseMove(x, y, ww, wh);
        };
        _input.OnScrollContainerWheel = (dx, dy, mx, my) => HandleScrollContainerWheel(dx, dy, mx, my);
        _input.OnImeChar = HandleImeChar;
        _input.OnImeTargetChanged = UpdateImeTarget;
        _input.OnCopy = PerformCopy;
        _input.OnPaste = PerformPaste;
        _input.OnCut = PerformCut;
        _input.OnSelectAll = PerformSelectAll;

        _jsEngine.ShowDialog = ShowDialog;

        // Wire LocationHost navigation callback
        if (_jsEngine.LocationHost != null)
        {
            _jsEngine.LocationHost.OnNavigate = (url) =>
            {
                Console.WriteLine($"Location navigate to: {url}");
                _eventLoop.PostTask(() =>
                {
                    if (!string.IsNullOrWhiteSpace(url))
                        _chrome.NavigateToUrl(url);
                });
            };
            _jsEngine.LocationHost.OnReload = () =>
            {
                _eventLoop.PostTask(() => _chrome.OnRefresh?.Invoke());
            };
        }

        // Wire window property delegates to real values
        if (_jsEngine.Builtins != null)
        {
            _jsEngine.Builtins.GetInnerWidth = () => (int)(_window.GetClientSize().width / _dpiScale);
            _jsEngine.Builtins.GetInnerHeight = () => (int)(_window.GetClientSize().height / _dpiScale);
            _jsEngine.Builtins.GetDevicePixelRatio = () => _dpiScale;
            _jsEngine.Builtins.GetScrollX = () => (int)_scroll.ScrollX;
            _jsEngine.Builtins.GetScrollY = () => (int)_scroll.ScrollY;
            _jsEngine.Builtins.OnScrollTo = (x, y) => _scroll.ScrollTo(x, y);
            _jsEngine.Builtins.OnScrollBy = (x, y) => _scroll.ScrollBy(x, y);
        }

        // Wire DOM keyboard events
        _input.OnDomKeyDown = (charCode, key, repeat) => HandleDomKeyDown(charCode, key, repeat);
        _input.OnDomKeyUp = (charCode, key, repeat) => HandleDomKeyUp(charCode, key, repeat);
        _input.OnDomChar = (charCode) => HandleDomChar(charCode);

        // Wire DOM mouse events
        _input.OnDomMouseMove = (x, y) => HandleDomMouseMove(x, y);
        _input.OnDomMouseDown = (x, y, isDown) => { /* handled via OnDomClick */ };
        _input.OnDomMouseUp = (x, y, isDown) => HandleDomMouseUp(x, y);

        _input.OnSettingsPageClick = (x, y, isUp) =>
        {
            if (isUp)
            {
                _renderingSettingsPage.HandleMouseUp();
                return false;
            }
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            return _renderingSettingsPage.HandleClick(x, y, ww, _contentOffset);
        };

        _input.OnSettingsPageMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            return _renderingSettingsPage.HandleMouseMove(x, y, ww, _contentOffset);
        };

        _input.OnSettingsPageWheel = (delta) =>
        {
            if (!_renderingSettingsPage.Visible) return false;
            var (pw, ph) = _window.GetClientSize();
            int wh = (int)(ph / _dpiScale);
            _renderingSettingsPage.HandleWheel(delta, wh, _contentOffset);
            return true;
        };

        _input.OnTaskManagerPageClick = (x, y, isUp) =>
        {
            if (isUp)
            {
                _taskManagerPage.HandleMouseUp();
                return false;
            }
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            return _taskManagerPage.HandleClick(x, y, ww, wh);
        };

        _input.OnTaskManagerPageMove = (x, y) =>
        {
            var (pw, ph) = _window.GetClientSize();
            int ww = (int)(pw / _dpiScale);
            int wh = (int)(ph / _dpiScale);
            return _taskManagerPage.HandleMouseMove(x, y, ww, wh);
        };

        _input.OnTaskManagerPageWheel = (delta) =>
        {
            if (!_taskManagerPage.Visible) return false;
            var (pw, ph) = _window.GetClientSize();
            int wh = (int)(ph / _dpiScale);
            _taskManagerPage.HandleWheel(delta, wh);
            return true;
        };

        _taskManagerPage.OnEndProcess += (tabIndex) =>
        {
            _chrome.CloseTab(tabIndex);
            _input.NeedsRedraw = true;
        };

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
        // Performance hub must be live before anything else uses the heavy
        // subsystems, so the first layout/style pass is already instrumented.
        InitializePerformanceHub();

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

        _skiaRenderer.Settings = _renderingSettings;

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

        var title = _currentLoad?.Document.Title ?? "UpBrowser";
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

        // ---- Initialize the performance layer ----
        InitializePerformanceHub();

        _currentHtml = File.ReadAllText(@"D:\Master\code\UpBrowser\test_css_features.html");
        var initialLoad = await _docManager.LoadHtmlAsync(_currentHtml);
        _currentLoad = initialLoad;

        var devTool = new LayoutDevTool();
        var debugReport = devTool.GenerateReport(_currentLoad!.Document, 1024, 768);
        File.WriteAllText("layout_debug.txt", debugReport);
        Console.WriteLine($"[Debug] Initial report saved ({debugReport.Length} chars)");
        Console.WriteLine(devTool.GenerateQuickReport(_currentLoad.Document));

        _jsEngine.LoadDocument(_currentLoad.Document);
        _devTools.SetDocument(_currentLoad.Document, _currentHtml);
        RunPageScripts(null);

        BuildDisplayList(1024, 768);
        _lastLayoutWidth = 1024;

        _lastActiveTabIndex = _chrome.ActiveTabIndex;
        _tabStates[_lastActiveTabIndex] = new TabState
        {
            Html = _currentHtml,
            LoadResult = _currentLoad,
            ScrollX = 0,
            ScrollY = 0
        };

        // Create process for the initial tab
        var initialProc = _processManager.CreateProcess(0, "upbrowser://local");
        initialProc.UpdateTitle(_currentLoad.Document.Title ?? "");

        var bodyBox = _currentLoad.Document.Body?.LayoutBox;
        var lastContentHeight = bodyBox?.BorderBox.Height ?? 0;

        WireNavigation();

        _window.Run(RenderFrame);

        Console.WriteLine("UpBrowser closed.");
    }

    private void WireNavigation()
    {
        _chrome.OnNavigate = (url) =>
        {
            Console.WriteLine($"Navigating to: {url}");
            _input.NeedsRedraw = true;

            if (url.StartsWith("upbrowser://"))
            {
                if (url == "upbrowser://newtab" || url == "upbrowser://local")
                {
                    _currentHtml = DocumentManager.DefaultHtml;
                    LoadAndRenderHtml(_currentHtml);
                    _scroll.ScrollTo(0, 0);
                }
            }
            else if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                NavigateToHttp(url);
            }
            // file:// 协议支持
            else if (url.StartsWith("file://"))
            {
                NavigateToFile(url);
            }
            else
            {
                NavigateToSearch(url);
            }
        };

        _chrome.OnRefresh = () =>
        {
            Console.WriteLine("Refreshing page...");
            _input.NeedsRedraw = true;
            if (!string.IsNullOrEmpty(_currentHtml))
            {
                LoadAndRenderHtml(_currentHtml);
            }
        };

        _chrome.OnHome = () =>
        {
            Console.WriteLine("Going home...");
            _chrome.NavigateToUrl("upbrowser://local");
        };

        _chrome.OnTabChanged = (url) =>
        {
            Console.WriteLine($"Tab changed to: {url}");
            _input.NeedsRedraw = true;

            int currentTabIndex = _chrome.ActiveTabIndex;

            // 保存当前标签页状态
            if (_lastActiveTabIndex >= 0 && _currentLoad != null)
            {
                _tabStates[_lastActiveTabIndex] = new TabState
                {
                    Html = _currentHtml,
                    LoadResult = _currentLoad,
                    ScrollX = _scroll.ScrollX,
                    ScrollY = _scroll.ScrollY,
                    DomNodeCount = CountDomNodes(_currentLoad.Document),
                    LayoutBoxCount = CountLayoutBoxes(_currentLoad.Document)
                };
                var oldProc = _processManager.GetProcess(_lastActiveTabIndex);
                if (oldProc != null)
                {
                    oldProc.UpdateTitle(_currentLoad.Document.Title ?? "");
                    oldProc.UpdateUrl(url);
                }
            }

            _lastActiveTabIndex = currentTabIndex;

            // 确保目标标签页有进程
            _processManager.GetOrCreate(currentTabIndex, url);

            // 尝试从进程获取最新的 DisplayList
            var procDl = _processManager.GetDisplayList(currentTabIndex);
            if (procDl != null && procDl.Count > 0)
            {
                _displayList = procDl;
            }

            // 检查目标标签页是否有已保存的状态
            if (_tabStates.TryGetValue(currentTabIndex, out var savedState) && !string.IsNullOrEmpty(savedState.Html))
            {
                _currentHtml = savedState.Html;
                _currentLoad = savedState.LoadResult;
                _scroll.ScrollTo(savedState.ScrollX, savedState.ScrollY);

                if (_currentLoad != null)
                {
                    _jsEngine.LoadDocument(_currentLoad.Document);
                    _devTools.SetDocument(_currentLoad.Document, _currentHtml);
                    BuildDisplayList(_lastWindowWidth, _lastWindowHeight);
                }
            }
            else
            {
                _isNavigating = false; // 清除旧导航，允许新导航
                if (url.StartsWith("http://") || url.StartsWith("https://"))
                {
                    NavigateToHttp(url);
                }
                else if (url == "upbrowser://newtab" || url == "upbrowser://local")
                {
                    _currentHtml = DocumentManager.DefaultHtml;
                    LoadAndRenderHtml(_currentHtml);
                }
            }
        };

        _chrome.OnNewTab = () =>
        {
            Console.WriteLine("New tab requested");
            int newIdx = _chrome.TabCount;
            _processManager.CreateProcess(newIdx, "upbrowser://newtab");
            _input.NeedsRedraw = true;
        };

        _chrome.OnCloseTab = (index) =>
        {
            Console.WriteLine($"Close tab {index} requested");
            _processManager.DestroyProcess(index);
            _input.NeedsRedraw = true;
        };
    }

    private bool _isNavigating;
    private int _navigationSeq;
    private int _lastActiveTabIndex = -1;
    private string? _currentBaseUrl;
    private int _navigatingTabIndex = -1; // which tab initiated the current navigation
    private readonly ConcurrentDictionary<int, TabState> _tabStates = new();

    private class TabState
    {
        public string Html { get; set; } = "";
        public DocumentManager.DocumentLoadResult? LoadResult { get; set; }
        public float ScrollX { get; set; }
        public float ScrollY { get; set; }
        public int DomNodeCount { get; set; }
        public int LayoutBoxCount { get; set; }
    }

    private void RunPageScripts(string? baseUrl)
    {
        if (_currentLoad == null) return;

        var angleDoc = _currentLoad.AngleSharpDoc;

        // First pass: collect all script elements
        var scriptElements = angleDoc.All.Where(e =>
            e.LocalName?.ToLowerInvariant() == "script").ToList();

        // Separate scripts by type
        var syncScripts = new List<AngleSharp.Dom.IElement>();
        var asyncScripts = new List<AngleSharp.Dom.IElement>();
        var deferScripts = new List<AngleSharp.Dom.IElement>();

        foreach (var scriptEl in scriptElements)
        {
            var type = scriptEl.GetAttribute("type");
            if (!string.IsNullOrEmpty(type) && type != "text/javascript" && type != "application/javascript" && type != "module")
                continue; // Skip non-JS scripts

            var isAsync = scriptEl.HasAttribute("async");
            var isDefer = scriptEl.HasAttribute("defer");
            var isModule = type == "module";

            if (isAsync || isModule)
                asyncScripts.Add(scriptEl);
            else if (isDefer)
                deferScripts.Add(scriptEl);
            else
                syncScripts.Add(scriptEl);
        }

        // Execute synchronous scripts immediately (in order)
        foreach (var scriptEl in syncScripts)
        {
            ExecuteScriptElement(scriptEl, baseUrl);
        }

        // Execute async scripts as soon as they're loaded
        foreach (var scriptEl in asyncScripts)
        {
            ExecuteScriptElementAsync(scriptEl, baseUrl);
        }

        // Execute defer scripts after document parsing (simulate)
        foreach (var scriptEl in deferScripts)
        {
            ExecuteScriptElementAsync(scriptEl, baseUrl);
        }
    }

    private void ExecuteScriptElement(AngleSharp.Dom.IElement scriptEl, string? baseUrl)
    {
        var src = scriptEl.GetAttribute("src");
        if (!string.IsNullOrEmpty(src))
        {
            var absoluteUrl = ResolveUrl(src, baseUrl);
            if (absoluteUrl == null)
            {
                Console.WriteLine($"[JS] Invalid script URL: {src}");
                return;
            }

            try
            {
                var resp = _httpFetcher.FetchAsync(new ResourceRequest
                {
                    Url = absoluteUrl,
                    Kind = ResourceKind.Script,
                    Priority = ResourcePriority.High,
                    Timeout = TimeSpan.FromSeconds(10),
                }).GetAwaiter().GetResult();
                var code = System.Text.Encoding.UTF8.GetString(resp.Body);
                _jsEngine.Execute(code);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JS] Failed to load script '{src}': {ex.Message}");
            }
        }
        else
        {
            var code = scriptEl.TextContent;
            if (!string.IsNullOrWhiteSpace(code))
            {
                _jsEngine.Execute(code);
            }
        }
    }

    private void ExecuteScriptElementAsync(AngleSharp.Dom.IElement scriptEl, string? baseUrl)
    {
        Task.Run(async () =>
        {
            try
            {
                var src = scriptEl.GetAttribute("src");
                string code;
                if (!string.IsNullOrEmpty(src))
                {
                    var absoluteUrl = ResolveUrl(src, baseUrl);
                    if (absoluteUrl == null)
                    {
                        Console.WriteLine($"[JS] Invalid script URL: {src}");
                        return;
                    }

                    var resp = await _httpFetcher.FetchAsync(new ResourceRequest
                    {
                        Url = absoluteUrl,
                        Kind = ResourceKind.Script,
                        Priority = ResourcePriority.High,
                        Timeout = TimeSpan.FromSeconds(10),
                    });
                    code = System.Text.Encoding.UTF8.GetString(resp.Body);
                }
                else
                {
                    code = scriptEl.TextContent;
                }

                if (!string.IsNullOrWhiteSpace(code))
                {
                    _eventLoop.PostTask(() => _jsEngine.Execute(code));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JS] Failed to load script: {ex.Message}");
            }
        });
    }

    private static string? ResolveUrl(string url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:") || url.StartsWith("blob:"))
            return url;

        if (url.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + url;
            return "http:" + url;
        }

        if (string.IsNullOrEmpty(baseUrl)) return null;
        try
        {
            var baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return null; }
    }

    private void NavigateToHttp(string url)
    {
        _isNavigating = false; // allow new navigation to interrupt previous one
        int seq = Interlocked.Increment(ref _navigationSeq);
        int tabIdx = _chrome.ActiveTabIndex;
        _navigatingTabIndex = tabIdx;
        _isNavigating = true;
        _chrome.SetLoadingState(true);
        _input.NeedsRedraw = true;

        Task.Run(async () =>
        {
            try
            {
                var request = new ResourceRequest
                {
                    Url = url,
                    Kind = ResourceKind.Document,
                    Priority = ResourcePriority.VeryHigh,
                    Timeout = TimeSpan.FromSeconds(30),
                    Headers = new Dictionary<string, string>
                    {
                        ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
                        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                        ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8",
                    },
                };
                var response = await _httpFetcher.FetchAsync(request);

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                    throw new HttpRequestException($"HTTP {response.StatusCode}");

                var webHtml = System.Text.Encoding.UTF8.GetString(response.Body);
                var finalUrl = response.FinalUrl ?? url;

                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return; // stale navigation
                    // 如果导航期间切换了标签页，保存到对应标签页的状态中
                    if (_chrome.ActiveTabIndex != tabIdx)
                    {
                        _tabStates[tabIdx] = new TabState { Html = webHtml };
                        _chrome.UpdateUrl(finalUrl);
                        return;
                    }
                    var savedUrl = finalUrl;
                    _chrome.UpdateUrl(savedUrl);
                    LoadAndRenderHtml(webHtml, savedUrl);
                });
            }
            catch (TaskCanceledException)
            {
                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return;
                    if (_chrome.ActiveTabIndex != tabIdx) return;
                    ShowErrorPage(url, "Request timed out");
                });
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() =>
                {
                    if (seq != _navigationSeq) return;
                    if (_chrome.ActiveTabIndex != tabIdx) return;
                    ShowErrorPage(url, ex.Message);
                });
            }
        });
    }

    private void NavigateToFile(string url)
    {
        if (_isNavigating)
            _isNavigating = false; // 允许新导航中断之前的请求

        _isNavigating = true;
        _chrome.SetLoadingState(true);
        _input.NeedsRedraw = true;

        Task.Run(async () =>
        {
            string filePath;
            try
            {
                // 将 file:///C:/path/file.html 转换为本地路径
                var uri = new Uri(url);
                filePath = uri.LocalPath; // Windows: "C:\\path\\file.html"
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() => ShowErrorPage(url, ex.Message));
                return;
            }

            try
            {
                string html = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                // 使用文件所在目录作为 baseUrl，用于解析相对路径的资源
                string baseDir = Path.GetDirectoryName(filePath)?.Replace('\\', '/');
                string baseUrl = baseDir != null ? $"file://{baseDir}/" : null;

                _eventLoop.PostTask(() => LoadAndRenderHtml(html, baseUrl));
            }
            catch (Exception ex)
            {
                _eventLoop.PostTask(() => ShowErrorPage(url, ex.Message));
            }
        });
    }

    private void LoadAndRenderHtml(string html, string? baseUrl = null)
    {
        _currentHtml = html;
        _currentBaseUrl = baseUrl;
        _sharedImageCache.Clear();
        _sharedTypefaceCache.Clear();
        _hasSelection = false;
        _isSelecting = false;
        _hoveredElement = null;

        // Parse HTML on background thread
        // Capture current viewport and dpi so layout during load uses correct CSS pixel size
        var (pw_cap, ph_cap) = _window.GetClientSize();
        float viewportWidthCss_cap = pw_cap / _dpiScale;
        float viewportHeightCss_cap = ph_cap / _dpiScale;

        Task.Run(async () =>
        {
            DocumentManager.DocumentLoadResult? loadResult = null;
            try
            {
                var docManager = new DocumentManager();
                loadResult = await docManager.LoadHtmlAsync(html, baseUrl, viewportWidthCss_cap, viewportHeightCss_cap, _dpiScale);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Load] Error loading HTML: {ex.Message}");
                var errorHtml = $@"<!DOCTYPE html>
<html><head><title>Error</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #d32f2f;'>Rendering Error</h1>
    <p style='color: #666;'>Error: {ex.Message}</p>
</body></html>";
                try
                {
                    var docManager = new DocumentManager();
                    loadResult = await docManager.LoadHtmlAsync(errorHtml, baseUrl, viewportWidthCss_cap, viewportHeightCss_cap, _dpiScale);
                }
                catch { return; }
            }

            if (loadResult != null)
            {
                var capturedHtml = html;
                _eventLoop.PostTask(() => ApplyLoadedHtml(loadResult, capturedHtml));
            }
        });
    }

    private void ApplyLoadedHtml(DocumentManager.DocumentLoadResult loadResult, string html)
    {
        int tabIdx = _chrome.ActiveTabIndex;

        // Dispose old document safely
        if (_currentLoad != null)
        {
            try { _currentLoad.AngleSharpDoc?.Dispose(); }
            catch (Exception ex) { Console.WriteLine($"[Dispose] Error: {ex.Message}"); }
        }

        _currentLoad = loadResult;
        _currentHtml = html;

        var (pw, ph) = _window.GetClientSize();
        int ww = (int)(pw / _dpiScale);
        int wh = (int)(ph / _dpiScale);

        _jsEngine.LoadDocument(_currentLoad.Document);
        _devTools.SetDocument(_currentLoad.Document, html);

        RunPageScripts(_currentBaseUrl);

        BuildDisplayList(ww, wh);
        _scroll.ScrollTo(0, 0);

        // Update layout debug report on every page load
        try
        {
            var devTool = new LayoutDevTool();
            var debugReport = devTool.GenerateReport(_currentLoad.Document, ww, wh);
            File.WriteAllText("layout_debug.txt", debugReport);
            Console.WriteLine($"[Debug] Report updated ({debugReport.Length} chars)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Debug] Failed to generate report: {ex.Message}");
        }

        _lastActiveTabIndex = tabIdx;
        _tabStates[tabIdx] = new TabState
        {
            Html = _currentHtml,
            LoadResult = _currentLoad,
            ScrollX = 0,
            ScrollY = 0
        };

        // Update process manager for the active tab
        var activeProc = _processManager.GetProcess(tabIdx);
        if (activeProc != null)
        {
            activeProc.UpdateTitle(_currentLoad?.Document.Title ?? "");
            activeProc.UpdateUrl(_currentBaseUrl ?? "");
            int nDom = _currentLoad != null ? CountDomNodes(_currentLoad.Document) : 0;
            int nBox = _currentLoad != null ? CountLayoutBoxes(_currentLoad.Document) : 0;
            long memBytes = System.GC.GetTotalMemory(false);
            activeProc.UpdateContentMetrics(nDom, nBox, memBytes,
                _jsEngine.GetHeapSizeKB(), _jsEngine.TimerCount);
        }

        _isNavigating = false;
        _chrome.SetLoadingState(false);
        _input.NeedsRedraw = true;
    }

    private void ShowErrorPage(string url, string errorMessage)
    {
        var errorHtml = $@"<!DOCTYPE html>
<html><head><title>Error</title>
<style>
    body {{ font-family: 'Microsoft YaHei', Arial, sans-serif; padding: 40px; background: #f5f5f5; }}
    .error-container {{ max-width: 600px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
    h1 {{ color: #d32f2f; margin-top: 0; }}
    .url {{ color: #666; word-break: break-all; }}
    .error {{ color: #999; margin-top: 20px; }}
    .retry-btn {{ background: #1a73e8; color: white; border: none; padding: 10px 20px; border-radius: 4px; cursor: pointer; margin-top: 20px; }}
</style>
</head>
<body>
    <div class='error-container'>
        <h1>Unable to connect</h1>
        <p>Failed to load: <span class='url'>{url}</span></p>
        <p class='error'>Error: {errorMessage}</p>
        <button class='retry-btn' onclick='location.reload()'>Retry</button>
    </div>
</body></html>";
        _isNavigating = false;
        _chrome.SetLoadingState(false);
        _input.NeedsRedraw = true;
        LoadAndRenderHtml(errorHtml, url);
    }

    private void NavigateToSearch(string query)
    {
        var searchHtml = $@"<!DOCTYPE html>
<html><head><title>Search: {query}</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #1a73e8;'>Search</h1>
    <p>Searching for: <strong>{query}</strong></p>
    <p style='color: #666;'>Search functionality requires network access.</p>
</body></html>";
        LoadAndRenderHtml(searchHtml);
    }

    private void BuildDisplayList(float windowWidth, float windowHeight)
    {
        if (_currentLoad == null) return;

        // Wrap the heavy work in the long-task observer so we get metrics
        // for free. When the hub is not initialised this is a no-op.
        if (_perfHub is { Enabled: true })
        {
            _perfHub.LongTasks.Observe("BuildDisplayList", TaskPriority.High, () =>
            {
                BuildDisplayListImpl(windowWidth, windowHeight);
            });
        }
        else
        {
            BuildDisplayListImpl(windowWidth, windowHeight);
        }
    }

    private void BuildDisplayListImpl(float windowWidth, float windowHeight)
    {
        // Route layout through the incremental engine when the hub is enabled.
        // The incremental engine consults LayoutCache + DirtyFlags and skips
        // clean subtrees, which is the main win on small JS-driven DOM updates.
        if (_perfHub is { Enabled: true } && _incrementalLayout is { } incLayout)
        {
            // When the viewport size changes we need a full re-layout. Bumping
            // the layout version invalidates the cache for all nodes that don't
            // already carry a dirty flag.
            DirtyState.BumpLayoutVersion(_currentLoad.Document.DocumentElement ?? _currentLoad.Document.Body!);
            incLayout.Layout(_currentLoad.Document, windowWidth, windowHeight, _dpiScale, 16f);

            // Surface incremental stats to the dev tools feed.
            _perfHub.Registry.Feed.Append("layout",
                $"skipped={incLayout.Stats.NodesSkipped} relaid={incLayout.Stats.NodesReLaid} " +
                $"hit={incLayout.Stats.CacheHits} miss={incLayout.Stats.CacheMisses} " +
                $"{incLayout.Stats.ElapsedMillis:F2}ms");
        }
        else
        {
            _layout.Layout(_currentLoad.Document, windowWidth, windowHeight);
        }

        // Return old display list ops to pool before creating new one (fixes memory leak)
        _displayList.Clear();

        _cachedPaintVisitor = new PaintVisitor(_contentOffset, _sharedTypefaceCache, _sharedImageCache, _fontFamilies, _currentBaseUrl);
        _cachedPaintVisitor.SetFocusedElement(_focusedElement);
        if (_hasSelection)
        {
            _cachedPaintVisitor.SetSelectionRange(_selectionStart, _selectionEnd);
        }
        _cachedPaintVisitor.VisitDocument(_currentLoad.Document);
        _displayList = _cachedPaintVisitor.GetDisplayList();
        _displayList.SortByZIndex();
        _displayList.BuildSpatialGrid();

        // Invalidate the cached SKPicture so SkiaRenderer re-records on next frame
        _skiaRenderer.InvalidatePageCache();
    }

    private void RenderFrame(double dt)
    {
        // Drive the cooperative scheduler for the frame and observe memory pressure.
        RunPerfFrame(dt);

        _eventLoop.ProcessTasks();
        // Run JS-engine timer callbacks through the cooperative scheduler. The
        // scheduler is configured with a per-frame budget so a runaway timer
        // cannot starve the render loop.
        if (_jsEngine.HasTimers)
        {
            if (_perfHub is { Enabled: true })
            {
                _perfHub.Scheduler.PostTask(() => _jsEngine.TickTimers(), TaskPriority.Normal);
            }
            else
            {
                _jsEngine.TickTimers();
            }
        }

        _chrome.UpdateLoadingProgress();

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

        // Scroll velocity = (Δscroll / Δt) in pixels/second. We feed the result
        // into the predictive tile scheduler so the compositor can pre-rasterise
        // tiles in the direction of travel. The velocity is decayed each frame
        // so the prediction window naturally narrows when the user stops
        // scrolling.
        if (scrollChanged && dt > 0.0001)
        {
            float vx = (_scroll.ScrollX - _lastScrollX) / (float)dt * 1000f;
            float vy = (_scroll.ScrollY - _lastScrollY) / (float)dt * 1000f;
            _skiaRenderer.ReportScrollVelocity(vx, vy);
        }
        else
        {
            // Apply a gentle decay so the predictor stops firing when the user
            // is idle. We multiply by 0.5 per frame, which means the velocity
            // signal is effectively zero after ~5 frames (~80 ms at 60 fps).
            _skiaRenderer.ReportScrollVelocity(0, 0);
        }

        bool inputRecently = Environment.TickCount64 - _lastInputTimeTick < InputCooldownMs;

        // Accumulate pending relayout flag
        if (_jsEngine.NeedsReLayout)
            _pendingRelayout = true;

        if (_taskManagerPage.Visible)
        {
            long now = Environment.TickCount64;
            if (now - _lastTaskManagerRefresh > 1000)
            {
                _lastTaskManagerRefresh = now;
                _input.NeedsRedraw = true;
            }
        }

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
            _skiaRenderer.InvalidatePageCache();
            // 清除文本测量缓存，确保重新测量所有文本宽度，支持动态换行
            (TextMeasurer.Instance as SkiaTextMeasurer)?.ClearCache();
            _pendingRelayout = true;
        }

        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight() - currentDevToolsHeight;

        if (sizeChanged || windowWidth != _lastLayoutWidth || _pendingRelayout || devToolsChanged || _input.NeedsRedraw)
        {
            _lastLayoutWidth = windowWidth;

            if (_pendingRelayout)
            {
                var styleComputer = _currentLoad.StyleComputer;
                if (styleComputer == null)
                {
                    styleComputer = new StyleComputer();
                    styleComputer.AddStylesheet(_docManager.GetUaStylesheet());
                }
                // Style compute: timing is already recorded inside CascadeResolver
                // via PipelineTimings.Style. We additionally wrap in a long-task
                // observer so anything > 50 ms is reported.
                if (_perfHub is { Enabled: true })
                {
                    _perfHub.LongTasks.Observe("StyleCompute", TaskPriority.High,
                        () => styleComputer.ComputeStyles(_currentLoad.Document, windowWidth, contentViewportHeight));
                }
                else
                {
                    styleComputer.ComputeStyles(_currentLoad.Document, windowWidth, contentViewportHeight);
                }
                _jsEngine.ClearDirty();
                _pendingRelayout = false;
            }

            BuildDisplayList(windowWidth, Math.Max(100, (int)contentViewportHeight));

            var bodyBox = _currentLoad.Document.Body?.LayoutBox;
            float contentWidth = bodyBox?.BorderBox.Width ?? windowWidth;
            float contentHeight = bodyBox?.BorderBox.Height ?? 0;

            _scroll.UpdateScroll(contentWidth, contentHeight, windowWidth, contentViewportHeight);

            _window.UpdateImeCompositionWindow();
        }

        if (_focusedElement != null && _focusedElement.IsFormElement)
        {
            _window.UpdateImeCompositionWindow();
        }

        _lastScrollX = _scroll.ScrollX;
        _lastScrollY = _scroll.ScrollY;
        _lastDevToolsHeight = currentDevToolsHeight;
        _lastDevToolsVisible = _devTools.Visible;

        _skiaRenderer.Canvas.Clear(SKColors.White);

        var title = _currentLoad.Document.Title ?? "UpBrowser";
        var currentUrl = _chrome.GetCurrentUrl();
        if (string.IsNullOrEmpty(currentUrl))
            currentUrl = "upbrowser://local";

        // Update tab tooltips
        int activeTabIdx = _chrome.ActiveTabIndex;
        for (int i = 0; i < _chrome.Tabs.Count; i++)
        {
            var tab = _chrome.Tabs[i];
            int nDom = 0, nBox = 0;
            if (i == activeTabIdx && _currentLoad != null)
            {
                nDom = CountDomNodes(_currentLoad.Document);
                nBox = CountLayoutBoxes(_currentLoad.Document);
            }
            else if (_tabStates.TryGetValue(i, out var st))
            {
                nDom = st.DomNodeCount;
                nBox = st.LayoutBoxCount;
            }
            tab.TooltipText = $"Title: {tab.Title}\nURL: {(string.IsNullOrEmpty(tab.Url) ? "upbrowser://newtab" : tab.Url)}\nDOM Nodes: {nDom}\nLayout Boxes: {nBox}\nStatus: {(tab.IsLoading ? "Loading" : (i == activeTabIdx ? "Running" : "Complete"))}";
        }

        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl, title);

        _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
            _scroll.ScrollX, _scroll.ScrollY,
            windowWidth, contentViewportHeight);

        _chrome.RenderScrollbars(_skiaRenderer.Canvas, windowWidth, windowHeight, _scroll);

        _chrome.RenderTabTooltip(_skiaRenderer.Canvas, windowWidth, windowHeight);

        _devTools.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset);

        _renderingSettingsPage.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset);

        // Build rich data for task manager with multi-process metrics
        var tmRows = new System.Collections.Generic.List<TmRowData>();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();

        // Collect per-tab process metrics
        var allProcMetrics = _processManager.GetAllMetrics();
        double wsmb = proc.WorkingSet64 / (1024.0 * 1024.0);
        double heapMB = System.GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        // Performance pipeline timings (read accumulators for latest values)
        double styleMs = PipelineTimings.Style.MeanMillis;
        double layoutMs = PipelineTimings.Layout.MeanMillis;
        double paintMs = PipelineTimings.Paint.MeanMillis;
        double scriptMs = PipelineTimings.Script.MeanMillis;
        double compositeMs = PipelineTimings.Composite.MeanMillis;
        double imageDecodeMs = PipelineTimings.ImageDecode.MeanMillis;
        double tileRasterMs = PipelineTimings.TileRaster.MeanMillis;
        double networkMs = PipelineTimings.NetworkWait.MeanMillis;

        // Memory breakdown from performance subsystems
        double imagePoolMB = _perfHub?.ImagePool.CapacityBytes / (1024.0 * 1024.0) ?? 0;
        double tileMemoryMB = (_perfHub?.Tiles.Settings?.MaxTilesInMemory ?? 0) * (256.0 * 256.0 * 4.0) / (1024.0 * 1024.0);

        // Rendering counters
        int tilesRast = (int)PipelineTimings.TilesRasterized.Value;
        int tilesReused = (int)PipelineTimings.TilesReused.Value;
        int imagesDecoded = (int)PipelineTimings.ImagesDecoded.Value;
        int imageHits = (int)PipelineTimings.ImageCacheHits.Value;
        int cacheHits = (int)PipelineTimings.ResourceCacheHits.Value;

        // JS stats
        int jsHeapSize = _jsEngine.GetHeapSizeKB();
        int jsCallbacks = _jsEngine.TimerCount;

        // Frame timing from the window
        double frameTimeMs = Math.Max(dt, 1.0 / 1000.0);
        double fps = 1000.0 / frameTimeMs;

        tmRows.Add(new TmRowData
        {
            Name = "Browser",
            Detail = $"PID: {proc.Id}",
            Memory = $"{wsmb:F1} MB",
            Cpu = "",
            Status = "Running",
            Pid = proc.Id,
            TabIndex = -1,
            StyleTimingMs = styleMs,
            LayoutTimingMs = layoutMs,
            PaintTimingMs = paintMs,
            ScriptTimingMs = scriptMs,
            CompositeTimingMs = compositeMs,
            ImageDecodeTimingMs = imageDecodeMs,
            TileRasterTimingMs = tileRasterMs,
            NetworkWaitTimingMs = networkMs,
            WorkingSetMB = wsmb,
            ManagedHeapMB = heapMB,
            ImageCacheMB = imagePoolMB,
            TileMemoryMB = tileMemoryMB,
            TilesRasterized = tilesRast,
            TilesReused = tilesReused,
            ImagesDecoded = imagesDecoded,
            ImageCacheHits = imageHits,
            ResourceCacheHits = cacheHits,
            JsHeapSizeKB = jsHeapSize,
            JsCallbackCount = jsCallbacks,
            FrameTimeMs = frameTimeMs,
            Fps = fps,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Working Set",
            Detail = "",
            Memory = $"{wsmb:F1} MB",
            Status = "",
            TabIndex = -1,
            WorkingSetMB = wsmb,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Managed Heap",
            Detail = "",
            Memory = $"{heapMB:F1} MB",
            Status = "",
            TabIndex = -1,
            ManagedHeapMB = heapMB,
        });
        tmRows.Add(new TmRowData
        {
            Name = "  Image Cache",
            Detail = "",
            Memory = $"{imagePoolMB:F1} MB",
            Status = "",
            TabIndex = -1,
            ImageCacheMB = imagePoolMB,
        });

        var tabSnapshot = _chrome.SnapshotTabs();
        for (int i = 0; i < tabSnapshot.Length; i++)
        {
            var tab = tabSnapshot[i];
            int domNodes = 0, layoutBoxes = 0;
            double memMB = 0;
            if (i == activeTabIdx && _currentLoad != null)
            {
                domNodes = CountDomNodes(_currentLoad.Document);
                layoutBoxes = CountLayoutBoxes(_currentLoad.Document);
            }
            else if (_tabStates.TryGetValue(i, out var st))
            {
                domNodes = st.DomNodeCount;
                layoutBoxes = st.LayoutBoxCount;
            }

            // Use per-process metrics when available
            var tcMetrics = _processManager.GetMetrics(i);
            if (tcMetrics != null)
            {
                if (domNodes == 0) domNodes = tcMetrics.DomNodeCount;
                if (layoutBoxes == 0) layoutBoxes = tcMetrics.LayoutBoxCount;
                memMB = tcMetrics.MemoryBytes / (1024.0 * 1024.0);
            }

            string detail = string.IsNullOrEmpty(tab.Url) || tab.Url == "upbrowser://newtab" ? "" : tab.Url;
            string status = tab.IsLoading ? "Loading" : (i == activeTabIdx ? "Running" : "Complete");
            // 每个标签页只显示自己独立的内存数据（来自 TabProcess），
            // 不显示整个进程的内存占用，避免误导
            tmRows.Add(new TmRowData
            {
                Name = string.IsNullOrEmpty(tab.Title) ? "New Tab" : tab.Title,
                Detail = detail,
                Memory = memMB > 0 ? $"{memMB:F1} MB" : "-",
                Cpu = i == activeTabIdx ? "" : "-",
                DomNodes = domNodes,
                LayoutBoxes = layoutBoxes,
                Status = status,
                TabIndex = i,
                StyleTimingMs = i == activeTabIdx ? styleMs : 0,
                LayoutTimingMs = i == activeTabIdx ? layoutMs : 0,
                PaintTimingMs = i == activeTabIdx ? paintMs : 0,
                ScriptTimingMs = i == activeTabIdx ? scriptMs : 0,
                CompositeTimingMs = i == activeTabIdx ? compositeMs : 0,
                ImageDecodeTimingMs = i == activeTabIdx ? imageDecodeMs : 0,
                TileRasterTimingMs = i == activeTabIdx ? tileRasterMs : 0,
                NetworkWaitTimingMs = i == activeTabIdx ? networkMs : 0,
                WorkingSetMB = memMB,
                ManagedHeapMB = i == activeTabIdx ? heapMB : 0,
                ImageCacheMB = i == activeTabIdx ? imagePoolMB : 0,
                TileMemoryMB = i == activeTabIdx ? tileMemoryMB : 0,
                TilesRasterized = i == activeTabIdx ? tilesRast : 0,
                TilesReused = i == activeTabIdx ? tilesReused : 0,
                ImagesDecoded = i == activeTabIdx ? imagesDecoded : 0,
                ImageCacheHits = i == activeTabIdx ? imageHits : 0,
                ResourceCacheHits = i == activeTabIdx ? cacheHits : 0,
                JsHeapSizeKB = (i == activeTabIdx ? jsHeapSize : (tcMetrics?.JsHeapSizeKB ?? 0)),
                JsCallbackCount = (i == activeTabIdx ? jsCallbacks : (tcMetrics?.JsTimerCount ?? 0)),
                FrameTimeMs = i == activeTabIdx ? frameTimeMs : 0,
                Fps = i == activeTabIdx ? fps : 0,
            });
        }

        _taskManagerPage.Render(_skiaRenderer.Canvas, windowWidth, windowHeight, _contentOffset, tmRows);

        _skiaRenderer.TickFrame();
        _skiaRenderer.RenderFpsCounter(_skiaRenderer.Canvas, windowWidth, windowHeight);

        var pixels = _skiaRenderer.GetPixelData();
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);

        _input.NeedsRedraw = false;
        if (_input.IsMouseDown()) _input.NeedsRedraw = true;
    }

    private void UpdateImeTarget()
    {
        if (_chrome.IsUrlBarFocused())
        {
            _devToolsFocused = false;
            _focusedElement = null;
            _window.SetImeTarget(_chrome);
        }
        else if (_devToolsFocused && _devTools.Visible)
        {
            _focusedElement = null;
            var ime = _devTools.GetActiveImeSupport();
            _window.SetImeTarget(ime);
        }
        else if (_focusedElement != null && _focusedElement.IsFormElement)
        {
            _devToolsFocused = false;
            _window.SetImeTarget(_pageInputImeHost);
        }
        else
        {
            _devToolsFocused = false;
            _focusedElement = null;
            _window.SetImeTarget(null);
        }
    }

    private void OnKeyDown(Key key)
    {
        _lastInputTimeTick = Environment.TickCount64;
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
        if (!_devTools.Visible) return false;

        if (isDown)
        {
            _devTools.HandleMouseUp(x, y);
            return true;
        }

        if (_devTools.HandleDragStart(x, y))
        {
            _devToolsFocused = true;
            _focusedElement = null;
            UpdateImeTarget();
            return true;
        }
        bool handled = _devTools.HandleClick(x, y);
        if (handled)
        {
            _devToolsFocused = true;
            _focusedElement = null;
            _window.UpdateImeCompositionWindow();
            _input.NeedsRedraw = true;
        }
        else
        {
            _devToolsFocused = false;
        }
        UpdateImeTarget();
        return handled;
    }

    private void HandleDomClick(float x, float y)
    {
        if (_currentLoad == null) return;

        float docX = x + _scroll.ScrollX;
        float adjustedY = y - _contentOffset + _scroll.ScrollY;
        var element = HitTest(_currentLoad.Document, docX, adjustedY);
        Console.WriteLine($"[Click] HitTest found: {element?.TagName} at ({docX:F1},{adjustedY:F1})");
        if (element != null)
        {
            // Handle <summary> click to toggle parent <details>
            if (element.TagName == "SUMMARY")
            {
                var detailsParent = element.ParentElement;
                while (detailsParent != null && detailsParent.TagName != "DETAILS")
                    detailsParent = detailsParent.ParentElement;
                if (detailsParent != null)
                {
                    if (detailsParent.HasAttribute("open"))
                        detailsParent.RemoveAttribute("open");
                    else
                        detailsParent.SetAttribute("open", "");
                    MarkSubtreeDirty((UpBrowser.Core.Dom.Element)detailsParent, DirtyFlags.AllLayout);
                    _pendingRelayout = true;
                    return;
                }
            }

            bool shouldProceed = _jsEngine.DispatchEvent(element, "click");

            // Dispatch focus/blur when focused element changes
            if (element.IsFormElement)
            {
                if (_focusedElement != element)
                {
                    if (_focusedElement != null)
                        _jsEngine.DispatchEvent(_focusedElement, "blur");
                    _focusedElement = element;
                    _jsEngine.DispatchEvent(element, "focus");
                    _window.UpdateImeCompositionWindow();
                    _hasSelection = false;
                }
            }
            else
            {
                if (_focusedElement != null)
                {
                    _jsEngine.DispatchEvent(_focusedElement, "blur");
                    _focusedElement = null;
                }

                // Start text selection on non-interactive elements
                if (shouldProceed && !IsInteractiveElement(element))
                {
                    _isSelecting = true;
                    // Selection coordinates in display-list space (window coords at scroll=0):
                    // DL_X = x + scrollX, DL_Y = y + scrollY
                    _selectionStart = new SKPoint(x + _scroll.ScrollX, y + _scroll.ScrollY);
                    _selectionEnd = _selectionStart;
                    _hasSelection = false;
                }
                else
                {
                    _hasSelection = false;
                }
            }

            if (shouldProceed)
                NavigateForLinkClick(element);
        }
        else
        {
            if (_focusedElement != null)
            {
                _jsEngine.DispatchEvent(_focusedElement, "blur");
                _focusedElement = null;
            }
            _hasSelection = false;
        }
        UpdateImeTarget();
    }

    private static bool IsInteractiveElement(Core.Dom.Element element)
    {
        return element.TagName is "A" or "BUTTON" or "INPUT" or "TEXTAREA" or "SELECT"
            || element.GetAttribute("onclick") != null
            || element.GetAttribute("role") == "button";
    }

    private void NavigateForLinkClick(Core.Dom.Element element)
    {
        // Walk up the element tree to find an <a> tag
        var current = element;
        while (current != null)
        {
            if (current.TagName == "A")
            {
                var href = current.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var url = href;
                    // Resolve relative URLs against current base URL
                    if (!url.Contains("://") && !url.StartsWith("//") && !string.IsNullOrEmpty(_currentBaseUrl))
                    {
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl.EndsWith('/') ? _currentBaseUrl : _currentBaseUrl + '/');
                            url = new Uri(baseUri, url).ToString();
                        }
                        catch { }
                    }
                    else if (url.StartsWith("//"))
                    {
                        // Protocol-relative URL
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl ?? "https://example.com");
                            url = baseUri.Scheme + ":" + url;
                        }
                        catch { }
                    }

                    Console.WriteLine($"Link click navigating to: {url}");
                    _chrome.NavigateToUrl(url);
                    return;
                }
            }

            // Check target="_blank" on anchor or area
            var target = current.GetAttribute("target");
            if (target == "_blank")
            {
                var href = current.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    Console.WriteLine($"New tab link: {href}");
                    // For now, navigate in same tab
                    var url = href;
                    if (!url.Contains("://") && !string.IsNullOrEmpty(_currentBaseUrl))
                    {
                        try
                        {
                            var baseUri = new Uri(_currentBaseUrl.EndsWith('/') ? _currentBaseUrl : _currentBaseUrl + '/');
                            url = new Uri(baseUri, url).ToString();
                        }
                        catch { }
                    }
                    _chrome.NavigateToUrl(url);
                    return;
                }
            }

            current = current.ParentElement;
        }
    }

    #region DOM Event Handlers

    private Core.Dom.Element? _hoveredElement;

    private void HandleDomKeyDown(char charCode, Key key, bool repeat)
    {
        if (_currentLoad == null) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = KeyToJsKey(charCode, key);
        var codeStr = KeyToJsCode(key);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keydown", host)
        {
            key = keyStr,
            code = codeStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            repeat = repeat,
            keyCode = (int)key,
            which = (int)key,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private void HandleDomKeyUp(char charCode, Key key, bool repeat)
    {
        if (_currentLoad == null) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = KeyToJsKey(charCode, key);
        var codeStr = KeyToJsCode(key);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keyup", host)
        {
            key = keyStr,
            code = codeStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            repeat = repeat,
            keyCode = (int)key,
            which = (int)key,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private void HandleDomChar(char charCode)
    {
        if (_currentLoad == null || charCode == '\0' || charCode < 32) return;

        var target = _focusedElement ?? _currentLoad.Document.Body ?? _currentLoad.Document.DocumentElement;
        if (target == null) return;

        var keyStr = new string(charCode, 1);

        var host = _jsEngine.GetElementHost(target);
        var evt = new ScriptEvent("keypress", host)
        {
            key = keyStr,
            code = keyStr,
            ctrlKey = IsCtrlPressed(),
            shiftKey = IsShiftPressed(),
            altKey = IsAltPressed(),
            keyCode = charCode,
            which = charCode,
            bubbles = true,
            cancelable = true
        };
        _jsEngine.DispatchEvent(target, evt);
    }

    private bool HandleScrollContainerWheel(double deltaX, double deltaY, float mouseX, float mouseY)
    {
        if (_currentLoad == null) return false;
        // Find the element under the mouse in document coordinates (accounting for page scroll)
        float docX = mouseX + _scroll.ScrollX;
        float docY = mouseY - _contentOffset + _scroll.ScrollY;
        var element = HitTest(_currentLoad.Document, docX, docY);
        if (element == null) return false;

        // Walk up from the hit element to find a scroll container
        var el = element;
        while (el != null)
        {
            var box = el.LayoutBox;
            if (box != null && box.IsScrollContainer &&
                box.ContentBox.Height > 0 && box.ContentBox.Width > 0 &&
                (box.ScrollContentHeight > box.ContentBox.Height || box.ScrollContentWidth > box.ContentBox.Width))
            {
                // Check if mouse is within this scroll container's viewport
                float scrollY = box.ScrollY - (float)deltaY;
                float maxScrollY = box.ScrollContentHeight - box.ContentBox.Height;
                if (maxScrollY > 0)
                {
                    scrollY = Math.Clamp(scrollY, 0, maxScrollY);
                    if (Math.Abs(scrollY - box.ScrollY) > 0.5f)
                    {
                        box.ScrollY = scrollY;
                        _pendingRelayout = true;
                        return true;
                    }
                }
                float scrollX = box.ScrollX - (float)deltaX;
                float maxScrollX = box.ScrollContentWidth - box.ContentBox.Width;
                if (maxScrollX > 0)
                {
                    scrollX = Math.Clamp(scrollX, 0, maxScrollX);
                    if (Math.Abs(scrollX - box.ScrollX) > 0.5f)
                    {
                        box.ScrollX = scrollX;
                        _pendingRelayout = true;
                        return true;
                    }
                }
            }
            el = el.ParentElement;
        }
        return false;
    }

    private void HandleDomMouseMove(float x, float y)
    {
        if (_currentLoad == null) return;

        float docX = x + _scroll.ScrollX;
        float adjustedY = y - _contentOffset + _scroll.ScrollY;
        var element = HitTest(_currentLoad.Document, docX, adjustedY);

        // Update text selection during drag
        if (_isSelecting)
        {
            _selectionEnd = new SKPoint(x + _scroll.ScrollX, y + _scroll.ScrollY);
            _hasSelection = true;
            _input.NeedsRedraw = true;
        }

        // Track hovered element for mouseover/mouseout
        if (element != _hoveredElement)
        {
            if (_hoveredElement != null)
            {
                var outHost = _jsEngine.GetElementHost(_hoveredElement);
                var outEvt = new ScriptEvent("mouseout", outHost)
                {
                    clientX = x,
                    clientY = y,
                    relatedTarget = element != null ? _jsEngine.GetElementHost(element) : null,
                    bubbles = true,
                    cancelable = true
                };
                _jsEngine.DispatchEvent(_hoveredElement, outEvt);
            }
            if (element != null)
            {
                var overHost = _jsEngine.GetElementHost(element);
                var overEvt = new ScriptEvent("mouseover", overHost)
                {
                    clientX = x,
                    clientY = y,
                    relatedTarget = _hoveredElement != null ? _jsEngine.GetElementHost(_hoveredElement) : null,
                    bubbles = true,
                    cancelable = true
                };
                _jsEngine.DispatchEvent(element, overEvt);
            }
            // Update CSS :hover pseudo-class state
            // Clear hover on old element's ancestor chain
            var oldHover = _hoveredElement;
            var ptr = oldHover;
            while (ptr != null)
            {
                ptr.IsHovered = false;
                ptr = ptr.ParentElement;
            }
            // Set hover on new element's ancestor chain
            _hoveredElement = element;
            ptr = _hoveredElement;
            while (ptr != null)
            {
                ptr.IsHovered = true;
                ptr = ptr.ParentElement;
            }
            _pendingRelayout = true;
        }

        if (element != null)
        {
            var moveHost = _jsEngine.GetElementHost(element);
            var moveEvt = new ScriptEvent("mousemove", moveHost)
            {
                clientX = x,
                clientY = y,
                bubbles = true,
                cancelable = true
            };
            _jsEngine.DispatchEvent(element, moveEvt);
        }
    }

    private void HandleDomMouseUp(float x, float y)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            if (_hasSelection)
            {
                // Finalize selection
                _input.NeedsRedraw = true;
            }
        }
    }

    public string GetSelectedText()
    {
        if (!_hasSelection || _currentLoad == null) return "";

        float minX = Math.Min(_selectionStart.X, _selectionEnd.X);
        float minY = Math.Min(_selectionStart.Y, _selectionEnd.Y);
        float maxX = Math.Max(_selectionStart.X, _selectionEnd.X);
        float maxY = Math.Max(_selectionStart.Y, _selectionEnd.Y);
        var selRect = new SKRect(minX, minY, maxX, maxY);

        var sb = new System.Text.StringBuilder();
        CollectSelectedText(_currentLoad.Document.DocumentElement, selRect, sb);
        return sb.ToString();
    }

    private static void CollectSelectedText(Core.Dom.Element? element, SKRect selRect, System.Text.StringBuilder sb)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        if (box != null)
        {
            var contentRect = new SKRect(box.ContentBox.Left, box.ContentBox.Top,
                                          box.ContentBox.Right, box.ContentBox.Bottom);
            if (contentRect.IntersectsWith(selRect))
            {
                // Collect text from child text nodes
                foreach (var child in element.Children)
                {
                    if (child is Core.Dom.TextNode textNode && textNode.TextContent != null)
                    {
                        if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]))
                            sb.Append(' ');
                        sb.Append(textNode.TextContent);
                    }
                }
            }
        }
        foreach (var child in element.Children.OfType<Core.Dom.Element>())
        {
            CollectSelectedText(child, selRect, sb);
        }
    }

    private static string KeyToJsKey(char charCode, Key key)
    {
        if (charCode != '\0' && charCode >= 32) return new string(charCode, 1);
        return key switch
        {
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Left => "ArrowLeft",
            Key.Up => "ArrowUp",
            Key.Right => "ArrowRight",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Space => " ",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            >= Key.A and <= Key.Z => ((char)(int)key).ToString(),
            _ => "Unidentified"
        };
    }

    private static string KeyToJsCode(Key key)
    {
        return key switch
        {
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Backspace => "Backspace",
            Key.Delete => "Delete",
            Key.Left => "ArrowLeft",
            Key.Up => "ArrowUp",
            Key.Right => "ArrowRight",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Space => "Space",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            >= Key.A and <= Key.Z => "Key" + (char)(int)key,
            _ => ""
        };
    }

    private bool IsCtrlPressed() => _input.IsCtrlDown;
    private bool IsShiftPressed() => _input.IsShiftDown;
    private bool IsAltPressed() => _input.IsAltDown;

    private bool DevToolsHandleInput(char c, Key key, bool shift)
    {
        if (key == Key.Unknown && c == 1) return false;
        if (key == Key.Unknown && c == 3) return false;
        if (key == Key.Unknown && c == 22) return false;
        if (key == Key.Unknown && c == 24) return false;
        if (key == Key.Unknown && c == 26) return false;

        return _devTools.HandleKeyPress(c, key, shift);
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
            _jsEngine.DispatchEvent(_focusedElement, "input");
            _input.NeedsRedraw = true;
        }
        else if (_chrome.IsUrlBarFocused())
        {
            _chrome.HandleKeyPress(charCode, SKKey.None);
            _input.NeedsRedraw = true;
        }
    }

    private void PerformCopy()
    {
        var (mx, my) = _input.GetMousePosition();
        if (_devTools.Visible && _devTools.IsInputField(mx, my))
        {
            string sel = _devTools.GetActiveTabSelectedText();
            if (!string.IsNullOrEmpty(sel))
                Clipboard.SetText(sel);
        }
        else if (_chrome.IsUrlBarFocused())
        {
            string? sel = _chrome.UrlBarSelectedText;
            if (sel != null)
                Clipboard.SetText(sel);
            else
            {
                string url = _chrome.GetCurrentUrl() ?? "";
                if (!string.IsNullOrEmpty(url))
                    Clipboard.SetText(url);
            }
        }
        else
        {
            string sel = GetSelectedText();
            if (!string.IsNullOrEmpty(sel))
                Clipboard.SetText(sel);
        }
    }

    private void PerformPaste()
    {
        string? text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        if (_chrome.IsUrlBarFocused())
        {
            // Delete selection first if any
            if (_chrome.UrlBarSelectedText != null)
                _chrome.HandleKeyPress('\0', SKKey.Backspace);
            foreach (char c in text)
                HandleImeChar(c);
        }
        else
        {
            foreach (char c in text)
                HandleImeChar(c);
        }
    }

    private void PerformCut()
    {
        PerformCopy();
        if (_devTools.Visible)
        {
            if (_devTools.GetActiveTab() == 0)
                _devTools.HandleKeyPress('\0', Key.Backspace);
            else if (_devTools.GetActiveTab() == 2)
                _devTools.HandleKeyPress('\0', Key.Backspace);
        }
        else if (_chrome.IsUrlBarFocused() && _chrome.UrlBarSelectedText != null)
            _chrome.HandleKeyPress('\0', SKKey.Backspace);
    }

    private void PerformSelectAll()
    {
        if (_chrome.IsUrlBarFocused())
            _chrome.SelectAllInUrlBar();
        else if (_devTools.Visible)
            _devTools.SelectAllInActiveTab();
    }

    public void InjectImeChar(char c)
    {
        HandleImeChar(c);
    }

    private class PageInputImeHost : IImeSupport
    {
        private readonly BrowserApp _app;

        public PageInputImeHost(BrowserApp app) { _app = app; }

        public Point GetImeCaretPosition()
        {
            var el = _app._focusedElement;
            if (el?.LayoutBox == null)
                return new Point(0, _app._contentOffset);

            float caretScreenX = el.LayoutBox.BorderBox.Left + 40;
            float caretScreenY = el.LayoutBox.BorderBox.Top - _app._scroll.ScrollY + _app._contentOffset;
            return new Point(caretScreenX, caretScreenY);
        }

        public void OnImeCompositionStart() { }
        public void OnImeCompositionUpdate(string compositionString, int cursorPosition) { }

        public void OnImeCompositionEnd(string? resultString)
        {
            if (resultString != null && _app._focusedElement != null && _app._focusedElement.IsFormElement)
            {
                _app._jsEngine.DispatchEvent(_app._focusedElement, "input");
            }
        }
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
        ShutdownPerformanceHub();
        _processManager.Dispose();
        if (_currentLoad != null)
        {
            _currentLoad.AngleSharpDoc?.Dispose();
        }
        _chrome.Dispose();
        _skiaRenderer.Dispose();
        _window.Dispose();
        _jsEngine.Dispose();
        _eventLoop.Stop();
        if (_renderingSettings != null)
            _skiaRenderer.Settings = null;
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Performance integration

    private void InitializePerformanceHub()
    {
        if (_perfHub is not null) return;

        _perfHub = PerformanceHub.Shared;
        _sharedStyleCache = _perfHub.StyleCache;
        _layoutCache = _perfHub.LayoutCache;

        // Wrap the regular LayoutEngine with the incremental engine so clean
        // subtrees can be skipped on subsequent passes.
        _incrementalLayout = new IncrementalLayoutEngine(_layout, _layoutCache);

        // 2GB soft budget for the process; can be tuned via config later.
        var memoryBudget = new MemoryBudget(2L * 1024 * 1024 * 1024);
        _perfHub.Initialize(memoryBudget);

        // Wire the long-task observer so we record any operation that exceeds
        // 50 ms in the central metrics. The hub will automatically push the
        // long-task duration into Total Blocking Time.
        _perfHub.LongTasks.OnLongTask += entry =>
        {
            _perfHub?.Registry.Feed.Append("longtask",
                $"{entry.Name} {entry.DurationNanos / 1_000_000.0:F1}ms");
        };

        // Connect the resource cache used by the rendering layer to memory pressure.
        // When the monitor reports a High/Critical level, the aggregate responder
        // shrinks caches to free pages.
        _perfHub.Registry.MemoryPressure.Register(_perfHub.AggregateResponder);

        // Make the image cache's decoded pool obey memory pressure
        if (_sharedImageCache is { } img)
        {
            _perfHub.Registry.MemoryPressure.Register(new ImagePoolPressureAdapter(img.DecodedPool));
        }

        // Route the tile compositor through the performance hub so its cache
        // uses the shared tile manager, the predictive scheduler pre-rasterises
        // tiles in the direction of scroll, and the memory budget caps the
        // tile byte total. The compositor is rebuilt inside AttachPerformanceHub
        // so it picks up these references on the next frame.
        try
        {
            _skiaRenderer.AttachPerformanceHub(_perfHub, memoryBudget);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PerfHub] Compositor attach failed: {ex.Message}");
        }

        Console.WriteLine("[PerfHub] Initialized. Style cache, layout cache, scheduler, long-task observer, tile compositor active.");
    }

    private void ShutdownPerformanceHub()
    {
        if (_perfHub is null) return;
        _perfHub.Shutdown();
        _perfHub = null;
    }

    /// <summary>
    /// Runs at the start of each render frame: drives the cooperative scheduler
    /// through one budget slice, observes the time spent in the main pipeline
    /// phases, and reports memory usage to the pressure monitor.
    /// </summary>
    private void RunPerfFrame(double dtMillis)
    {
        if (_perfHub is null) return;

        // Choose a frame budget based on the current target FPS (capped at 60 Hz
        // for the C#/Skia renderer). When the page is "behind" (dt > target),
        // use a larger catch-up budget to drain pending tasks.
        var budget = dtMillis > 50
            ? CooperativeScheduler.FrameBudget.CatchUp
            : CooperativeScheduler.FrameBudget.For60Fps;
        _perfHub.RunFrame(budget);

        // Coarse memory accounting: bytes used by managed heap is not directly
        // observable, but we can poke the GC heap and feed it to the pressure
        // monitor. This is a hint — the real policy is in MemoryPressureMonitor.
        long managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        _perfHub.Registry.MemoryPressure.ReportUsage(managedBytes);
    }

    /// <summary>
    /// Convenience used by JS engine / input handlers to mark a subtree as
    /// needing relayout. Replaces the previous boolean flag with a per-element
    /// dirty bit that the incremental layout engine can use to skip work.
    /// </summary>
    private void MarkSubtreeDirty(UpBrowser.Core.Dom.Element root, DirtyFlags flags = DirtyFlags.AllLayout)
    {
        if (root is null) return;
        DirtyState.AddSelf(root, flags);
        foreach (var child in root.Children)
        {
            if (child is UpBrowser.Core.Dom.Element ce)
            {
                DirtyState.AddChildren(ce, flags);
                MarkSubtreeDirty(ce, flags);
            }
        }
    }

    private static int CountDomNodes(Core.Dom.Node node)
    {
        int count = node is Core.Dom.Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDomNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Core.Dom.Node node)
    {
        int count = (node is Core.Dom.Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }

    /// <summary>
    /// Public escape hatch: a debugging snapshot of the current performance
    /// state. Exposed so the dev tools panel can show a single JSON view of
    /// style/layout/paint timings, long tasks, and memory pressure level.
    /// </summary>
    public string GetPerformanceSnapshot() => _perfHub?.Api.Snapshot() ?? "{}";

    #endregion
}

/// <summary>
/// Memory responder that shrinks the decoded-image pool in proportion to the
/// reported pressure level. Wired in <see cref="BrowserApp.InitializePerformanceHub"/>.
/// </summary>
internal sealed class ImagePoolPressureAdapter : UpBrowser.Core.Performance.Memory.MemoryResponder
{
    private readonly UpBrowser.Core.Performance.Resources.DecodedImagePool _pool;
    private long _originalCapacity;

    public ImagePoolPressureAdapter(UpBrowser.Core.Performance.Resources.DecodedImagePool pool)
    {
        _pool = pool;
        _originalCapacity = pool.CapacityBytes;
    }

    public override string Name => "image-pool";

    public override void OnMemoryPressure(UpBrowser.Core.Performance.Memory.MemoryPressureLevel level)
    {
        long factor = level switch
        {
            UpBrowser.Core.Performance.Memory.MemoryPressureLevel.Critical => 4,
            UpBrowser.Core.Performance.Memory.MemoryPressureLevel.High => 2,
            UpBrowser.Core.Performance.Memory.MemoryPressureLevel.Moderate => 1,
            _ => 0,
        };
        if (factor == 0) return;
        long target = Math.Max(1L * 1024 * 1024, _originalCapacity / factor);
        _pool.SetCapacity(target);
    }

    public override void OnMemoryRelease(UpBrowser.Core.Performance.Memory.MemoryPressureLevel level)
    {
        if (level <= UpBrowser.Core.Performance.Memory.MemoryPressureLevel.Moderate)
        {
            _pool.SetCapacity(_originalCapacity);
        }
    }
}