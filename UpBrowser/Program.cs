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
    static async Task Main(string[] args)
    {
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
        Console.WriteLine("📊 Debug report saved to layout_debug.txt");
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

        var paintVisitor = new PaintVisitor(contentOffset);
        paintVisitor.VisitDocument(doc);

        var displayList = paintVisitor.GetDisplayList();
        displayList.SortByZIndex();

        PaintVisitor? cachedPaintVisitor = null;
        float lastLayoutWidth = 0;

        window.Run((dt) =>
        {
            eventLoop.ProcessTasks();

            var (windowWidth, windowHeight) = window.GetClientSize();
            if (windowWidth > 0 && windowHeight > 0)
            {
                bool needsRepaint = false;

                if (skiaRenderer.Width != windowWidth || skiaRenderer.Height != windowHeight)
                {
                    skiaRenderer.Resize(windowWidth, windowHeight);
                    needsRepaint = true;
                }

                if (needsRepaint || windowWidth != lastLayoutWidth)
                {
                    lastLayoutWidth = windowWidth;
                    
                    layoutEngine.Layout(doc, windowWidth, windowHeight);
                    
                    cachedPaintVisitor = new PaintVisitor(contentOffset);
                    cachedPaintVisitor.VisitDocument(doc);
                    displayList = cachedPaintVisitor.GetDisplayList();
                    displayList.SortByZIndex();
                }
            }

            skiaRenderer.Render(displayList);

            var title = angleSharpDoc.Title ?? "UpBrowser";
            chromeRenderer.RenderChrome(skiaRenderer.Canvas, windowWidth, windowHeight, "upbrowser://local", title);

            var pixels = skiaRenderer.GetPixelData();
            window.Render(pixels, skiaRenderer.Width, skiaRenderer.Height);
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