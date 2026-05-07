using UpBrowser.Platform;
using UpBrowser.Rendering;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Css;
using UpBrowser.Core.Layout;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.EventLoop;
using AngleSharp;
using SkiaSharp;
using System.IO;

namespace UpBrowser;

class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetProcessDpiAwarenessContext(IntPtr dpiAwarenessContext);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    private static Document? _currentDoc;
    private static AngleSharp.Dom.IDocument? _currentAngleSharpDoc;
    private static PaintVisitor? _cachedPaintVisitor;
    private static DisplayList _displayList = new();
    private static LayoutEngine _layoutEngine = new();
    private static float _lastLayoutWidth;
    private static float _lastContentHeight;
    private static string _currentHtml = "";

    static float GetDpiScale()
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            SetProcessDPIAware();
        }

        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, hdc);
            return dpiX / 96.0f;
        }
        return 1.0f;
    }

    static string GetDefaultHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <title>UpBrowser</title>
</head>
<body style='background: #f5f5f5; margin: 0; padding: 20px; font-family: Arial, sans-serif;'>
    <h1 style='color: #333; font-size: 32px; margin: 0 0 20px 0;'>Hello World</h1>
    <p style='color: #666; font-size: 16px; line-height: 1.5;'>This is a test paragraph with some text content.</p>
    <div style='background: #ffeb3b; padding: 20px; border: 2px solid #f44336; margin: 20px 0; border-radius: 8px;'>
        <h2 style='color: #333; margin: 0 0 10px 0;'>Box Model Test</h2>
        <p style='color: #555;'>This div has margin, border, padding, and content.</p>
    </div>
    <ul style='color: #333;'>
        <li>List item 1</li>
        <li>List item 2</li>
        <li>List item 3</li>
    </ul>
    <button style='background: #2196F3; color: white; padding: 10px 20px; border: none; border-radius: 4px; font-size: 14px;'>Click Me</button>
    <div style='margin-top: 20px; padding: 15px; background: white; border: 1px solid #ddd;'>
        <span style='color: red;'>Red text</span> and <span style='color: blue;'>blue text</span> in same line.
    </div>
    <div style='display: flex; gap: 10px; margin-top: 20px;'>
        <div style='background: #e3f2fd; padding: 15px; flex: 1;'>Flex Item 1</div>
        <div style='background: #f3e5f5; padding: 15px; flex: 1;'>Flex Item 2</div>
        <div style='background: #e8f5e9; padding: 15px; flex: 1;'>Flex Item 3</div>
    </div>
    <div style='position: relative; height: 100px; margin-top: 20px; background: #fff3e0;'>
        <div style='position: absolute; top: 10px; right: 10px; background: #ff5722; color: white; padding: 5px 10px;'>Absolute Position</div>
    </div>
    <div style='margin-top: 30px;'>
        <p>More content below - Testing scroll functionality</p>
        <p>Line 2</p>
        <p>Line 3</p>
        <p>Line 4</p>
        <p>Line 5</p>
        <p>Line 6</p>
        <p>Line 7</p>
        <p>Line 8</p>
        <p>Line 9</p>
        <p>Line 10</p>
        <p style='color: red;'>This is the bottom of the page!</p>
    </div>
    <style>
h1 {
color: red;
text-align: center;
}
p {
color: green;
font-size: 16px;
}
</style>
</body>
</html>";
    }

    static async Task<(Document doc, AngleSharp.Dom.IDocument angleSharpDoc)> LoadHtml(string html)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var angleSharpDoc = await context.OpenAsync(req => req.Content(html));

        var doc = new Document
        {
            Url = "upbrowser://local",
            Title = angleSharpDoc.Title ?? "Untitled"
        };

        ConvertHtmlToDom(angleSharpDoc.DocumentElement!, doc);

        var cssParser = new CssParser();
        var styleComputer = new StyleComputer();

        var uaStylesheet = cssParser.Parse(GetUserAgentStyles());
        styleComputer.AddStylesheet(uaStylesheet);

        await LoadStylesFromHtml(angleSharpDoc, cssParser, styleComputer);

        styleComputer.ComputeStyles(doc);

        return (doc, angleSharpDoc);
    }

    static void BuildDisplayList(Document doc, float contentOffset, float windowWidth, float windowHeight)
    {
        _layoutEngine.Layout(doc, windowWidth, windowHeight);

        _cachedPaintVisitor = new PaintVisitor(contentOffset);
        _cachedPaintVisitor.VisitDocument(doc);
        _displayList = _cachedPaintVisitor.GetDisplayList();
        _displayList.SortByZIndex();
    }

    static async Task Main(string[] args)
    {
        float dpiScale = GetDpiScale();
        Console.WriteLine($"DPI Scale: {dpiScale:F2} ({dpiScale * 100}%)");
        Console.WriteLine("UpBrowser - Starting...");

        _currentHtml = GetDefaultHtml();

        var chromeRenderer = new ChromeRenderer();
        chromeRenderer.Initialize();

        var (doc, angleSharpDoc) = await LoadHtml(_currentHtml);
        _currentDoc = doc;
        _currentAngleSharpDoc = angleSharpDoc;

        var devTool = new LayoutDevTool();
        var debugReport = devTool.GenerateReport(doc, 1024, 768);
        File.WriteAllText("layout_debug.txt", debugReport);
        Console.WriteLine("Debug report saved to layout_debug.txt");
        Console.WriteLine(devTool.GenerateQuickReport(doc));

        using var jsEngine = new JavaScriptEngine();
        jsEngine.Execute(@"
            console.log('UpBrowser JavaScript engine initialized!');
            document.title = 'UpBrowser - Running';
        ");

        var eventLoop = new EventLoop();
        eventLoop.Start();

        int logicalWidth = 1024;
        int logicalHeight = 768;
        int physicalWidth = (int)(logicalWidth * dpiScale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        var window = BrowserWindow.Create(physicalWidth, physicalHeight, "UpBrowser");

        var skiaRenderer = new SkiaRenderer();
        skiaRenderer.Initialize(logicalWidth, logicalHeight, enableDirtyRegions: true);
        skiaRenderer.DpiScale = dpiScale;

        var contentOffset = chromeRenderer.GetContentOffset();
        var scrollManager = new ScrollManager();

        BuildDisplayList(doc, contentOffset, logicalWidth, logicalHeight);

        _lastLayoutWidth = logicalWidth;
        var bodyBox = doc.Body?.LayoutBox;
        _lastContentHeight = bodyBox?.BorderBox.Height ?? 0;

        // ========== 渲染状态跟踪 ==========
        var targetFrameTime = TimeSpan.FromSeconds(1.0 / 60.0);
        var lastRenderTime = DateTime.Now;
        bool needsRedraw = true;
        int lastWindowWidth = 0;
        int lastWindowHeight = 0;
        float lastScrollX = 0;
        float lastScrollY = 0;

        // ========== Chrome 导航回调 ==========
        chromeRenderer.OnNavigate = async (url) =>
        {
            Console.WriteLine($"Navigating to: {url}");
            needsRedraw = true;

            if (url.StartsWith("upbrowser://"))
            {
                if (url == "upbrowser://newtab" || url == "upbrowser://local")
                {
                    _currentHtml = GetDefaultHtml();
                    var (newDoc, newAngleDoc) = await LoadHtml(_currentHtml);
                    _currentDoc = newDoc;
                    _currentAngleSharpDoc = newAngleDoc;

                    var (pw, ph) = window.GetClientSize();
                    int ww = (int)(pw / dpiScale);
                    int wh = (int)(ph / dpiScale);

                    BuildDisplayList(newDoc, contentOffset, ww, wh);
                    scrollManager.ScrollTo(0, 0);
                }
            }
            else if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    var webHtml = await httpClient.GetStringAsync(url);
                    _currentHtml = webHtml;

                    var (newDoc, newAngleDoc) = await LoadHtml(webHtml);
                    _currentDoc = newDoc;
                    _currentAngleSharpDoc = newAngleDoc;

                    var (pw, ph) = window.GetClientSize();
                    int ww = (int)(pw / dpiScale);
                    int wh = (int)(ph / dpiScale);

                    BuildDisplayList(newDoc, contentOffset, ww, wh);
                    scrollManager.ScrollTo(0, 0);

                    Console.WriteLine($"Loaded: {newAngleDoc.Title}");
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

                    _currentHtml = errorHtml;
                    var (errorDoc, errorAngleDoc) = await LoadHtml(errorHtml);
                    _currentDoc = errorDoc;
                    _currentAngleSharpDoc = errorAngleDoc;

                    var (pw, ph) = window.GetClientSize();
                    int ww = (int)(pw / dpiScale);
                    int wh = (int)(ph / dpiScale);

                    BuildDisplayList(errorDoc, contentOffset, ww, wh);
                    scrollManager.ScrollTo(0, 0);
                }
            }
            else
            {
                var searchHtml = $@"<!DOCTYPE html>
<html><head><title>Search: {url}</title></head>
<body style='font-family: Arial; padding: 40px;'>
    <h1 style='color: #1a73e8;'>Search</h1>
    <p>Searching for: <strong>{url}</strong></p>
    <p style='color: #666;'>Search functionality requires network access.</p>
</body></html>";

                _currentHtml = searchHtml;
                var (searchDoc, searchAngleDoc) = await LoadHtml(searchHtml);
                _currentDoc = searchDoc;
                _currentAngleSharpDoc = searchAngleDoc;

                var (pw, ph) = window.GetClientSize();
                int ww = (int)(pw / dpiScale);
                int wh = (int)(ph / dpiScale);

                BuildDisplayList(searchDoc, contentOffset, ww, wh);
                scrollManager.ScrollTo(0, 0);
            }
        };

        chromeRenderer.OnRefresh = () =>
        {
            Console.WriteLine("Refreshing page...");
            needsRedraw = true;
            if (!string.IsNullOrEmpty(_currentHtml))
            {
                Task.Run(async () =>
                {
                    var (newDoc, newAngleDoc) = await LoadHtml(_currentHtml);
                    _currentDoc = newDoc;
                    _currentAngleSharpDoc = newAngleDoc;

                    var (pw, ph) = window.GetClientSize();
                    int ww = (int)(pw / dpiScale);
                    int wh = (int)(ph / dpiScale);

                    BuildDisplayList(newDoc, contentOffset, ww, wh);
                    needsRedraw = true;
                });
            }
        };

        chromeRenderer.OnHome = () =>
        {
            Console.WriteLine("Going home...");
            chromeRenderer.NavigateToUrl("upbrowser://local");
        };

        // ========== 窗口事件处理 ==========
        window.OnMouseMove = (x, y) =>
        {
            chromeRenderer.HandleMouseMove(x / dpiScale, y / dpiScale);
        };

        window.OnMouseClick = (x, y, isDown) =>
        {
            if (!isDown) return;

            float logicalX = x / dpiScale;
            float logicalY = y / dpiScale;

            bool handled = chromeRenderer.HandleMouseClick(logicalX, logicalY);
            needsRedraw = true;

            if (!handled)
            {
                float statusBarHeight = chromeRenderer.GetStatusBarHeight();
                float viewportHeight = window.Height / dpiScale - contentOffset - statusBarHeight;

                if (scrollManager.CanScrollY)
                {
                    float scrollbarLeft = window.Width / dpiScale - ScrollManager.ScrollbarWidth;
                    if (logicalX >= scrollbarLeft &&
                        logicalY >= contentOffset &&
                        logicalY <= contentOffset + viewportHeight)
                    {
                        float trackHeight = viewportHeight;
                        float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                            trackHeight * viewportHeight / scrollManager.ContentHeight);
                        float maxScrollY = scrollManager.ContentHeight - viewportHeight;
                        float thumbTop = maxScrollY > 0 ?
                            (scrollManager.ScrollY / maxScrollY) * (trackHeight - thumbHeight) : 0;

                        if (logicalY < contentOffset + thumbTop)
                            scrollManager.PageUp();
                        else if (logicalY > contentOffset + thumbTop + thumbHeight)
                            scrollManager.PageDown();
                    }
                }
            }
        };

        window.OnKeyDownWithChar = (charCode, key) =>
        {
            // 如果是有字符输入（来自 WM_CHAR）
            if (key == Key.Unknown && charCode != '\0')
            {
                // 这是字符输入，直接传给 Chrome（skKey 为 None）
                chromeRenderer.HandleKeyPress(charCode, SKKey.None);
                needsRedraw = true;
                return true;
            }

            // 这是非字符按键（来自 WM_KEYDOWN），转换为 SKKey
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

            bool handledByChrome = chromeRenderer.HandleKeyPress(charCode, chromeKey);

            // 立即标记需要重绘
            needsRedraw = true;

            if (handledByChrome)
                return true;

            if (chromeRenderer.IsUrlBarFocused())
                return false;

            // 滚动快捷键
            switch (key)
            {
                case Key.PageUp:
                    scrollManager.PageUp();
                    return true;
                case Key.PageDown:
                    scrollManager.PageDown();
                    return true;
                case Key.Home:
                    scrollManager.ScrollHome();
                    return true;
                case Key.End:
                    scrollManager.ScrollEnd();
                    return true;
                case Key.Up:
                    scrollManager.ScrollBy(0, -40);
                    return true;
                case Key.Down:
                    scrollManager.ScrollBy(0, 40);
                    return true;
                case Key.Left:
                    scrollManager.ScrollBy(-40, 0);
                    return true;
                case Key.Right:
                    scrollManager.ScrollBy(40, 0);
                    return true;
                default:
                    return false;
            }
        };
        window.OnKeyDown = (key) =>
        {
            if (!chromeRenderer.IsUrlBarFocused())
            {
                if (key == Key.F5)
                {
                    chromeRenderer.OnRefresh?.Invoke();
                }
            }
        };

        window.OnMouseWheel = (delta) =>
        {
            if (!chromeRenderer.IsUrlBarFocused())
            {
                scrollManager.ScrollBy((float)delta);
                needsRedraw = true;
            }
        };

        window.OnScrollbarDrag = (deltaX, deltaY) =>
        {
            if (deltaY != 0)
            {
                scrollManager.ScrollBy(0, deltaY * 3.0f);
                needsRedraw = true;
            }
            if (deltaX != 0)
            {
                scrollManager.ScrollBy(deltaX * 3.0f, 0);
                needsRedraw = true;
            }
        };

        // ========== 主渲染循环 ==========
        window.Run((dt) =>
        {
            eventLoop.ProcessTasks();

            bool cursorChanged = chromeRenderer.UpdateCursorBlink();

            var (pw, ph) = window.GetClientSize();
            int windowWidth = (int)(pw / dpiScale);
            int windowHeight = (int)(ph / dpiScale);

            bool sizeChanged = windowWidth != lastWindowWidth || windowHeight != lastWindowHeight;
            bool scrollChanged = Math.Abs(scrollManager.ScrollX - lastScrollX) > 0.5f ||
                                 Math.Abs(scrollManager.ScrollY - lastScrollY) > 0.5f;
            bool chromeNeedsRedraw = cursorChanged && chromeRenderer.IsUrlBarFocused();

            if (!sizeChanged && !scrollChanged && !chromeNeedsRedraw && !needsRedraw)
                return;

            var now = DateTime.Now;
            if ((now - lastRenderTime) < targetFrameTime && !sizeChanged && !needsRedraw)
                return;
            lastRenderTime = now;

            if (windowWidth <= 0 || windowHeight <= 0 || _currentDoc == null)
                return;

            if (sizeChanged)
            {
                skiaRenderer.Resize(windowWidth, windowHeight);
                lastWindowWidth = windowWidth;
                lastWindowHeight = windowHeight;
            }

            if (sizeChanged || windowWidth != _lastLayoutWidth)
            {
                _lastLayoutWidth = windowWidth;

                BuildDisplayList(_currentDoc, contentOffset, windowWidth, windowHeight);

                var bodyBox = _currentDoc.Body?.LayoutBox;
                float contentWidth = bodyBox?.BorderBox.Width ?? windowWidth;
                float contentHeight = bodyBox?.BorderBox.Height ?? 0;
                float viewportH = windowHeight - contentOffset - chromeRenderer.GetStatusBarHeight();

                scrollManager.UpdateScroll(contentWidth, contentHeight, windowWidth, viewportH);
                _lastContentHeight = contentHeight;
            }

            lastScrollX = scrollManager.ScrollX;
            lastScrollY = scrollManager.ScrollY;

            skiaRenderer.Canvas.Clear(SKColors.White);

            var title = _currentAngleSharpDoc?.Title ?? "UpBrowser";
            var currentUrl = chromeRenderer.GetCurrentUrl();
            if (string.IsNullOrEmpty(currentUrl))
                currentUrl = "upbrowser://local";

            chromeRenderer.RenderChrome(skiaRenderer.Canvas, windowWidth, windowHeight, currentUrl, title);

            float contentViewportHeight = windowHeight - contentOffset - chromeRenderer.GetStatusBarHeight();
            skiaRenderer.RenderWithScroll(_displayList, contentOffset,
                scrollManager.ScrollX, scrollManager.ScrollY,
                windowWidth, contentViewportHeight);

            chromeRenderer.RenderScrollbars(skiaRenderer.Canvas, windowWidth, windowHeight, scrollManager);

            var pixels = skiaRenderer.GetPixelData();
            window.Render(pixels, skiaRenderer.PhysicalWidth, skiaRenderer.PhysicalHeight);

            needsRedraw = false;
        });

        Console.WriteLine("UpBrowser closed.");
    }

    static void ConvertHtmlToDom(AngleSharp.Dom.IElement source, Document target)
    {
        foreach (var child in source.ChildNodes)
        {
            if (child is AngleSharp.Dom.IElement childElement)
            {
                var element = new HtmlElement(childElement.LocalName);

                foreach (var attr in childElement.Attributes)
                    element.Attributes[attr.Name] = attr.Value;

                if (childElement.HasAttribute("style"))
                {
                    var styleParser = new CssParser();
                    var props = styleParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                    foreach (var prop in props)
                        element.Style[prop.Key] = prop.Value;
                }

                if (childElement.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase))
                {
                    target.Body = element;
                    target.DocumentElement ??= element;
                    ConvertElementChildren(childElement, element);
                }
                else if (childElement.LocalName.Equals("head", StringComparison.OrdinalIgnoreCase))
                {
                    target.Head = element;
                    ConvertElementChildren(childElement, element);
                }
                else if (childElement.LocalName.Equals("title", StringComparison.OrdinalIgnoreCase))
                {
                    target.Title = childElement.TextContent ?? "";
                }
                else
                {
                    target.DocumentElement ??= element;
                    target.AppendChild(element);
                    ConvertElementChildren(childElement, element);
                }
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                var text = child.TextContent?.Trim();
                if (!string.IsNullOrEmpty(text))
                    target.AppendChild(new TextNode(text));
            }
        }
    }

    static void ConvertElementChildren(AngleSharp.Dom.INode source, Element target)
    {
        foreach (var child in source.ChildNodes)
        {
            if (child is AngleSharp.Dom.IElement childElement)
            {
                var element = new HtmlElement(childElement.LocalName);

                foreach (var attr in childElement.Attributes)
                    element.Attributes[attr.Name] = attr.Value;

                if (childElement.HasAttribute("style"))
                {
                    var styleParser = new CssParser();
                    var props = styleParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                    foreach (var prop in props)
                        element.Style[prop.Key] = prop.Value;
                }

                target.AppendChild(element);
                ConvertElementChildren(childElement, element);
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                var text = child.TextContent?.Trim();
                if (!string.IsNullOrEmpty(text))
                    target.AppendChild(new TextNode(text));
            }
        }
    }

    static string GetUserAgentStyles()
    {
        return @"
            body { font-family: Arial, sans-serif; display: block; margin: 8px; }
            h1 { display: block; margin: 0.67em 0; font-size: 2em; font-weight: bold; }
            h2 { display: block; margin: 0.83em 0; font-size: 1.5em; font-weight: bold; }
            h3 { display: block; margin: 1em 0; font-size: 1.17em; font-weight: bold; }
            h4 { display: block; margin: 1.33em 0; font-size: 1em; font-weight: bold; }
            h5 { display: block; margin: 1.67em 0; font-size: 0.83em; font-weight: bold; }
            h6 { display: block; margin: 2.33em 0; font-size: 0.67em; font-weight: bold; }
            p { display: block; margin: 1em 0; }
            div { display: block; }
            span { display: inline; }
            ul { display: block; margin: 1em 0; padding-left: 40px; list-style-type: disc; }
            ol { display: block; margin: 1em 0; padding-left: 40px; list-style-type: decimal; }
            li { display: list-item; }
            table { display: table; border-collapse: separate; border-spacing: 2px; }
            thead { display: table-header-group; vertical-align: middle; }
            tbody { display: table-row-group; vertical-align: middle; }
            tfoot { display: table-footer-group; vertical-align: middle; }
            tr { display: table-row; vertical-align: middle; }
            td { display: table-cell; vertical-align: inherit; padding: 1px; }
            th { display: table-cell; vertical-align: inherit; font-weight: bold; padding: 1px; }
            button { display: inline-block; cursor: pointer; padding: 2px 6px; }
            input { display: inline-block; }
            a { display: inline; color: #0000EE; text-decoration: underline; }
            strong { font-weight: bold; }
            em { font-style: italic; }
            u { text-decoration: underline; }
            s { text-decoration: line-through; }
            hr { display: block; margin: 0.5em auto; border: none; border-top: 1px solid #ccc; }
            img { display: inline-block; }
            br { display: none; }
            blockquote { display: block; margin: 1em 40px; }
            pre { display: block; font-family: monospace; white-space: pre; margin: 1em 0; }
            code { font-family: monospace; }
        ";
    }

    static async Task LoadStylesFromHtml(AngleSharp.Dom.IDocument angleSharpDoc, CssParser cssParser, StyleComputer styleComputer)
    {
        var baseUrl = angleSharpDoc.Url ?? "";
        var elements = angleSharpDoc.All;

        foreach (var element in elements)
        {
            var tagName = element.LocalName?.ToLowerInvariant();

            if (tagName == "style")
            {
                var cssText = element.TextContent;
                if (!string.IsNullOrEmpty(cssText))
                {
                    Console.WriteLine("Loading internal stylesheet...");
                    var stylesheet = cssParser.Parse(cssText);
                    styleComputer.AddStylesheet(stylesheet);
                }
            }
            else if (tagName == "link")
            {
                var rel = element.GetAttribute("rel");
                if (rel != null && rel.ToLowerInvariant().Contains("stylesheet"))
                {
                    var href = element.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        Console.WriteLine($"Loading external stylesheet: {href}");
                        try
                        {
                            var externalCss = await LoadExternalResource(href, baseUrl);
                            if (!string.IsNullOrEmpty(externalCss))
                            {
                                var stylesheet = cssParser.Parse(externalCss);
                                styleComputer.AddStylesheet(stylesheet);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load stylesheet {href}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }

    static async Task<string> LoadExternalResource(string url, string baseUrl)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
                    uri = new Uri(baseUri, url);
            }

            if (uri == null) return string.Empty;

            if (uri.IsFile || uri.Scheme == Uri.UriSchemeFile)
            {
                var path = uri.LocalPath;
                if (File.Exists(path))
                    return await File.ReadAllTextAsync(path);
            }
            else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                return await httpClient.GetStringAsync(uri);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading external resource: {ex.Message}");
        }

        return string.Empty;
    }
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }
}