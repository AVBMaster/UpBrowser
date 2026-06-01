using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class ElementStyleRegistry
{
    private static readonly Dictionary<string, Action<ComputedStyle, string>> _elementStyles = new();

    static ElementStyleRegistry()
    {
        RegisterAllElements();
    }

    private static void RegisterAllElements()
    {
        Register("html", RootElements.Apply);
        Register("body", RootElements.Apply);
        Register("head", RootElements.Apply);
        Register("title", RootElements.Apply);
        Register("meta", RootElements.Apply);
        Register("link", RootElements.Apply);
        Register("style", RootElements.Apply);
        Register("script", RootElements.Apply);
        Register("base", RootElements.Apply);
        Register("noscript", RootElements.Apply);
        Register("template", RootElements.Apply);

        Register("div", BlockElements.Apply);
        Register("p", BlockElements.Apply);
        Register("h1", BlockElements.Apply);
        Register("h2", BlockElements.Apply);
        Register("h3", BlockElements.Apply);
        Register("h4", BlockElements.Apply);
        Register("h5", BlockElements.Apply);
        Register("h6", BlockElements.Apply);
        Register("ul", BlockElements.Apply);
        Register("ol", BlockElements.Apply);
        Register("li", BlockElements.Apply);
        Register("dl", BlockElements.Apply);
        Register("dt", BlockElements.Apply);
        Register("dd", BlockElements.Apply);
        Register("table", BlockElements.Apply);
        Register("thead", BlockElements.Apply);
        Register("tbody", BlockElements.Apply);
        Register("tfoot", BlockElements.Apply);
        Register("tr", BlockElements.Apply);
        Register("th", BlockElements.Apply);
        Register("td", BlockElements.Apply);
        Register("caption", BlockElements.Apply);
        Register("pre", BlockElements.Apply);
        Register("blockquote", BlockElements.Apply);
        Register("address", BlockElements.Apply);
        Register("article", BlockElements.Apply);
        Register("aside", BlockElements.Apply);
        Register("main", BlockElements.Apply);
        Register("section", BlockElements.Apply);
        Register("nav", BlockElements.Apply);
        Register("header", BlockElements.Apply);
        Register("footer", BlockElements.Apply);
        Register("figure", BlockElements.Apply);
        Register("figcaption", BlockElements.Apply);
        Register("details", BlockElements.Apply);
        Register("summary", BlockElements.Apply);
        Register("dialog", BlockElements.Apply);
        Register("hr", BlockElements.Apply);
        Register("search", BlockElements.Apply);
        Register("hgroup", BlockElements.Apply);

        Register("span", InlineElements.Apply);
        Register("a", InlineElements.Apply);
        Register("strong", InlineElements.Apply);
        Register("b", InlineElements.Apply);
        Register("em", InlineElements.Apply);
        Register("i", InlineElements.Apply);
        Register("u", InlineElements.Apply);
        Register("s", InlineElements.Apply);
        Register("del", InlineElements.Apply);
        Register("ins", InlineElements.Apply);
        Register("code", InlineElements.Apply);
        Register("kbd", InlineElements.Apply);
        Register("samp", InlineElements.Apply);
        Register("var", InlineElements.Apply);
        Register("small", InlineElements.Apply);
        Register("sub", InlineElements.Apply);
        Register("sup", InlineElements.Apply);
        Register("mark", InlineElements.Apply);
        Register("time", InlineElements.Apply);
        Register("abbr", InlineElements.Apply);
        Register("acronym", InlineElements.Apply);
        Register("cite", InlineElements.Apply);
        Register("q", InlineElements.Apply);
        Register("dfn", InlineElements.Apply);
        Register("br", InlineElements.Apply);
        Register("wbr", InlineElements.Apply);
        Register("label", InlineElements.Apply);
        Register("output", InlineElements.Apply);
        Register("progress", InlineElements.Apply);
        Register("meter", InlineElements.Apply);
        Register("data", InlineElements.Apply);

        Register("input", FormElements.Apply);
        Register("textarea", FormElements.Apply);
        Register("select", FormElements.Apply);
        Register("button", FormElements.Apply);
        Register("fieldset", FormElements.Apply);
        Register("legend", FormElements.Apply);
        Register("datalist", FormElements.Apply);
        Register("optgroup", FormElements.Apply);
        Register("option", FormElements.Apply);

        Register("img", MediaElements.Apply);
        Register("picture", MediaElements.Apply);
        Register("canvas", MediaElements.Apply);
        Register("svg", MediaElements.Apply);
        Register("video", MediaElements.Apply);
        Register("audio", MediaElements.Apply);
        Register("source", MediaElements.Apply);
        Register("track", MediaElements.Apply);
        Register("iframe", MediaElements.Apply);
        Register("embed", MediaElements.Apply);
        Register("object", MediaElements.Apply);
        Register("param", MediaElements.Apply);
        Register("map", MediaElements.Apply);
        Register("area", MediaElements.Apply);
    }

    private static void Register(string tagName, Action<ComputedStyle, string> applyStyle)
    {
        _elementStyles[tagName.ToLowerInvariant()] = applyStyle;
    }

    public static void ApplyUserAgentStyle(ComputedStyle style, string tagName)
    {
        var key = tagName.ToLowerInvariant();
        if (_elementStyles.TryGetValue(key, out var applyStyle))
        {
            applyStyle(style, key);
        }
    }

    public static bool IsRegistered(string tagName)
    {
        return _elementStyles.ContainsKey(tagName.ToLowerInvariant());
    }

    public static int RegisteredCount => _elementStyles.Count;
}