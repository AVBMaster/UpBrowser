using System.Reflection;
using System.Text;
using UpBrowser.Core.Css;
using UpBrowser.Core.Dom.Html;
using AngleSharp;
using AngleSharp.Css.Parser;

namespace UpBrowser.Core.Dom;

public class DocumentManager
{
    private static readonly Stylesheet _uaStylesheet;
    private static readonly string _defaultHtml;
    private static readonly CssParser _cssParser = new();

    private static string LoadEmbeddedResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"UpBrowser.Core.Resources.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static DocumentManager()
    {
        _uaStylesheet = _cssParser.Parse(LoadEmbeddedResource("Css.ua-stylesheet.css"));
        _defaultHtml = LoadEmbeddedResource("Html.default.html");
    }

    public async Task<DocumentLoadResult> LoadHtmlAsync(string html, string? baseUrl = null, float viewportWidth = 1024f, float viewportHeight = 768f, float dpiScale = 1.0f)
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
            styleComputer.ComputeStyles(doc, viewportWidth, viewportHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CSS] Error computing styles: {ex.Message}");
        }

        try
        {
            var layoutEngine = new UpBrowser.Core.Layout.LayoutEngine();
            layoutEngine.Layout(doc, viewportWidth, viewportHeight, dpiScale);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Layout] Error during layout: {ex.Message}");
        }

        return new DocumentLoadResult(doc, angleSharpDoc, styleComputer);
    }

    public static string DefaultHtml => _defaultHtml;

    private static string? _jsTestHtml;
    public static string JsTestHtml => _jsTestHtml ??= LoadEmbeddedResource("Html.js-test.html");

    private static string? _elementTestHtml;
    public static string ElementTestHtml => _elementTestHtml ??= LoadEmbeddedResource("Html.element-test.html");

    private static string? _elementAppearanceHtml;
    public static string ElementAppearanceHtml => _elementAppearanceHtml ??= LoadEmbeddedResource("Html.element-appearance.html");

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
                    await ProcessImports(stylesheet, styleComputer, baseUrl);
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
                await ProcessImports(stylesheet, styleComputer, url);
                styleComputer.AddStylesheet(stylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to load stylesheet '{url}': {ex.Message}");
            }
        }
    }

    private async Task ProcessImports(Stylesheet stylesheet, StyleComputer styleComputer, string? baseUrl)
    {
        foreach (var importRule in stylesheet.ImportRules)
        {
            if (string.IsNullOrEmpty(importRule.Url)) continue;
            var url = ResolveUrl(baseUrl, importRule.Url);
            if (url == null) continue;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var cssText = await client.GetStringAsync(url);
                var importedStylesheet = _cssParser.Parse(cssText);
                await ProcessImports(importedStylesheet, styleComputer, url);
                styleComputer.AddStylesheet(importedStylesheet);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CSS] Failed to process @import '{importRule.Url}': {ex.Message}");
            }
        }
    }

    private static string? ResolveUrl(string? baseUrl, string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        if (href.StartsWith("http://") || href.StartsWith("https://") || href.StartsWith("data:") || href.StartsWith("blob:"))
            return href;
        if (href.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + href;
            return "http:" + href;
        }
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
        var htmlElement = new HtmlElement("html");
        target.DocumentElement = htmlElement;
        target.AppendChild(htmlElement);

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
                    if (tagName == "html") { }
                    else if (tagName == "head")
                    {
                        target.Head = element;
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "body")
                    {
                        target.Body = element;
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                    else if (tagName == "title")
                    {
                        target.Title = childElement.TextContent ?? "";
                        if (target.Head != null)
                        {
                            var titleElem = new HtmlElement("title");
                            titleElem.AppendChild(new TextNode(childElement.TextContent ?? ""));
                            target.Head.AppendChild(titleElem);
                        }
                    }
                    else
                    {
                        htmlElement.AppendChild(element);
                        ConvertElementChildren(childElement, element);
                    }
                }
                else if (child.NodeType == AngleSharp.Dom.NodeType.Text)
                {
                    var text = NormalizeTextContent(child.TextContent ?? "");
                    if (!string.IsNullOrWhiteSpace(text))
                        htmlElement.AppendChild(new TextNode(text));
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

    public Stylesheet GetUaStylesheet() => _uaStylesheet;

    public record DocumentLoadResult(Document Document, AngleSharp.Dom.IDocument AngleSharpDoc, StyleComputer? StyleComputer = null);
}

public class HtmlElement : Element
{
    public HtmlElement(string tagName) : base(tagName) { }

    // Global HTML attributes (HTMLElement spec)
    public string? Title
    {
        get => GetAttribute("title");
        set { if (value != null) SetAttribute("title", value); else RemoveAttribute("title"); }
    }
    public string? Lang
    {
        get => GetAttribute("lang");
        set { if (value != null) SetAttribute("lang", value); else RemoveAttribute("lang"); }
    }
    public bool Translate
    {
        get => !string.Equals(GetAttribute("translate"), "no", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("translate", value ? "yes" : "no");
    }
    public string? Dir
    {
        get => GetAttribute("dir");
        set { if (value != null) SetAttribute("dir", value); else RemoveAttribute("dir"); }
    }
    public bool Hidden
    {
        get => HasAttribute("hidden");
        set { if (value) SetAttribute("hidden", ""); else RemoveAttribute("hidden"); }
    }
    public bool Inert
    {
        get => HasAttribute("inert");
        set { if (value) SetAttribute("inert", ""); else RemoveAttribute("inert"); }
    }
    public bool Draggable
    {
        get => string.Equals(GetAttribute("draggable"), "true", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("draggable", value ? "true" : "false");
    }
    public bool Spellcheck
    {
        get => !string.Equals(GetAttribute("spellcheck"), "false", StringComparison.OrdinalIgnoreCase);
        set => SetAttribute("spellcheck", value ? "true" : "false");
    }
    public string? ContentEditable
    {
        get => GetAttribute("contenteditable") ?? "inherit";
        set { if (value != null) SetAttribute("contenteditable", value); else RemoveAttribute("contenteditable"); }
    }
    public bool IsContentEditable => ContentEditable == "true";
    public string? InputMode
    {
        get => GetAttribute("inputmode");
        set { if (value != null) SetAttribute("inputmode", value); else RemoveAttribute("inputmode"); }
    }
    public string? EnterKeyHint
    {
        get => GetAttribute("enterkeyhint");
        set { if (value != null) SetAttribute("enterkeyhint", value); else RemoveAttribute("enterkeyhint"); }
    }
    public string? Autocapitalize
    {
        get => GetAttribute("autocapitalize") ?? "";
        set { if (value != null) SetAttribute("autocapitalize", value); else RemoveAttribute("autocapitalize"); }
    }
    public string? Nonce
    {
        get => GetAttribute("nonce");
        set { if (value != null) SetAttribute("nonce", value); else RemoveAttribute("nonce"); }
    }
    public string? Popover
    {
        get => GetAttribute("popover");
        set { if (value != null) SetAttribute("popover", value); else RemoveAttribute("popover"); }
    }
    public int TabIndex
    {
        get => int.TryParse(GetAttribute("tabindex"), out var v) ? v : 0;
        set => SetAttribute("tabindex", value.ToString());
    }
    public string? AccessKey
    {
        get => GetAttribute("accesskey");
        set { if (value != null) SetAttribute("accesskey", value); else RemoveAttribute("accesskey"); }
    }
    public string? AccessKeyLabel => AccessKey;
    public DOMStringMap Dataset => new(x => GetAttribute(x), (x, v) => SetAttribute(x, v));
    public ElementInternals AttachInternals() => new(this);
}
