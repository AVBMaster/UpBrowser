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

    static float GetDpiScale()
    {
        try
        {
            // Try modern DPI awareness first
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Fall back to old API
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

    static async Task Main(string[] args)
    {
        float dpiScale = GetDpiScale();
        Console.WriteLine($"DPI Scale: {dpiScale:F2} ({dpiScale * 100}%)");

        Console.WriteLine("UpBrowser - Starting...");

        var html = @"<!DOCTYPE html>
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
        <p style='color: red;' fontsize=>This is the bottom of the page!</p>
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

        var chromeRenderer = new ChromeRenderer();
        chromeRenderer.Initialize();

        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var angleSharpDoc = await context.OpenAsync(req => req.Content(html));

        var doc = new Document
        {
            Url = "https://example.com",
            Title = angleSharpDoc.Title ?? "Untitled"
        };

        ConvertHtmlToDom(angleSharpDoc.DocumentElement!, doc);

        var cssParser = new CssParser();
        var styleComputer = new StyleComputer();
        
        var uaStylesheet = cssParser.Parse(GetUserAgentStyles());
        styleComputer.AddStylesheet(uaStylesheet);
        
        await LoadStylesFromHtml(angleSharpDoc, cssParser, styleComputer);
        
        styleComputer.ComputeStyles(doc);

        var layoutEngine = new LayoutEngine();
        layoutEngine.Layout(doc, 1024, 768);

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

        // 根据DPI缩放调整窗口大小，让用户看到合适的大小
        int physicalWidth = (int)(logicalWidth * dpiScale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        var window = BrowserWindow.Create(physicalWidth, physicalHeight, "UpBrowser");

        var skiaRenderer = new SkiaRenderer();
        skiaRenderer.Initialize(logicalWidth, logicalHeight, enableDirtyRegions: true);
        skiaRenderer.DpiScale = dpiScale;

        var contentOffset = chromeRenderer.GetContentOffset();
        var scrollManager = new ScrollManager();

        var paintVisitor = new PaintVisitor(contentOffset);
        paintVisitor.VisitDocument(doc);

        var displayList = paintVisitor.GetDisplayList();
        displayList.SortByZIndex();

        PaintVisitor? cachedPaintVisitor = null;
        float lastLayoutWidth = 0;
        float lastContentHeight = 0;

        window.OnMouseWheel = (delta) =>
        {
            scrollManager.ScrollBy((float)delta);
            // 不需要重建显示列表，滚动变换在渲染时应用
        };

        window.OnScrollbarClick = (isVertical, isUp) =>
        {
            if (isVertical)
            {
                if (isUp)
                    scrollManager.PageUp();
                else
                    scrollManager.PageDown();
            }
            else
            {
                if (isUp)
                    scrollManager.PageLeft();
                else
                    scrollManager.PageRight();
            }
        };

        window.OnScrollbarDrag = (deltaX, deltaY) =>
        {
            if (deltaY != 0)
            {
                // 将像素移动转换为滚动增量
                float scrollDelta = deltaY * 3.0f;
                scrollManager.ScrollBy(0, scrollDelta);
            }
            if (deltaX != 0)
            {
                float scrollDelta = deltaX * 3.0f;
                scrollManager.ScrollBy(scrollDelta, 0);
            }
        };

        window.OnKeyDown = (key) =>
        {
            switch (key)
            {
                case Key.PageUp:
                    scrollManager.PageUp();
                    break;
                case Key.PageDown:
                    scrollManager.PageDown();
                    break;
                case Key.Home:
                    scrollManager.ScrollHome();
                    break;
                case Key.End:
                    scrollManager.ScrollEnd();
                    break;
                case Key.Up:
                    scrollManager.ScrollBy(0, -40);
                    break;
                case Key.Down:
                    scrollManager.ScrollBy(0, 40);
                    break;
                case Key.Left:
                    scrollManager.ScrollBy(-40, 0);
                    break;
                case Key.Right:
                    scrollManager.ScrollBy(40, 0);
                    break;
            }
            // 不需要重建显示列表，滚动变换在渲染时应用
        };

        window.Run((dt) =>
                {
                    eventLoop.ProcessTasks();

                    var (physicalWidth, physicalHeight) = window.GetClientSize();

                    // 将物理大小转换为逻辑大小
                    int windowWidth = (int)(physicalWidth / dpiScale);
                    int windowHeight = (int)(physicalHeight / dpiScale);

                    if (windowWidth > 0 && windowHeight > 0)
                    {
                        bool needsLayout = false;

                        if (skiaRenderer.Width != windowWidth || skiaRenderer.Height != windowHeight)
                        {
                            skiaRenderer.Resize(windowWidth, windowHeight);
                            needsLayout = true;
                        }

                        if (needsLayout || windowWidth != lastLayoutWidth)
                        {
                            lastLayoutWidth = windowWidth;

                            layoutEngine.Layout(doc, windowWidth, windowHeight);

                            var bodyBox = doc.Body?.LayoutBox;
                            float contentWidth = bodyBox?.BorderBox.Width ?? windowWidth;
                            float contentHeight = bodyBox?.BorderBox.Height ?? 0;
                            float viewportHeight = windowHeight - contentOffset - chromeRenderer.GetStatusBarHeight();

                            scrollManager.UpdateScroll(contentWidth, contentHeight, windowWidth, viewportHeight);

                            if (lastContentHeight != contentHeight)
                            {
                                lastContentHeight = contentHeight;
                                needsLayout = true;
                            }

                            cachedPaintVisitor = new PaintVisitor(contentOffset);
                            cachedPaintVisitor.VisitDocument(doc);
                            displayList = cachedPaintVisitor.GetDisplayList();
                            displayList.SortByZIndex();
                        }

                        // 新的渲染流程：先绘制 Chrome，再绘制内容（带滚动变换），最后绘制滚动条
                        skiaRenderer.Canvas.Clear(SKColors.White);

                        // 1. 绘制 Chrome（不受滚动影响，使用逻辑坐标）
                        var title = angleSharpDoc.Title ?? "UpBrowser";
                        chromeRenderer.RenderChrome(skiaRenderer.Canvas, windowWidth, windowHeight, "upbrowser://local", title);

                        // 2. 绘制内容（应用滚动变换，使用逻辑坐标）
                        float contentViewportHeight = windowHeight - contentOffset - chromeRenderer.GetStatusBarHeight();
                        skiaRenderer.RenderWithScroll(displayList, contentOffset, scrollManager.ScrollX, scrollManager.ScrollY, windowWidth, contentViewportHeight);

                        // 3. 绘制滚动条（不受滚动影响，使用逻辑坐标）
                        chromeRenderer.RenderScrollbars(skiaRenderer.Canvas, windowWidth, windowHeight, scrollManager);

                        var pixels = skiaRenderer.GetPixelData();
                        // 传递物理大小，因为bitmap是按DPI缩放创建的
                        window.Render(pixels, skiaRenderer.PhysicalWidth, skiaRenderer.PhysicalHeight);
                    }
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
                {
                    element.Attributes[attr.Name] = attr.Value;
                }

                if (childElement.HasAttribute("style"))
                {
                    var styleParser = new CssParser();
                    var props = styleParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                    foreach (var prop in props)
                    {
                        element.Style[prop.Key] = prop.Value;
                    }
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
                {
                    target.AppendChild(new TextNode(text));
                }
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
                {
                    element.Attributes[attr.Name] = attr.Value;
                }

                if (childElement.HasAttribute("style"))
                {
                    var styleParser = new CssParser();
                    var props = styleParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                    foreach (var prop in props)
                    {
                        element.Style[prop.Key] = prop.Value;
                    }
                }

                target.AppendChild(element);
                ConvertElementChildren(childElement, element);
            }
            else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
            {
                var text = child.TextContent?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    target.AppendChild(new TextNode(text));
                }
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
                {
                    uri = new Uri(baseUri, url);
                }
            }

            if (uri == null) return string.Empty;

            if (uri.IsFile || uri.Scheme == Uri.UriSchemeFile)
            {
                var path = uri.LocalPath;
                if (File.Exists(path))
                {
                    return await File.ReadAllTextAsync(path);
                }
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