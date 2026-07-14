using UpBrowser.Core.Dom;
using AngleSharp;

namespace UpBrowser.Core.JavaScript;

public static class HtmlParser
{
    public static List<Node> ParseFragment(string html, string parentTagName)
    {
        var result = new List<Node>();
        if (string.IsNullOrWhiteSpace(html)) return result;

        try
        {
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var doc = context.OpenAsync(req => req.Content($"<html><body>{html}</body></html>"))
                .GetAwaiter().GetResult();
            if (doc.Body == null) return result;

            foreach (var child in doc.Body.ChildNodes)
                ConvertFromAngleSharp(child, result);
        }
        catch
        {
            result.Add(new TextNode(html));
        }

        return result;
    }

    private static void ConvertFromAngleSharp(AngleSharp.Dom.INode node, List<Node> result)
    {
        if (node is AngleSharp.Dom.IText textNode)
        {
            var text = textNode.TextContent ?? "";
            result.Add(new TextNode(text));
        }
        else if (node is AngleSharp.Dom.IElement element)
        {
            var el = new HtmlElement(element.LocalName);
            foreach (var attr in element.Attributes)
                el.Attributes[attr.Name] = attr.Value;
            result.Add(el);
            foreach (var child in element.ChildNodes)
                ConvertFromAngleSharp(child, el.Children);
        }
    }
}
