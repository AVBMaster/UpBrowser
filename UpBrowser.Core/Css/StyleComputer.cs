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

    public void ComputeStyles(Document document)
    {
        _resolver.ResolveStyles(document);
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
