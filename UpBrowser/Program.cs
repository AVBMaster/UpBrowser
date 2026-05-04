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
    <title>UpBrowser Test Page</title>
</head>
<body style='background: #f0f0f0; margin: 0; padding: 20px; font-family: Arial, sans-serif;'>
    <!-- 测试标题 -->
    <h1>标题1 - H1</h1>
    <h2>标题2 - H2</h2>
    <h3>标题3 - H3</h3>
    <h4>标题4 - H4</h4>
    <h5>标题5 - H5</h5>
    <h6>标题6 - H6</h6>
    
    <hr>
    
    <!-- 测试段落 -->
    <p>这是一个普通段落，包含一些文本内容。</p>
    <p style='color: red;'>红色文字的段落</p>
    <p style='font-size: 20px;'>大号文字段落</p>
    <p style='font-weight: bold;'>粗体段落</p>
    <p style='font-style: italic;'>斜体段落</p>
    <p><strong>粗体</strong>和<em>斜体</em>混排</p>
    <p><u>下划线</u>和<s>删除线</s></p>
    <a href='#'>这是一个链接</a>
    
    <hr>
    
    <!-- 测试 div 块级元素 -->
    <div style='background: yellow; padding: 15px; margin: 10px 0; border: 2px solid orange;'>
        普通 div 块级元素
    </div>
    
    <!-- 测试 span 内联元素 -->
    <div style='background: #e0e0e0; padding: 15px; margin: 10px 0;'>
        这是一些<span style='color: red;'>红色</span>和<span style='color: blue;'>蓝色</span>的<span style='background: yellow;'>内联</span>文本。
    </div>
    
    <hr>
    
    <!-- 测试无序列表 -->
    <h3>无序列表 (ul)</h3>
    <ul style='background: #f9f9f9; padding: 15px; border: 1px solid #ccc;'>
        <li>列表项 1</li>
        <li>列表项 2</li>
        <li>列表项 3</li>
    </ul>
    
    <!-- 测试有序列表 -->
    <h3>有序列表 (ol)</h3>
    <ol style='background: #f9f9f9; padding: 15px; border: 1px solid #ccc;'>
        <li>第一项</li>
        <li>第二项</li>
        <li>第三项</li>
    </ol>
    
    <hr>
    
    <!-- 测试按钮 -->
    <h3>按钮测试</h3>
    <button style='background: #2196F3; color: white; padding: 10px 20px; border: none; border-radius: 4px;'>普通按钮</button>
    <button style='background: #4CAF50; color: white; padding: 10px 20px; border: 2px solid #388E3C;'>带边框按钮</button>
    <input type='text' placeholder='文本输入框' style='padding: 8px; border: 1px solid #ccc;'>
    
    <hr>
    
    <!-- 测试 Flexbox 布局 -->
    <h3>Flexbox 布局测试</h3>
    <div style='display: flex; gap: 10px; margin: 10px 0;'>
        <div style='background: #e3f2fd; padding: 15px; flex: 1;'>Flex Item 1</div>
        <div style='background: #f3e5f5; padding: 15px; flex: 1;'>Flex Item 2</div>
        <div style='background: #e8f5e9; padding: 15px; flex: 1;'>Flex Item 3</div>
    </div>
    <div style='display: flex; gap: 10px; margin: 10px 0; flex-wrap: wrap;'>
        <div style='background: #ffebee; padding: 15px; width: 200px;'>换行 Item 1</div>
        <div style='background: #fff3e0; padding: 15px; width: 200px;'>换行 Item 2</div>
        <div style='background: #e8eaf6; padding: 15px; width: 200px;'>换行 Item 3</div>
    </div>
    
    <hr>
    
    <!-- 测试表格 -->
    <h3>表格测试</h3>
    <table style='border-collapse: collapse; width: 100%; margin: 10px 0;'>
        <tr style='background: #f5f5f5;'>
            <th style='border: 1px solid #ccc; padding: 10px;'>表头1</th>
            <th style='border: 1px solid #ccc; padding: 10px;'>表头2</th>
            <th style='border: 1px solid #ccc; padding: 10px;'>表头3</th>
        </tr>
        <tr>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格1</td>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格2</td>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格3</td>
        </tr>
        <tr style='background: #fafafa;'>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格4</td>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格5</td>
            <td style='border: 1px solid #ccc; padding: 10px;'>单元格6</td>
        </tr>
    </table>
    
    <hr>
    
    <!-- 测试绝对定位 -->
    <h3>绝对定位测试</h3>
    <div style='position: relative; height: 100px; background: #fff3e0; border: 1px solid #ff9800; margin: 10px 0;'>
        <div style='position: absolute; top: 10px; left: 10px; background: #ff5722; color: white; padding: 5px 10px;'>左上角</div>
        <div style='position: absolute; top: 10px; right: 10px; background: #4CAF50; color: white; padding: 5px 10px;'>右上角</div>
        <div style='position: absolute; bottom: 10px; left: 10px; background: #2196F3; color: white; padding: 5px 10px;'>左下角</div>
        <div style='position: absolute; bottom: 10px; right: 10px; background: #9C27B0; color: white; padding: 5px 10px;'>右下角</div>
    </div>
    
    <hr>
    
    <!-- 测试更多样式 -->
    <h3>其他样式测试</h3>
    <div style='background: linear-gradient(to right, red, orange, yellow, green, blue, indigo, violet); padding: 20px; margin: 10px 0; color: white;'>
        渐变背景 (暂不支持，仅作占位)
    </div>
    <div style='background: #ff9800; padding: 20px; margin: 10px 0; border-radius: 10px;'>
        圆角 div
    </div>
    <div style='border: 3px dashed #666; padding: 15px; margin: 10px 0;'>
        虚线边框
    </div>
    
    <hr>
    
    <!-- 测试多行文本 -->
    <h3>多行文本测试</h3>
    <p style='line-height: 1.5;'>
        这是第一行文本。
        这是第二行文本。
        这是第三行文本。
        这是第四行文本。
    </p>
    <p style='line-height: 2.0;'>
        行高2.0的第一行。
        行高2.0的第二行。
        行高2.0的第三行。
    </p>
    
    <div style='height: 50px;'></div>
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