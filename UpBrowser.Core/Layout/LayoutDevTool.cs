using UpBrowser.Core.Dom;
using SkiaSharp;
using System.Text;

namespace UpBrowser.Core.Layout;

public class LayoutDevTool
{
    private readonly StringBuilder _sb = new();
    private int _indent = 0;

    public string GenerateReport(Document document, float viewportWidth, float viewportHeight)
    {
        _sb.Clear();
        _indent = 0;

        _sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
        _sb.AppendLine("║                    UpBrowser Layout DevTool                      ║");
        _sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
        _sb.AppendLine();
        _sb.AppendLine($"📐 Viewport: {viewportWidth} x {viewportHeight}");
        _sb.AppendLine();

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
        {
            _sb.AppendLine("🌳 DOM Tree:");
            DumpDomTree(root, 0);
            _sb.AppendLine();

            _sb.AppendLine("📦 Layout Boxes:");
            DumpLayoutBoxes(root);
        }

        return _sb.ToString();
    }

    private void DumpDomTree(Element element, int depth)
    {
        var indent = new string(' ', depth * 2);
        var style = element.ComputedStyle;
        var display = style?.Display.ToString().ToLowerInvariant() ?? "none";
        var pos = style?.Position.ToString().ToLowerInvariant() ?? "static";
        string boxSizing = "content-box";
        if (style != null)
        {
            boxSizing = style.BoxSizing == BoxSizingType.BorderBox ? "border-box" : "content-box";
        }

        _sb.AppendLine($"{indent}├─ <{element.TagName}> id=\"{element.Id ?? ""}\" class=\"{element.ClassName ?? ""}\"");
        _sb.AppendLine($"{indent}│  display={display}, position={pos}, boxSizing={boxSizing}");

        if (style != null)
        {
            _sb.AppendLine($"{indent}│  width={style.Width} height={style.Height}");
            _sb.AppendLine($"{indent}│  margin={FormatLength(style.MarginTop)}/{FormatLength(style.MarginRight)}/{FormatLength(style.MarginBottom)}/{FormatLength(style.MarginLeft)}");
            _sb.AppendLine($"{indent}│  padding={FormatLength(style.PaddingTop)}/{FormatLength(style.PaddingRight)}/{FormatLength(style.PaddingBottom)}/{FormatLength(style.PaddingLeft)}");
            var lineH = style.LineHeight == 1.2f ? "normal" : $"{style.LineHeight}px";
            _sb.AppendLine($"{indent}│  lineHeight={lineH}");
        }

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                DumpDomTree(childElement, depth + 1);
            }
            else if (child is TextNode textNode && !textNode.IsWhitespaceOnly)
            {
                _sb.AppendLine($"{indent}│  └─ \"{textNode.TextContent?.Truncate(30)}\"");
            }
        }
    }

    private void DumpLayoutBoxes(Element element)
    {
        var box = element.LayoutBox;
        if (box == null)
        {
            _sb.AppendLine($"⚠️ <{element.TagName}> has NO layout box!");
            return;
        }

        Indent();
        _sb.AppendLine($"📦 <{element.TagName}>");
        Indent();
        _sb.AppendLine($"   MarginBox:    {FormatRect(box.MarginBox)}");
        _sb.AppendLine($"   BorderBox:   {FormatRect(box.BorderBox)}");
        _sb.AppendLine($"   PaddingBox:  {FormatRect(box.PaddingBox)}");
        _sb.AppendLine($"   ContentBox:  {FormatRect(box.ContentBox)}");
        _sb.AppendLine($"   LineHeight:  {box.LineHeight}");

        if (box.Children.Count > 0)
        {
            Indent();
            _sb.AppendLine($"   Children ({box.Children.Count}):");
            foreach (var child in box.Children)
            {
                var childElem = FindElementByBox(child);
                if (childElem != null)
                {
                    Indent();
                    _sb.AppendLine($"     - <{childElem.TagName}> @ {FormatRect(child.MarginBox)}");
                }
            }
        }

        if (box.Lines?.Count > 0)
        {
            Indent();
            _sb.AppendLine($"   Lines ({box.Lines.Count}):");
            for (int i = 0; i < box.Lines.Count; i++)
            {
                var line = box.Lines[i];
                Indent();
                _sb.AppendLine($"     Line {i}: Y={line.Y:F1} Baseline={line.Baseline:F1} Height={line.Height:F1} Runs={line.Runs.Count}");
                foreach (var run in line.Runs)
                {
                    Indent();
                    _sb.AppendLine($"       \"{run.Text.Truncate(20)}\" Width={run.Width:F1}");
                }
            }
        }

        if (box.LineRuns?.Count > 0)
        {
            Indent();
            _sb.AppendLine($"   LineRuns ({box.LineRuns.Count}):");
            foreach (var run in box.LineRuns)
            {
                Indent();
                _sb.AppendLine($"     \"{run.Text.Truncate(30)}\" Width={run.Width:F1} Height={run.Height:F1}");
            }
        }

        Unindent();
        Unindent();

        foreach (var child in element.Children.OfType<Element>())
        {
            DumpLayoutBoxes(child);
        }
    }

    private Element? FindElementByBox(LayoutBox box)
    {
        return null;
    }

    private string FormatRect(SKRect rect)
    {
        return $"L={rect.Left:F1}, T={rect.Top:F1}, R={rect.Right:F1}, B={rect.Bottom:F1} (W={rect.Width:F1}, H={rect.Height:F1})";
    }

    private static string FormatLength(Length? length)
    {
        return length?.ToCssString() ?? "0px";
    }

    private void Indent() => _sb.Append("  ");
    private void Unindent() { }

    public string GenerateQuickReport(Document document)
    {
        _sb.Clear();
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return "No root element";

        CountElements(root, out int total, out int block, out int inline, out int flex);

        _sb.AppendLine($"Elements: {total} (Block: {block}, Inline: {inline}, Flex: {flex})");

        var body = document.Body;
        if (body?.LayoutBox != null)
        {
            _sb.AppendLine($"Body box: {FormatRect(body.LayoutBox.ContentBox)}");
        }

        return _sb.ToString();
    }

    private void CountElements(Element element, out int total, out int block, out int inline, out int flex)
    {
        total = 1;
        block = 0;
        inline = 0;
        flex = 0;

        var style = element.ComputedStyle;
        if (style != null)
        {
            switch (style.Display)
            {
                case DisplayType.Block:
                case DisplayType.ListItem:
                    block++;
                    break;
                case DisplayType.Inline:
                case DisplayType.InlineBlock:
                    inline++;
                    break;
                case DisplayType.Flex:
                case DisplayType.InlineFlex:
                    flex++;
                    break;
            }
        }

        foreach (var child in element.Children.OfType<Element>())
        {
            CountElements(child, out int ct, out int cb, out int ci, out int cf);
            total += ct;
            block += cb;
            inline += ci;
            flex += cf;
        }
    }
}

public static class StringExtensions
{
    public static string Truncate(this string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= maxLength ? s : s[..maxLength] + "...";
    }
}