using UpBrowser.Core.Dom;
using DomDocument = UpBrowser.Core.Dom.Document;

namespace UpBrowser.Core.JavaScript;

public class DocumentHost
{
    private readonly DomDocument _document;
    private ElementHost? _documentElementHost;
    private ElementHost? _bodyHost;
    private readonly Dictionary<string, string> _cookies = new();

    public string cookie
    {
        get => string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            var parts = value.Split('=', 2);
            if (parts.Length == 2)
                _cookies[parts[0].Trim()] = parts[1].Split(';')[0].Trim();
        }
    }

    public DocumentHost(Document document)
    {
        _document = document;
    }

    public Document NativeDocument => _document;

    public ElementHost? documentElement
    {
        get
        {
            if (_document.DocumentElement == null) return null;
            _documentElementHost ??= new ElementHost(_document.DocumentElement);
            return _documentElementHost;
        }
    }

    public ElementHost? body
    {
        get
        {
            if (_document.Body == null) return null;
            if (_bodyHost == null || _bodyHost.NativeElement != _document.Body)
                _bodyHost = new ElementHost(_document.Body);
            return _bodyHost;
        }
        set
        {
            if (value != null)
                _document.Body = value.NativeElement;
        }
    }

    public ElementHost? head
    {
        get
        {
            if (_document.Head == null) return null;
            return new ElementHost(_document.Head);
        }
    }

    public string title
    {
        get => _document.Title;
        set => _document.Title = value;
    }

    public string URL => _document.Url;
    public string documentURI => _document.Url;
    public string? baseURI => _document.Url;

    public string? compatMode => "CSS1Compat";
    public string? characterSet => "UTF-8";
    public string? contentType => "text/html";

    public ElementHost? getElementById(string id)
    {
        if (_document.DocumentElement == null) return null;
        var el = FindElementById(_document.DocumentElement, id);
        return el != null ? new ElementHost(el) : null;
    }

    public object? querySelector(string selector)
    {
        if (_document.DocumentElement == null) return null;
        var el = QuerySelectorInternal(_document.DocumentElement, selector);
        return el != null ? new ElementHost(el) : null;
    }

    public object[] querySelectorAll(string selector)
    {
        if (_document.DocumentElement == null) return Array.Empty<object>();
        return QuerySelectorAllInternal(_document.DocumentElement, selector)
            .Select(e => (object)new ElementHost(e)).ToArray();
    }

    public object[] getElementsByTagName(string tagName)
    {
        if (_document.DocumentElement == null) return Array.Empty<object>();
        return GetElementsByTagNameInternal(_document.DocumentElement, tagName)
            .Select(e => (object)new ElementHost(e)).ToArray();
    }

    public object[] getElementsByClassName(string className)
    {
        if (_document.DocumentElement == null) return Array.Empty<object>();
        return GetElementsByClassNameInternal(_document.DocumentElement, className)
            .Select(e => (object)new ElementHost(e)).ToArray()
            ?? Array.Empty<object>();
    }

    public object[] getElementsByName(string name)
    {
        if (_document.DocumentElement == null) return Array.Empty<object>();
        return GetElementsByNameInternal(_document.DocumentElement, name)
            .Select(e => (object)new ElementHost(e)).ToArray();
    }

    public ElementHost createElement(string tagName)
    {
        var el = new HtmlElement(tagName);
        _document.DocumentElement?.AppendChild(el);
        return new ElementHost(el);
    }

    public TextNodeWrapper createTextNode(string text)
    {
        return new TextNodeWrapper(new TextNode(text));
    }

    public object? createComment(string data) =>
        new TextNodeWrapper(new TextNode(data));

    public object? createDocumentFragment() => null;

    public ElementHost? getElementByClassName(string className) =>
        getElementsByClassName(className).FirstOrDefault() as ElementHost;

    public bool hasFocus() => true;

    public string? write(string text)
    {
        return null;
    }

    public string? writeln(string text)
    {
        return null;
    }

    // ===== Private helpers =====

    private static Element? FindElementById(Element root, string id)
    {
        if (root.Id == id) return root;
        foreach (var child in root.Children.OfType<Element>())
        {
            var found = FindElementById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static Element? QuerySelectorInternal(Element root, string selector)
    {
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSelector(child, selector))
                return child;
            var found = QuerySelectorInternal(child, selector);
            if (found != null) return found;
        }
        return null;
    }

    private static List<Element> QuerySelectorAllInternal(Element root, string selector)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSelector(child, selector))
                result.Add(child);
            result.AddRange(QuerySelectorAllInternal(child, selector));
        }
        return result;
    }

    private static List<Element> GetElementsByTagNameInternal(Element root, string tagName)
    {
        tagName = tagName.ToUpperInvariant();
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.TagName == tagName)
                result.Add(child);
            result.AddRange(GetElementsByTagNameInternal(child, tagName));
        }
        return result;
    }

    private static List<Element> GetElementsByClassNameInternal(Element root, string className)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.HasClass(className))
                result.Add(child);
            result.AddRange(GetElementsByClassNameInternal(child, className));
        }
        return result;
    }

    private static List<Element> GetElementsByNameInternal(Element root, string name)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.GetAttribute("name") == name)
                result.Add(child);
            result.AddRange(GetElementsByNameInternal(child, name));
        }
        return result;
    }

    private static bool MatchesSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    public object getComputedStyle(ElementHost element)
    {
        var computedStyle = element.NativeElement.ComputedStyle;
        if (computedStyle == null)
            return new ComputedStyleHost(new Dictionary<string, string>());

        var props = new Dictionary<string, string>
        {
            ["width"] = computedStyle.Width.ToString(),
            ["height"] = computedStyle.Height.ToString(),
            ["display"] = computedStyle.Display.ToString().ToLowerInvariant(),
            ["position"] = computedStyle.Position.ToString().ToLowerInvariant(),
            ["float"] = computedStyle.Float.ToString().ToLowerInvariant(),
            ["clear"] = computedStyle.Clear.ToString().ToLowerInvariant(),
            ["color"] = $"rgb({computedStyle.Color.Red}, {computedStyle.Color.Green}, {computedStyle.Color.Blue})",
            ["font-family"] = computedStyle.FontFamily ?? "",
            ["font-size"] = $"{computedStyle.FontSize}px",
            ["font-weight"] = computedStyle.FontWeight.ToString(),
            ["font-style"] = computedStyle.FontStyle.ToString().ToLowerInvariant(),
            ["line-height"] = computedStyle.LineHeight.ToString(),
            ["text-align"] = computedStyle.TextAlign.ToString().ToLowerInvariant(),
            ["text-decoration"] = computedStyle.TextDecoration.ToString().ToLowerInvariant(),
            ["white-space"] = computedStyle.WhiteSpace.ToString().ToLowerInvariant(),
            ["visibility"] = computedStyle.Visibility.ToString().ToLowerInvariant(),
            ["overflow"] = computedStyle.Overflow.ToString().ToLowerInvariant(),
            ["opacity"] = computedStyle.Opacity.ToString(),
            ["z-index"] = computedStyle.ZIndex?.ToString() ?? "auto",
            ["background-color"] = computedStyle.BackgroundColor.HasValue
                ? $"rgba({computedStyle.BackgroundColor.Value.Red}, {computedStyle.BackgroundColor.Value.Green}, {computedStyle.BackgroundColor.Value.Blue}, {computedStyle.BackgroundColor.Value.Alpha / 255f})"
                : "transparent",
            ["margin-top"] = computedStyle.MarginTop.ToString(),
            ["margin-right"] = computedStyle.MarginRight.ToString(),
            ["margin-bottom"] = computedStyle.MarginBottom.ToString(),
            ["margin-left"] = computedStyle.MarginLeft.ToString(),
            ["padding-top"] = computedStyle.PaddingTop.ToString(),
            ["padding-right"] = computedStyle.PaddingRight.ToString(),
            ["padding-bottom"] = computedStyle.PaddingBottom.ToString(),
            ["padding-left"] = computedStyle.PaddingLeft.ToString(),
            ["border-top-width"] = $"{computedStyle.BorderTopWidth}px",
            ["border-right-width"] = $"{computedStyle.BorderRightWidth}px",
            ["border-bottom-width"] = $"{computedStyle.BorderBottomWidth}px",
            ["border-left-width"] = $"{computedStyle.BorderLeftWidth}px",
            ["flex-direction"] = computedStyle.FlexDirection.ToString().ToLowerInvariant(),
            ["flex-wrap"] = computedStyle.FlexWrap.ToString().ToLowerInvariant(),
            ["justify-content"] = computedStyle.JustifyContent.ToString().ToLowerInvariant(),
            ["align-items"] = computedStyle.AlignItems.ToString().ToLowerInvariant()
        };

        return new ComputedStyleHost(props);
    }

    public object? createEvent(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "customevent" => new ScriptEvent("Custom", null),
            "mouseevent" => new ScriptEvent("Mouse", null),
            "keyevent" => new ScriptEvent("Key", null),
            _ => new ScriptEvent(type ?? "Event", null)
        };
    }

    public string? getSelection() => null;

    public ElementHost? activeElement => null;

    public string readyState => "complete";

    public string domain
    {
        get => _document.Url;
        set { }
    }

    public string referrer => "";

    public string lastModified => DateTime.Now.ToString();

public class ComputedStyleHost
{
    private readonly Dictionary<string, string> _properties;

    public ComputedStyleHost(Dictionary<string, string> properties)
    {
        _properties = properties;
    }

    public string? getProperty(string name)
    {
        return _properties.GetValueOrDefault(name?.ToLowerInvariant());
    }
}
