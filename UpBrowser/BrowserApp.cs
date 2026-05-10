using UpBrowser.Platform;
using UpBrowser.Rendering;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.EventLoop;
using SkiaSharp;

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
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private DocumentManager.DocumentLoadResult? _currentLoad;
    private PaintVisitor? _cachedPaintVisitor;
    private DisplayList _displayList = new();
    private float _lastLayoutWidth;
    private string _currentHtml = "";

    private DateTime _lastRenderTime = DateTime.Now;
    private readonly TimeSpan _targetFrameTime = TimeSpan.FromSeconds(1.0 / 60.0);
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private float _lastScrollX;
    private float _lastScrollY;

    public BrowserApp(int logicalWidth, int logicalHeight)
    {
        _dpiScale = PlatformFactory.GetDpiScale();
        Console.WriteLine($"DPI Scale: {_dpiScale:F2} ({_dpiScale * 100}%)");

        int physicalWidth = (int)(logicalWidth * _dpiScale);
        int physicalHeight = (int)(logicalHeight * _dpiScale);

        _window = PlatformFactory.CreateWindow(physicalWidth, physicalHeight, "UpBrowser");
        _docManager = new DocumentManager();
        _chrome = new ChromeRenderer();
        _scroll = new ScrollManager();
        _skiaRenderer = new SkiaRenderer();
        _jsEngine = new JavaScriptEngine();
        _eventLoop = new EventLoop();
        _input = new InputHandler(_chrome, _scroll, _window, _dpiScale);

        _contentOffset = _chrome.GetContentOffset();

        Initialize();
    }

    private void Initialize()
    {
        _chrome.Initialize();

        _skiaRenderer.Initialize(1024, 768, enableDirtyRegions: true);
        _skiaRenderer.DpiScale = _dpiScale;

        _jsEngine.Execute(@"
            console.log('UpBrowser JavaScript engine initialized!');
            document.title = 'UpBrowser - Running';
        ");

        _eventLoop.Start();

        _input.WireEvents();
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

        BuildDisplayList(ww, wh);
        _scroll.ScrollTo(0, 0);
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

        _cachedPaintVisitor = new PaintVisitor(_contentOffset);
        _cachedPaintVisitor.VisitDocument(_currentLoad.Document);
        _displayList = _cachedPaintVisitor.GetDisplayList();
        _displayList.SortByZIndex();
    }

    private void RenderFrame(double dt)
    {
        _eventLoop.ProcessTasks();

        bool cursorChanged = _chrome.UpdateCursorBlink();

        var (pw, ph) = _window.GetClientSize();
        int windowWidth = (int)(pw / _dpiScale);
        int windowHeight = (int)(ph / _dpiScale);

        bool sizeChanged = windowWidth != _lastWindowWidth || windowHeight != _lastWindowHeight;
        bool scrollChanged = Math.Abs(_scroll.ScrollX - _lastScrollX) > 0.5f ||
                             Math.Abs(_scroll.ScrollY - _lastScrollY) > 0.5f;
        bool chromeNeedsRedraw = cursorChanged && _chrome.IsUrlBarFocused();
        bool needsRedraw = _input.NeedsRedraw;

        if (!sizeChanged && !scrollChanged && !chromeNeedsRedraw && !needsRedraw)
            return;

        var now = DateTime.Now;
        if ((now - _lastRenderTime) < _targetFrameTime && !sizeChanged && !needsRedraw)
            return;
        _lastRenderTime = now;

        if (windowWidth <= 0 || windowHeight <= 0 || _currentLoad == null)
            return;

        if (sizeChanged)
        {
            _skiaRenderer.Resize(windowWidth, windowHeight);
            _lastWindowWidth = windowWidth;
            _lastWindowHeight = windowHeight;
        }

        if (sizeChanged || windowWidth != _lastLayoutWidth)
        {
            _lastLayoutWidth = windowWidth;

            BuildDisplayList(windowWidth, windowHeight);

            var bodyBox = _currentLoad.Document.Body?.LayoutBox;
            float contentWidth = bodyBox?.BorderBox.Width ?? windowWidth;
            float contentHeight = bodyBox?.BorderBox.Height ?? 0;
            float viewportH = windowHeight - _contentOffset - _chrome.GetStatusBarHeight();

            _scroll.UpdateScroll(contentWidth, contentHeight, windowWidth, viewportH);
        }

        _lastScrollX = _scroll.ScrollX;
        _lastScrollY = _scroll.ScrollY;

        _skiaRenderer.Canvas.Clear(SKColors.White);

        var title = _currentLoad.AngleSharpDoc.Title ?? "UpBrowser";
        var currentUrl = _chrome.GetCurrentUrl();
        if (string.IsNullOrEmpty(currentUrl))
            currentUrl = "upbrowser://local";

        _chrome.RenderChrome(_skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl, title);

        float contentViewportHeight = windowHeight - _contentOffset - _chrome.GetStatusBarHeight();
        _skiaRenderer.RenderWithScroll(_displayList, _contentOffset,
            _scroll.ScrollX, _scroll.ScrollY,
            windowWidth, contentViewportHeight);

        _chrome.RenderScrollbars(_skiaRenderer.Canvas, windowWidth, windowHeight, _scroll);

        var pixels = _skiaRenderer.GetPixelData();
        _window.Render(pixels, _skiaRenderer.PhysicalWidth, _skiaRenderer.PhysicalHeight);

        _input.NeedsRedraw = false;
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
