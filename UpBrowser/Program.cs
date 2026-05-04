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

    static async Task Main(string[] args)
    {
        SetProcessDPIAware();

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
        <p style='color: red;'>This is the bottom of the page!</p>
    </div>
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
        var stylesheet = cssParser.Parse(@"
            body { font-family: Arial, sans-serif; display: block; }
            h1 { display: block; margin: 0 0 20px 0; font-size: 32px; font-weight: bold; }
            h2 { display: block; margin: 0 0 10px 0; font-weight: bold; }
            p { display: block; margin: 0 0 10px 0; }
            ul { display: block; margin: 10px 0; padding-left: 20px; }
            li { display: list-item; }
            button { display: inline-block; cursor: pointer; }
            div { display: block; }
            span { display: inline; }
            a { display: inline; color: #0000EE; }
            strong { font-weight: bold; }
            em { font-style: italic; }
        ");
        styleComputer.AddStylesheet(stylesheet);
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

        var window = BrowserWindow.Create(1024, 768, "UpBrowser");

        var skiaRenderer = new SkiaRenderer();
        skiaRenderer.Initialize(1024, 768, enableDirtyRegions: true);

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
                // 不需要重建显示列表，滚动变换在渲染时应用
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

            var (windowWidth, windowHeight) = window.GetClientSize();
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
                
                // 1. 绘制 Chrome（不受滚动影响）
                var title = angleSharpDoc.Title ?? "UpBrowser";
                chromeRenderer.RenderChrome(skiaRenderer.Canvas, windowWidth, windowHeight, "upbrowser://local", title);
                
                // 2. 绘制内容（应用滚动变换）
                float contentViewportHeight = windowHeight - contentOffset - chromeRenderer.GetStatusBarHeight();
                skiaRenderer.RenderWithScroll(displayList, contentOffset, scrollManager.ScrollX, scrollManager.ScrollY, windowWidth, contentViewportHeight);
                
                // 3. 绘制滚动条（不受滚动影响）
                chromeRenderer.RenderScrollbars(skiaRenderer.Canvas, windowWidth, windowHeight, scrollManager);

                var pixels = skiaRenderer.GetPixelData();
                window.Render(pixels, skiaRenderer.Width, skiaRenderer.Height);
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
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }
}