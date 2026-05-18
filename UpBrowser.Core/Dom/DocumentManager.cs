using System.Text;
using UpBrowser.Core.Css;
using AngleSharp;
using AngleSharp.Css.Parser;

namespace UpBrowser.Core.Dom;

public class DocumentManager
{
    private static readonly Stylesheet _uaStylesheet;
    private static readonly string _defaultHtml;
    private static readonly CssParser _cssParser = new();

    static DocumentManager()
    {
        _uaStylesheet = _cssParser.Parse(GetUserAgentStylesStatic());
        _defaultHtml = BuildDefaultHtml();
    }

    public async Task<DocumentLoadResult> LoadHtmlAsync(string html, string? baseUrl = null)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var angleSharpDoc = await context.OpenAsync(req => req.Content(html));

        var doc = new Document
        {
            Url = baseUrl ?? "upbrowser://local",
            Title = angleSharpDoc.Title ?? "Untitled"
        };

        try
        {
            ConvertHtmlToDom(angleSharpDoc.DocumentElement!, doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DOM] Error converting HTML to DOM: {ex.Message}");
        }

        var styleComputer = new StyleComputer();
        styleComputer.AddStylesheet(_uaStylesheet);

        try
        {
            await LoadStylesFromHtml(angleSharpDoc, styleComputer, baseUrl);
            styleComputer.ComputeStyles(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSS] Error computing styles: {ex.Message}");
        }

        return new DocumentLoadResult(doc, angleSharpDoc);
    }

    public static string DefaultHtml => _defaultHtml;

    private async Task LoadStylesFromHtml(AngleSharp.Dom.IDocument angleSharpDoc, StyleComputer styleComputer, string? baseUrl)
    {
        var elements = angleSharpDoc.All;

        var styleElements = elements.Where(e => e.LocalName?.ToLowerInvariant() == "style");
        foreach (var styleElement in styleElements)
        {
            var cssText = styleElement.TextContent;
            if (!string.IsNullOrEmpty(cssText))
            {
                try
                {
                    var stylesheet = _cssParser.Parse(cssText);
                    styleComputer.AddStylesheet(stylesheet);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CSS] Failed to parse <style>: {ex.Message}");
                }
            }
        }

        var linkElements = elements.Where(e =>
            e.LocalName?.ToLowerInvariant() == "link" &&
            e.GetAttribute("rel")?.ToLowerInvariant() == "stylesheet");

        foreach (var link in linkElements)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrEmpty(href)) continue;

            var url = ResolveUrl(baseUrl, href);
            if (url == null) continue;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var stylesheet = _cssParser.Parse(cssText);
                styleComputer.AddStylesheet(stylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to load stylesheet '{url}': {ex.Message}");
            }
        }
    }

    private static string? ResolveUrl(string? baseUrl, string href)
    {
        if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("data:"))
            return href;

        if (baseUrl != null && (baseUrl.StartsWith("http://") || baseUrl.StartsWith("https://")))
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                var resolved = new Uri(baseUri, href);
                return resolved.ToString();
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private void ConvertHtmlToDom(AngleSharp.Dom.IElement source, Document target)
    {
        if (source == null) return;

        foreach (var child in source.ChildNodes)
        {
            try
            {
                if (child is AngleSharp.Dom.IElement childElement)
                {
                    var element = new HtmlElement(childElement.LocalName);

                    foreach (var attr in childElement.Attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Name))
                            element.Attributes[attr.Name] = attr.Value ?? "";
                    }

                    if (childElement.HasAttribute("style"))
                    {
                        var props = _cssParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
                        foreach (var prop in props)
                            element.Style[prop.Key] = prop.Value;
                    }

                    var tagName = childElement.LocalName?.ToLowerInvariant();

                    if (tagName == "html")
                    {
                        target.DocumentElement = element;
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "body")
                    {
                        target.Body = element;
                        if (target.DocumentElement == null)
                            target.DocumentElement = element;
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "head")
                    {
                        target.Head = element;
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "title")
                    {
                        target.Title = childElement.TextContent ?? "";
                    }
                    else
                    {
                        if (target.DocumentElement == null)
                            target.DocumentElement = element;
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
            catch (Exception ex)
            {
                Console.WriteLine($"[DOM] Error converting element: {ex.Message}");
            }
        }
    }

    private void ConvertElementChildren(AngleSharp.Dom.INode source, Element target)
    {
        if (source == null || target == null) return;

        foreach (var child in source.ChildNodes)
        {
            try
            {
                if (child is AngleSharp.Dom.IElement childElement)
                {
                    var element = new HtmlElement(childElement.LocalName);

                    foreach (var attr in childElement.Attributes)
                    {
                        if (!string.IsNullOrEmpty(attr.Name))
                            element.Attributes[attr.Name] = attr.Value ?? "";
                    }

                    if (childElement.HasAttribute("style"))
                    {
                        var props = _cssParser.ParseInlineStyle(childElement.GetAttribute("style") ?? "");
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
            catch (Exception ex)
            {
                Console.WriteLine($"[DOM] Error converting child: {ex.Message}");
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
    <h1 id=""title"" style=""color: #333; font-size: 32px; margin: 0 0 20px 0;"">Hello World</h1>
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
    <button id=""mybtn"" style=""background: #2196F3; color: white; padding: 10px 20px; border: none; border-radius: 4px; font-size: 14px;"">Click Me</button>
    <div id=""output"" style=""margin-top: 20px; padding: 15px; background: white; border: 1px solid #ddd;"">
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
    <script>
        console.log('JS bridge active!');
        var title = document.getElementById('title');
        if (title) console.log('Title text:', title.textContent);
        var btn = document.getElementById('mybtn');
        if (btn) {
            btn.addEventListener('click', function() {
                var out = document.getElementById('output');
                if (out) {
                    out.innerHTML = '<b style=""color: green;"">Button clicked via JS!</b>';
                    out.style.background = '#e8f5e9';
                }
            });
        }
    </script>
</body></html>";
    }

    private static string GetUserAgentStylesStatic()
    {
        return @"
            html { display: block; }
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
