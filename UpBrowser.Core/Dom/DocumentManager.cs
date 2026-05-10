using System.Text;
using UpBrowser.Core.Css;
using AngleSharp;

namespace UpBrowser.Core.Dom;

public class DocumentManager
{
    private static readonly Stylesheet _uaStylesheet;
    private static readonly string _defaultHtml;

    static DocumentManager()
    {
        var parser = new CssParser();
        _uaStylesheet = parser.Parse(GetUserAgentStylesStatic());
        _defaultHtml = BuildDefaultHtml();
    }

    public async Task<DocumentLoadResult> LoadHtmlAsync(string html)
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
        styleComputer.AddStylesheet(_uaStylesheet);

        await LoadStylesFromHtml(angleSharpDoc, cssParser, styleComputer);
        styleComputer.ComputeStyles(doc);

        return new DocumentLoadResult(doc, angleSharpDoc);
    }

    public static string DefaultHtml => _defaultHtml;

    private static async Task LoadStylesFromHtml(AngleSharp.Dom.IDocument angleSharpDoc, CssParser cssParser, StyleComputer styleComputer)
    {
        var elements = angleSharpDoc.All;
        var styleElements = elements.Where(e => e.LocalName?.ToLowerInvariant() == "style");

        foreach (var styleElement in styleElements)
        {
            var cssText = styleElement.TextContent;
            if (!string.IsNullOrEmpty(cssText))
            {
                var stylesheet = cssParser.Parse(cssText);
                styleComputer.AddStylesheet(stylesheet);
            }
        }
    }

    private static void ConvertHtmlToDom(AngleSharp.Dom.IElement source, Document target)
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
                var text = NormalizeTextContent(child.TextContent ?? "");
                if (!string.IsNullOrWhiteSpace(text))
                    target.AppendChild(new TextNode(text));
            }
        }
    }

    private static void ConvertElementChildren(AngleSharp.Dom.INode source, Element target)
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
                var text = NormalizeTextContent(child.TextContent ?? "");
                if (!string.IsNullOrWhiteSpace(text))
                    target.AppendChild(new TextNode(text));
            }
        }
    }

    private static string NormalizeTextContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string BuildDefaultHtml()
    {
        return @"<html><head>
    <title>UpBrowser</title>
</head>
<body style=""background: #f5f5f5; margin: 0; padding: 20px; font-family: Arial, sans-serif;"">
    <h1 style=""color: #333; font-size: 32px; margin: 0 0 20px 0;"">Hello World</h1>
    <p style=""color: #666; font-size: 16px; line-height: 1.5;"">This is a test paragraph with some text content.</p>
    <div style=""background: #ffeb3b; padding: 20px; border: 2px solid #f44336; margin: 20px 0; border-radius: 8px;"">
        <h2 style=""color: #333; margin: 0 0 10px 0;"">Box Model Test</h2>
        <p style=""color: #555;"">This div has margin, border, padding, and content.</p>
    </div>
    <ul style=""color: #333;"">
        <li>List item 1</li>
        <li>List item 2</li>
        <li>List item 3</li>
    </ul>
    <button style=""background: #2196F3; color: white; padding: 10px 20px; border: none; border-radius: 4px; font-size: 14px;"">Click Me</button>
    <div style=""margin-top: 20px; padding: 15px; background: white; border: 1px solid #ddd;"">
        <span style=""color: red;"">Red text</span> and <span style=""color: blue;"">blue text</span> in same line.
    </div>
    <div style=""display: flex; gap: 10px; margin-top: 20px;"">
        <div style=""background: #e3f2fd; padding: 15px; flex: 1;"">Flex Item 1</div>
        <div style=""background: #f3e5f5; padding: 15px; flex: 1;"">Flex Item 2</div>
        <div style=""background: #e8f5e9; padding: 15px; flex: 1;"">Flex Item 3</div>
    </div>
    <div style=""position: relative; height: 100px; margin-top: 20px; background: #fff3e0;"">
        <div style=""position: absolute; top: 10px; right: 10px; background: #ff5722; color: white; padding: 5px 10px;"">Absolute Position</div>
    </div>
</body></html>";
    }

    private static string GetUserAgentStylesStatic()
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

    public record DocumentLoadResult(Document Document, AngleSharp.Dom.IDocument AngleSharpDoc);
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }
}
