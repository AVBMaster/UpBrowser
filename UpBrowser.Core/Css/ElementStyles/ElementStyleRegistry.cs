using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class ElementStyleRegistry
{
    private static readonly Dictionary<string, Action<ComputedStyle, string, Element?>> _elementStyles = new();

    static ElementStyleRegistry()
    {
        RegisterAllElements();
    }

    private static void RegisterAllElements()
    {
        Register("html", (s, t, _) => RootElements.Apply(s, t));
        Register("body", (s, t, _) => RootElements.Apply(s, t));
        Register("head", (s, t, _) => RootElements.Apply(s, t));
        Register("title", (s, t, _) => RootElements.Apply(s, t));
        Register("meta", (s, t, _) => RootElements.Apply(s, t));
        Register("link", (s, t, _) => RootElements.Apply(s, t));
        Register("style", (s, t, _) => RootElements.Apply(s, t));
        Register("script", (s, t, _) => RootElements.Apply(s, t));
        Register("base", (s, t, _) => RootElements.Apply(s, t));
        Register("noscript", (s, t, _) => RootElements.Apply(s, t));
        Register("template", (s, t, _) => RootElements.Apply(s, t));

        Register("div", (s, t, _) => BlockElements.Apply(s, t));
        Register("p", (s, t, _) => BlockElements.Apply(s, t));
        Register("h1", (s, t, _) => BlockElements.Apply(s, t));
        Register("h2", (s, t, _) => BlockElements.Apply(s, t));
        Register("h3", (s, t, _) => BlockElements.Apply(s, t));
        Register("h4", (s, t, _) => BlockElements.Apply(s, t));
        Register("h5", (s, t, _) => BlockElements.Apply(s, t));
        Register("h6", (s, t, _) => BlockElements.Apply(s, t));
        Register("ul", (s, t, _) => BlockElements.Apply(s, t));
        Register("ol", (s, t, _) => BlockElements.Apply(s, t));
        Register("li", (s, t, _) => BlockElements.Apply(s, t));
        Register("dl", (s, t, _) => BlockElements.Apply(s, t));
        Register("dt", (s, t, _) => BlockElements.Apply(s, t));
        Register("dd", (s, t, _) => BlockElements.Apply(s, t));
        Register("table", (s, t, _) => BlockElements.Apply(s, t));
        Register("thead", (s, t, _) => BlockElements.Apply(s, t));
        Register("tbody", (s, t, _) => BlockElements.Apply(s, t));
        Register("tfoot", (s, t, _) => BlockElements.Apply(s, t));
        Register("tr", (s, t, _) => BlockElements.Apply(s, t));
        Register("th", (s, t, _) => BlockElements.Apply(s, t));
        Register("td", (s, t, _) => BlockElements.Apply(s, t));
        Register("caption", (s, t, _) => BlockElements.Apply(s, t));
        Register("pre", (s, t, _) => BlockElements.Apply(s, t));
        Register("blockquote", (s, t, _) => BlockElements.Apply(s, t));
        Register("address", (s, t, _) => BlockElements.Apply(s, t));
        Register("article", (s, t, _) => BlockElements.Apply(s, t));
        Register("aside", (s, t, _) => BlockElements.Apply(s, t));
        Register("main", (s, t, _) => BlockElements.Apply(s, t));
        Register("section", (s, t, _) => BlockElements.Apply(s, t));
        Register("nav", (s, t, _) => BlockElements.Apply(s, t));
        Register("header", (s, t, _) => BlockElements.Apply(s, t));
        Register("footer", (s, t, _) => BlockElements.Apply(s, t));
        Register("figure", (s, t, _) => BlockElements.Apply(s, t));
        Register("figcaption", (s, t, _) => BlockElements.Apply(s, t));
        Register("details", (s, t, _) => BlockElements.Apply(s, t));
        Register("summary", (s, t, _) => BlockElements.Apply(s, t));
        Register("dialog", (s, t, _) => BlockElements.Apply(s, t));
        Register("hr", (s, t, _) => BlockElements.Apply(s, t));
        Register("search", (s, t, _) => BlockElements.Apply(s, t));
        Register("hgroup", (s, t, _) => BlockElements.Apply(s, t));

        Register("span", (s, t, _) => InlineElements.Apply(s, t));
        Register("a", (s, t, _) => InlineElements.Apply(s, t));
        Register("strong", (s, t, _) => InlineElements.Apply(s, t));
        Register("b", (s, t, _) => InlineElements.Apply(s, t));
        Register("em", (s, t, _) => InlineElements.Apply(s, t));
        Register("i", (s, t, _) => InlineElements.Apply(s, t));
        Register("u", (s, t, _) => InlineElements.Apply(s, t));
        Register("s", (s, t, _) => InlineElements.Apply(s, t));
        Register("del", (s, t, _) => InlineElements.Apply(s, t));
        Register("ins", (s, t, _) => InlineElements.Apply(s, t));
        Register("code", (s, t, _) => InlineElements.Apply(s, t));
        Register("kbd", (s, t, _) => InlineElements.Apply(s, t));
        Register("samp", (s, t, _) => InlineElements.Apply(s, t));
        Register("var", (s, t, _) => InlineElements.Apply(s, t));
        Register("small", (s, t, _) => InlineElements.Apply(s, t));
        Register("sub", (s, t, _) => InlineElements.Apply(s, t));
        Register("sup", (s, t, _) => InlineElements.Apply(s, t));
        Register("mark", (s, t, _) => InlineElements.Apply(s, t));
        Register("time", (s, t, _) => InlineElements.Apply(s, t));
        Register("abbr", (s, t, _) => InlineElements.Apply(s, t));
        Register("acronym", (s, t, _) => InlineElements.Apply(s, t));
        Register("cite", (s, t, _) => InlineElements.Apply(s, t));
        Register("q", (s, t, _) => InlineElements.Apply(s, t));
        Register("dfn", (s, t, _) => InlineElements.Apply(s, t));
        Register("br", (s, t, _) => InlineElements.Apply(s, t));
        Register("wbr", (s, t, _) => InlineElements.Apply(s, t));
        Register("label", (s, t, _) => FormElements.Apply(s, t));
        Register("output", (s, t, _) => InlineElements.Apply(s, t));
        Register("data", (s, t, _) => InlineElements.Apply(s, t));

        Register("input", (s, t, e) => FormElements.Apply(s, t, e));
        Register("textarea", (s, t, e) => FormElements.Apply(s, t, e));
        Register("select", (s, t, e) => FormElements.Apply(s, t, e));
        Register("button", (s, t, e) => FormElements.Apply(s, t, e));
        Register("fieldset", (s, t, _) => FormElements.Apply(s, t));
        Register("legend", (s, t, _) => FormElements.Apply(s, t));
        Register("datalist", (s, t, _) => FormElements.Apply(s, t));
        Register("optgroup", (s, t, _) => FormElements.Apply(s, t));
        Register("option", (s, t, _) => FormElements.Apply(s, t));
        Register("progress", (s, t, _) => FormElements.Apply(s, t));
        Register("meter", (s, t, _) => FormElements.Apply(s, t));

        Register("img", (s, t, _) => MediaElements.Apply(s, t));
        Register("picture", (s, t, _) => MediaElements.Apply(s, t));
        Register("canvas", (s, t, _) => MediaElements.Apply(s, t));
        Register("svg", (s, t, _) => MediaElements.Apply(s, t));
        Register("video", (s, t, _) => MediaElements.Apply(s, t));
        Register("audio", (s, t, _) => MediaElements.Apply(s, t));
        Register("source", (s, t, _) => MediaElements.Apply(s, t));
        Register("track", (s, t, _) => MediaElements.Apply(s, t));
        Register("iframe", (s, t, _) => MediaElements.Apply(s, t));
        Register("embed", (s, t, _) => MediaElements.Apply(s, t));
        Register("object", (s, t, _) => MediaElements.Apply(s, t));
        Register("param", (s, t, _) => MediaElements.Apply(s, t));
        Register("map", (s, t, _) => MediaElements.Apply(s, t));
        Register("area", (s, t, _) => MediaElements.Apply(s, t));
    }

    private static void Register(string tagName, Action<ComputedStyle, string, Element?> applyStyle)
    {
        _elementStyles[tagName.ToLowerInvariant()] = applyStyle;
    }

    public static void ApplyUserAgentStyle(ComputedStyle style, string tagName, Element? element = null)
    {
        var key = tagName.ToLowerInvariant();
        if (_elementStyles.TryGetValue(key, out var applyStyle))
        {
            applyStyle(style, key, element);
        }
    }

    public static bool IsRegistered(string tagName)
    {
        return _elementStyles.ContainsKey(tagName.ToLowerInvariant());
    }

    public static int RegisteredCount => _elementStyles.Count;
}