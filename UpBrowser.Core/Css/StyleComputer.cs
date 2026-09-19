using UpBrowser.Core.Dom;
using UpBrowser.Core.Css.Resolver;

namespace UpBrowser.Core.Css;

/// <summary>
/// StyleComputer - public API for computing styles on a document.
/// Delegates to CascadeResolver for the actual cascade resolution.
/// Maintains backward compatibility with the existing API.
/// </summary>
public class StyleComputer
{
    private readonly CascadeResolver _resolver = new();
    private readonly List<Stylesheet> _stylesheets = new();

    public void AddStylesheet(Stylesheet stylesheet)
    {
        _stylesheets.Add(stylesheet);
        _resolver.AddStylesheet(stylesheet);
    }

    public void ComputeStyles(Document document, float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light")
    {
        _resolver.ResolveStyles(document, viewportWidth, viewportHeight, colorScheme);
    }

    /// <summary>
    /// True when any registered stylesheet contains a <c>:hover</c> selector.
    /// Cheap string scan (selector text); the host uses it to decide whether a
    /// mouse hover change can affect computed styles at all — when no :hover rule
    /// exists, hovering needs no style recompute / relayout.
    /// </summary>
    public bool HasHoverRules()
    {
        foreach (var sheet in _stylesheets)
        {
            if (SheetHasHoverRules(sheet)) return true;
        }
        return false;
    }

    private static bool SheetHasHoverRules(Stylesheet sheet)
    {
        if (RulesHaveHover(sheet.Rules)) return true;
        foreach (var m in sheet.MediaRules)
            if (RulesHaveHover(m.Rules)) return true;
        foreach (var s in sheet.SupportsRules)
            if (RulesHaveHover(s.Rules)) return true;
        foreach (var l in sheet.LayerRules)
            if (RulesHaveHover(l.Rules)) return true;
        return false;
    }

    private static bool RulesHaveHover(List<CssRule> rules)
    {
        foreach (var r in rules)
        {
            if (r.Selector.Contains(":hover", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parse inline style string into property dictionary.
    /// </summary>
    public Dictionary<string, string> ParseInlineStyle(string styleText)
    {
        var parser = new CssParser();
        return parser.ParseInlineStyle(styleText);
    }
}
