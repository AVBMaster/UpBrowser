using SkiaSharp;
using UpBrowser.Core.Dom.Html;

namespace UpBrowser.Core.Dom;

public class Document : Node
{
    private Element? _documentElement;
    public Element? DocumentElement
    {
        get => _documentElement;
        set
        {
            _documentElement = value;
            if (value != null && !Children.Contains(value))
                AppendChild(value);
        }
    }
    public Element? Body { get; set; }
    public Element? Head { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Charset { get; set; } = "UTF-8";
    public string ContentType { get; set; } = "text/html";
    public string ReadyState { get; set; } = "loading";
    public string CompatMode { get; set; } = "CSS1Compat";
    public string? Cookie { get; set; }
    public bool Hidden { get; set; }
    public string VisibilityState { get; set; } = "visible";

    public Document()
    {
        NodeName = "#document";
    }

    public override NodeType NodeType => NodeType.Document;
    public override string NodeName => "#document";

    // Spec methods
    public Element CreateElement(string tagName)
    {
        return CreateHtmlElement(tagName);
    }

    public Element CreateElementNS(string? ns, string tagName)
    {
        var el = CreateHtmlElement(tagName);
        el.NamespaceUri = ns;
        return el;
    }

    private static HtmlElement CreateHtmlElement(string tagName)
    {
        return tagName.ToLowerInvariant() switch
        {
            "a" => new HTMLAnchorElement(null, "a"),
            "abbr" => new HtmlElement("abbr"),
            "address" => new HtmlElement("address"),
            "area" => new HtmlElement("area"),
            "article" => new HtmlElement("article"),
            "aside" => new HtmlElement("aside"),
            "audio" => new HTMLAudioElement(null, "audio"),
            "b" => new HtmlElement("b"),
            "base" => new HtmlElement("base"),
            "bdi" => new HtmlElement("bdi"),
            "bdo" => new HtmlElement("bdo"),
            "blockquote" => new HtmlElement("blockquote"),
            "body" => new HtmlElement("body"),
            "br" => new HtmlElement("br"),
            "button" => new HTMLButtonElement(null, "button"),
            "canvas" => new HTMLCanvasElement(null, "canvas"),
            "caption" => new HtmlElement("caption"),
            "cite" => new HtmlElement("cite"),
            "code" => new HtmlElement("code"),
            "col" => new HtmlElement("col"),
            "colgroup" => new HtmlElement("colgroup"),
            "data" => new HtmlElement("data"),
            "datalist" => new HTMLDataListElement(null, "datalist"),
            "dd" => new HtmlElement("dd"),
            "del" => new HtmlElement("del"),
            "details" => new HTMLDetailsElement(null, "details"),
            "dfn" => new HtmlElement("dfn"),
            "dialog" => new HTMLDialogElement(null, "dialog"),
            "div" => new HtmlElement("div"),
            "dl" => new HtmlElement("dl"),
            "dt" => new HtmlElement("dt"),
            "em" => new HtmlElement("em"),
            "embed" => new HtmlElement("embed"),
            "fieldset" => new HtmlElement("fieldset"),
            "figcaption" => new HtmlElement("figcaption"),
            "figure" => new HtmlElement("figure"),
            "footer" => new HtmlElement("footer"),
            "form" => new HTMLFormElement(null, "form"),
            "h1" => new HtmlElement("h1"),
            "h2" => new HtmlElement("h2"),
            "h3" => new HtmlElement("h3"),
            "h4" => new HtmlElement("h4"),
            "h5" => new HtmlElement("h5"),
            "h6" => new HtmlElement("h6"),
            "head" => new HtmlElement("head"),
            "header" => new HtmlElement("header"),
            "hgroup" => new HtmlElement("hgroup"),
            "hr" => new HtmlElement("hr"),
            "html" => new HtmlElement("html"),
            "i" => new HtmlElement("i"),
            "iframe" => new HTMLIFrameElement(null, "iframe"),
            "img" => new HTMLImageElement(null, "img"),
            "input" => new HTMLInputElement(null, "input"),
            "ins" => new HtmlElement("ins"),
            "kbd" => new HtmlElement("kbd"),
            "label" => new HTMLLabelElement(null, "label"),
            "legend" => new HtmlElement("legend"),
            "li" => new HTMLLIElement(null, "li"),
            "link" => new HTMLLinkElement(null, "link"),
            "main" => new HtmlElement("main"),
            "map" => new HtmlElement("map"),
            "mark" => new HtmlElement("mark"),
            "menu" => new HtmlElement("menu"),
            "meta" => new HtmlElement("meta"),
            "meter" => new HtmlElement("meter"),
            "nav" => new HtmlElement("nav"),
            "noscript" => new HtmlElement("noscript"),
            "object" => new HtmlElement("object"),
            "ol" => new HtmlElement("ol"),
            "optgroup" => new HtmlElement("optgroup"),
            "option" => new HTMLOptionElement(null, "option"),
            "output" => new HtmlElement("output"),
            "p" => new HtmlElement("p"),
            "picture" => new HtmlElement("picture"),
            "pre" => new HtmlElement("pre"),
            "progress" => new HtmlElement("progress"),
            "q" => new HtmlElement("q"),
            "rp" => new HtmlElement("rp"),
            "rt" => new HtmlElement("rt"),
            "ruby" => new HtmlElement("ruby"),
            "s" => new HtmlElement("s"),
            "samp" => new HtmlElement("samp"),
            "script" => new HTMLScriptElement(null, "script"),
            "section" => new HtmlElement("section"),
            "select" => new HTMLSelectElement(null, "select"),
            "slot" => new HTMLSlotElement(null, "slot"),
            "small" => new HtmlElement("small"),
            "source" => new HtmlElement("source"),
            "span" => new HtmlElement("span"),
            "strong" => new HtmlElement("strong"),
            "style" => new HTMLStyleElement(null, "style"),
            "sub" => new HtmlElement("sub"),
            "summary" => new HtmlElement("summary"),
            "sup" => new HtmlElement("sup"),
            "table" => new HtmlElement("table"),
            "tbody" => new HtmlElement("tbody"),
            "td" => new HtmlElement("td"),
            "template" => new HTMLTemplateElement(null, "template"),
            "textarea" => new HTMLTextAreaElement(null, "textarea"),
            "tfoot" => new HtmlElement("tfoot"),
            "th" => new HtmlElement("th"),
            "thead" => new HtmlElement("thead"),
            "time" => new HtmlElement("time"),
            "title" => new HtmlElement("title"),
            "tr" => new HtmlElement("tr"),
            "track" => new HtmlElement("track"),
            "u" => new HtmlElement("u"),
            "ul" => new HtmlElement("ul"),
            "var" => new HtmlElement("var"),
            "video" => new HTMLVideoElement(null, "video"),
            "wbr" => new HtmlElement("wbr"),
            _ => new HtmlElement(tagName)
        };
    }

    public TextNode CreateTextNode(string text)
    {
        return new TextNode(text);
    }

    public CommentNode CreateComment(string data)
    {
        return new CommentNode(data);
    }

    public DocumentFragment CreateDocumentFragment()
    {
        return new DocumentFragment();
    }

    public CDataSection CreateCDataSection(string data)
    {
        return new CDataSection(data);
    }

    public ProcessingInstruction CreateProcessingInstruction(string target, string data)
    {
        return new ProcessingInstruction(target, data);
    }

    public Attr CreateAttribute(string name)
    {
        return new Attr(name);
    }

    public Attr CreateAttributeNS(string? ns, string name)
    {
        return new Attr(name, ns);
    }

    public Element? GetElementById(string id)
    {
        return FindElementById(DocumentElement, id);
    }

    public List<Element> GetElementsByTagName(string tagName)
    {
        var result = new List<Element>();
        tagName = tagName.ToUpperInvariant();
        CollectElementsByTagName(DocumentElement, tagName, result);
        return result;
    }

    public List<Element> GetElementsByClassName(string className)
    {
        var result = new List<Element>();
        CollectElementsByClassName(DocumentElement, className, result);
        return result;
    }

    public Element? QuerySelector(string selector)
    {
        return QuerySelectorInternal(DocumentElement, selector);
    }

    public List<Element> QuerySelectorAll(string selector)
    {
        var result = new List<Element>();
        QuerySelectorAllInternal(DocumentElement, selector, result);
        return result;
    }

    public bool HasFocus() => true;

    public Node ImportNode(Node node, bool deep = false)
    {
        return node.CloneNode(deep);
    }

    public Node AdoptNode(Node node)
    {
        node.Remove();
        node.OwnerDocument = this;
        return node;
    }

    public Range CreateRange() => new();

    public void ParseHtml(string source)
    {
        ReadyState = "loading";
        ContentType = "text/html";
    }

    public void ParseXml(string source)
    {
        ReadyState = "loading";
        ContentType = "text/xml";
    }

    // Private helpers
    private static Element? FindElementById(Element? root, string id)
    {
        if (root == null) return null;
        if (root.Id == id) return root;
        foreach (var child in root.Children.OfType<Element>())
        {
            var found = FindElementById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectElementsByTagName(Element? root, string tagName, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.TagName == tagName)
                result.Add(child);
            CollectElementsByTagName(child, tagName, result);
        }
    }

    private static void CollectElementsByClassName(Element? root, string className, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.HasClass(className))
                result.Add(child);
            CollectElementsByClassName(child, className, result);
        }
    }

    private static Element? QuerySelectorInternal(Element? root, string selector)
    {
        if (root == null) return null;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSimpleSelector(child, selector))
                return child;
            var found = QuerySelectorInternal(child, selector);
            if (found != null) return found;
        }
        return null;
    }

    private static void QuerySelectorAllInternal(Element? root, string selector, List<Element> result)
    {
        if (root == null) return;
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSimpleSelector(child, selector))
                result.Add(child);
            QuerySelectorAllInternal(child, selector, result);
        }
    }

    private static bool MatchesSimpleSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }
}