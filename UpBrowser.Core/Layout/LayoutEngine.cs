using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;
using UpBrowser.Core.Layout.Grid;
using SkiaSharp;
using System.Text;
using UpBrowser.Core.Css;

namespace UpBrowser.Core.Layout;

public static class LayoutMath
{
    public static float RoundToDevicePixel(float value, float dpiScale = 1.0f)
    {
        if (dpiScale <= 0) dpiScale = 1.0f;
        float physical = value * dpiScale;
        float roundedPhysical = MathF.Round(physical);
        return roundedPhysical / dpiScale;
    }

    public static SKRect RoundRect(SKRect rect, float dpiScale = 1.0f)
    {
        return new SKRect(
            RoundToDevicePixel(rect.Left, dpiScale),
            RoundToDevicePixel(rect.Top, dpiScale),
            RoundToDevicePixel(rect.Right, dpiScale),
            RoundToDevicePixel(rect.Bottom, dpiScale)
        );
    }
}

/// <summary>
/// LayoutEngine is the single layout engine of UpBrowser. It drives the
/// standards-conforming (CSS/W3C, aligned with modern engines) layout pipeline:
/// a tree of <see cref="BlockLayoutAlgorithm"/> / <see cref="InlineLayoutAlgorithm"/>
/// / <see cref="FlexLayoutAlgorithm"/> / grid / table algorithms built from a
/// <see cref="ConstraintSpace"/>, whose resulting fragment tree is converted
/// into the box model consumed by the painting layer.
///
/// Previously this class also hosted a second, older, hand-written box-building
/// recursion (<c>CreateLayoutBox</c>). That legacy path was non-conformant,
/// oversimplified and incomplete, so it has been removed. There is a single
/// layout path: the modern one below.
/// </summary>
public class LayoutEngine
{
    private float _viewportWidth;
    private float _viewportHeight;
    private float _contentHeight;
    private float _rootFontSize = 16;
    private float _dpiScale = 1.0f;

    public float ViewportWidth => _viewportWidth;
    public float ViewportHeight => _viewportHeight;
    public float RootFontSize => _rootFontSize;
    public float DpiScale => _dpiScale;
    public float ContentHeight => _contentHeight;

    /// <summary>
    /// Push viewport / device / root-font state onto this engine instance without
    /// running a layout pass. The unit-resolution context (vw/vh/rem/DPI) is
    /// established here; every layout entry calls this first.
    /// </summary>
    public void SyncPipelineState(float viewportWidth, float viewportHeight, float dpiScale, float rootFontSize)
    {
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0f;
        _rootFontSize = rootFontSize > 0 ? rootFontSize : 16f;
    }

    /// <summary>
    /// Layout entry point. Runs the modern pipeline (<see cref="LayoutAurora"/>).
    /// </summary>
    public void Layout(Document document, float width, float height, float dpiScale = 1.0f)
    {
        LayoutAurora(document, width, height, dpiScale);
    }

    /// <summary>
    /// Runs the modern layout pipeline (BlockLayoutAlgorithm on the root) and
    /// converts the result into box-model values via
    /// <see cref="AuroraFragmentConverter"/>. This is the one and only box
    /// construction path in the engine.
    /// </summary>
    public void LayoutAurora(Document document, float width, float height, float dpiScale = 1.0f)
    {
        var sw = UpBrowser.Core.Performance.Clock.NowNanos();
        _viewportWidth = width;
        _viewportHeight = height;
        _dpiScale = dpiScale;
        _contentHeight = 0;

        var root = document.DocumentElement ?? document.Body;
        if (root == null)
        {
            UpBrowser.Core.Performance.PipelineTimings.Layout.AddSample(UpBrowser.Core.Performance.Clock.NowNanos() - sw);
            return;
        }

        // Save scroll state BEFORE ClearLayoutBoxes destroys the boxes.
        // Without this, every relayout resets ScrollY to 0 and inner scroll
        // containers can never hold a position.
        var savedScroll = new Dictionary<Element, Dom.LayoutBox>();
        SaveScrollContainers(root, savedScroll);

        ClearLayoutBoxes(root);

        // Generate ::before / ::after pseudo-element content for all elements
        // before layout runs, so the pseudo-elements are in the DOM tree when the
        // layout algorithm processes them.
        GeneratePseudoElementsForTree(root);

        // Unit-resolution context: rem resolves against the ROOT ELEMENT's
        // computed font-size (CSS spec), vw/vh against the viewport established
        // by SyncPipelineState/Layout entry above.
        float rootFontSize = root.ComputedStyle?.FontSize ?? _rootFontSize;
        if (rootFontSize <= 0) rootFontSize = ConstraintSpace.DefaultRootFontSize;
        var space = ConstraintSpace.Builder(width, height)
            .SetIsNewFormattingContext(true)
            .SetBfcBlockOffset(0)
            .SetForcedBfcBlockOffset(0)
            .SetRootFontSize(rootFontSize)
            .SetViewportSize(_viewportWidth, _viewportHeight)
            .ToConstraintSpace();
        var result = new BlockLayoutAlgorithm(root, space).Layout();
        var rootBox = AuroraFragmentConverter.ToLayoutBox(result.Fragment, root);
        if (rootBox != null)
        {
            root.LayoutBox = rootBox;
            AssignLayoutBox(root, rootBox);

            // Restore scroll state AFTER AssignLayoutBox has populated
            // element.LayoutBox for all children. Restoring before would write
            // to null references and silently lose the scroll position.
            foreach (var kv in savedScroll)
            {
                if (kv.Key.LayoutBox is { } nb)
                {
                    nb.ScrollX = kv.Value.ScrollX;
                    nb.ScrollY = kv.Value.ScrollY;
                    nb.TargetScrollX = kv.Value.TargetScrollX;
                    nb.TargetScrollY = kv.Value.TargetScrollY;
                    nb.IsSmoothScrollingX = kv.Value.IsSmoothScrollingX;
                    nb.IsSmoothScrollingY = kv.Value.IsSmoothScrollingY;
                    nb.ScrollVelX = kv.Value.ScrollVelX;
                    nb.ScrollVelY = kv.Value.ScrollVelY;
                }
            }
            savedScroll.Clear();

            CalculateContentHeight(rootBox);
        }
        UpBrowser.Core.Performance.PipelineTimings.Layout.AddSample(UpBrowser.Core.Performance.Clock.NowNanos() - sw);
    }

    /// <summary>Recursively collect LayoutBoxes of scroll containers.</summary>
    private static void SaveScrollContainers(Element element, Dictionary<Element, Dom.LayoutBox> into)
    {
        if (element.LayoutBox is { IsScrollContainer: true } b)
            into[element] = b;
        foreach (var child in element.Children)
            if (child is Element ce)
                SaveScrollContainers(ce, into);
    }

    private void GeneratePseudoElementsForTree(Element element)
    {
        // Generate pseudo-elements for this element (needs a dummy box just for
        // the style reference — the actual layout box will be created later).
        if (element.ComputedStyle != null)
        {
            var dummy = new LayoutBox();
            GeneratePseudoElementContent(element, dummy, element.ComputedStyle);
        }
        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                GeneratePseudoElementsForTree(childEl);
        }
    }

    private void AssignLayoutBox(Element element, Dom.LayoutBox box)
    {
        element.LayoutBox = box;
        foreach (var child in element.Children)
        {
            if (child is not Element childEl) continue;
            // Search the box subtree, not just direct children: table layout nests
            // cells under anonymous section/row boxes that have no DOM element, so
            // a cell's box is a grandchild of the table's box. A direct-child-only
            // match would leave every cell without a LayoutBox (and unpainted).
            var childBox = FindBoxForElement(box, childEl);
            childEl.LayoutBox = childBox;
            if (childBox != null)
                AssignLayoutBox(childEl, childBox);
        }
    }

    /// <summary>
    /// Find the layout box produced for <paramref name="target"/> within
    /// <paramref name="box"/>'s subtree, preferring a direct child and otherwise
    /// descending through anonymous boxes (e.g. table sections). Elements are
    /// unique, so a subtree match is unambiguous.
    /// </summary>
    private static Dom.LayoutBox? FindBoxForElement(Dom.LayoutBox box, Element target)
    {
        foreach (var c in box.Children)
        {
            if (c.Dimensions?.Element == target)
                return c;
        }
        foreach (var c in box.Children)
        {
            var found = FindBoxForElement(c, target);
            if (found != null)
                return found;
        }
        return null;
    }

    private void ClearLayoutBoxes(Element element)
    {
        element.LayoutBox = null;
        foreach (var child in element.Children)
        {
            if (child is Element childElement)
                ClearLayoutBoxes(childElement);
        }
    }

    private void CalculateContentHeight(LayoutBox box)
    {
        foreach (var child in box.Children)
            CalculateContentHeight(child);
        if (box.MarginBox.Bottom > _contentHeight)
            _contentHeight = box.MarginBox.Bottom;
    }

    private void GeneratePseudoElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        if (element.BeforeStyles != null && element.BeforeStyles.TryGetValue("content", out var beforeContent) && !element.HasGeneratedBefore)
        {
            var result = BuildPseudoElement(element, style, beforeContent, isBefore: true);
            if (result is Element el)
            {
                element.Children.Insert(0, el);
                element.HasGeneratedBefore = true;
            }
        }

        if (element.AfterStyles != null && element.AfterStyles.TryGetValue("content", out var afterContent) && !element.HasGeneratedAfter)
        {
            var result = BuildPseudoElement(element, style, afterContent, isBefore: false);
            if (result is Element el)
            {
                element.Children.Add(el);
                element.HasGeneratedAfter = true;
            }
        }
    }

    private static Node? BuildPseudoElement(Element parent, ComputedStyle parentStyle, string rawContent, bool isBefore)
    {
        var props = isBefore ? parent.BeforeStyles : parent.AfterStyles;
        if (props == null) return null;

        var content = DecodeCssContent(rawContent);
        if (content == "none" || content == null) return null;

        // Build a ComputedStyle by cloning the parent and applying ::before/::after props.
        var pseudoStyle = parentStyle.Clone();

        // Apply display (default for ::before/::after is 'inline').
        string displayStr = "inline";
        if (props.TryGetValue("display", out var d))
            displayStr = d;
        pseudoStyle.Display = displayStr.ToLowerInvariant() switch
        {
            "block" => DisplayType.Block,
            "flex" => DisplayType.Flex,
            "inline-flex" => DisplayType.InlineFlex,
            "grid" => DisplayType.Grid,
            "inline-grid" => DisplayType.InlineGrid,
            "table" => DisplayType.Table,
            "inline-block" => DisplayType.InlineBlock,
            "list-item" => DisplayType.ListItem,
            _ => DisplayType.Inline,
        };

        // Apply the remaining pseudo-element properties.
        foreach (var kv in props)
        {
            if (kv.Key == "content" || kv.Key == "display") continue;
            ApplyPseudoProperty(pseudoStyle, kv.Key, kv.Value);
        }

        // Create the Element. Even for inline content we need a real Element
        // so that the pseudo-element's own styles (color, font-weight, etc.) are
        // applied — a bare TextNode would inherit the parent's style and ignore
        // the ::before/::after declarations.
        var pseudoEl = new HtmlElement("pseudo-" + (isBefore ? "before" : "after"))
        {
            ComputedStyle = pseudoStyle,
            Parent = parent,
        };

        // Add the text content as a child text node.
        if (!string.IsNullOrEmpty(content))
        {
            var textNode = new TextNode(content);
            textNode.Parent = pseudoEl;
            pseudoEl.Children.Add(textNode);
        }

        return pseudoEl;
    }

    private static void ApplyPseudoProperty(ComputedStyle style, string name, string value)
    {
        var lower = name.ToLowerInvariant();
        try
        {
            switch (lower)
            {
                case "width": style.Width = Length.Parse(value); break;
                case "height": style.Height = Length.Parse(value); break;
                case "min-width": style.MinWidth = Length.Parse(value); break;
                case "min-height": style.MinHeight = Length.Parse(value); break;
                case "max-width": style.MaxWidth = Length.Parse(value); break;
                case "max-height": style.MaxHeight = Length.Parse(value); break;
                case "margin": ParseShorthand4(value, out var mt, out var mr, out var mb, out var ml);
                    style.MarginTop = mt; style.MarginRight = mr; style.MarginBottom = mb; style.MarginLeft = ml; break;
                case "margin-top": style.MarginTop = Length.Parse(value); break;
                case "margin-right": style.MarginRight = Length.Parse(value); break;
                case "margin-bottom": style.MarginBottom = Length.Parse(value); break;
                case "margin-left": style.MarginLeft = Length.Parse(value); break;
                case "padding": ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                    style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl; break;
                case "padding-top": style.PaddingTop = Length.Parse(value); break;
                case "padding-right": style.PaddingRight = Length.Parse(value); break;
                case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
                case "padding-left": style.PaddingLeft = Length.Parse(value); break;
                case "color": style.Color = ColorParser.Parse(value); break;
                case "background-color": style.BackgroundColor = ColorParser.Parse(value); break;
                case "background-image": style.BackgroundImage = new List<string> { value }; break;
                case "position": style.Position = ParsePseudoPosition(value); break;
                case "top": style.Top = Length.Parse(value); break;
                case "right": style.Right = Length.Parse(value); break;
                case "bottom": style.Bottom = Length.Parse(value); break;
                case "left": style.Left = Length.Parse(value); break;
                case "float": style.Float = ParsePseudoFloat(value); break;
                case "clear": style.Clear = ParsePseudoClear(value); break;
                case "z-index": style.ZIndex = int.TryParse(value, out var zi) ? zi : 0; break;
                case "opacity": style.Opacity = float.TryParse(value, out var op) ? op : 1; break;
                case "overflow": style.Overflow = ParsePseudoOverflow(value); break;
                case "text-align": style.TextAlign = ParsePseudoTextAlign(value); break;
                case "font-size": style.FontSize = ParsePseudoFontSize(value); break;
                case "font-family": style.FontFamily = value; break;
                case "font-weight": style.FontWeight = (FontWeight)(int.TryParse(value, out var fw) ? fw : 400); break;
                case "border": ParsePseudoBorder(style, value); break;
                case "border-radius": ParsePseudoBorderRadius(style, value); break;
                case "box-shadow": ParsePseudoBoxShadow(style, value); break;
                case "background": style.BackgroundColor = ColorParser.Parse(value); break;
            }
        }
        catch { /* ignore invalid property values */ }
    }

    private static void ParseShorthand4(string value, out Length? v1, out Length? v2, out Length? v3, out Length? v4)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var p = parts.Select(Length.Parse).ToList();
        v1 = p.Count > 0 ? p[0] : null;
        v2 = p.Count > 1 ? p[1] : v1;
        v3 = p.Count > 2 ? p[2] : v1;
        v4 = p.Count > 3 ? p[3] : (p.Count > 1 ? p[1] : v1);
    }

    private static PositionType ParsePseudoPosition(string v) => v.ToLowerInvariant() switch
    {
        "absolute" => PositionType.Absolute, "fixed" => PositionType.Fixed,
        "relative" => PositionType.Relative, "sticky" => PositionType.Sticky,
        _ => PositionType.Static
    };
    private static FloatType ParsePseudoFloat(string v) => v.ToLowerInvariant() switch
    {
        "left" => FloatType.Left, "right" => FloatType.Right, _ => FloatType.None
    };
    private static ClearType ParsePseudoClear(string v) => v.ToLowerInvariant() switch
    {
        "left" => ClearType.Left, "right" => ClearType.Right, "both" => ClearType.Both, _ => ClearType.None
    };
    private static OverflowType ParsePseudoOverflow(string v) => v.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden, "scroll" => OverflowType.Scroll, "auto" => OverflowType.Auto, _ => OverflowType.Visible
    };
    private static TextAlignType ParsePseudoTextAlign(string v) => v.ToLowerInvariant() switch
    {
        "left" => TextAlignType.Left, "right" => TextAlignType.Right, "center" => TextAlignType.Center, "justify" => TextAlignType.Justify, _ => TextAlignType.Start
    };
    private static float ParsePseudoFontSize(string v)
    {
        if (v.EndsWith("px") && float.TryParse(v[..^2], out var px)) return px;
        if (v.EndsWith("em") && float.TryParse(v[..^2], out var em)) return em * 16;
        if (v.EndsWith("rem") && float.TryParse(v[..^2], out var rem)) return rem * 16;
        if (float.TryParse(v, out var f)) return f;
        return 16;
    }

    private static void ParsePseudoBorder(ComputedStyle style, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.EndsWith("px") && float.TryParse(p[..^2], out var w))
            { style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = w; }
            else if (p is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset")
            { var bs = p switch { "solid" => BorderStyle.Solid, "dashed" => BorderStyle.Dashed, "dotted" => BorderStyle.Dotted, "double" => BorderStyle.Double, "groove" => BorderStyle.Groove, "ridge" => BorderStyle.Ridge, "inset" => BorderStyle.Inset, "outset" => BorderStyle.Outset, _ => BorderStyle.Solid };
                style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = bs; }
            else
            { var c = ColorParser.Parse(p); style.BorderTopColor = style.BorderRightColor = style.BorderBottomColor = style.BorderLeftColor = c; }
        }
    }

    private static void ParsePseudoBorderRadius(ComputedStyle style, string value)
    {
        var radii = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var r = radii.Select(v =>
        {
            if (v.EndsWith("px") && float.TryParse(v[..^2], out var px)) return px;
            if (float.TryParse(v, out var f)) return f;
            return 0f;
        }).ToList();
        style.BorderTopLeftRadius = r.Count > 0 ? r[0] : 0;
        style.BorderTopRightRadius = r.Count > 1 ? r[1] : r[0];
        style.BorderBottomRightRadius = r.Count > 2 ? r[2] : r[0];
        style.BorderBottomLeftRadius = r.Count > 3 ? r[3] : (r.Count > 0 ? r[0] : 0);
    }

    private static void ParsePseudoBoxShadow(ComputedStyle style, string value)
    {
        // Simplified: single shadow only.
        if (value == "none") return;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool inset = false;
        int idx = 0;
        if (parts[0] == "inset") { inset = true; idx++; }
        if (idx + 1 >= parts.Length) return;
        float.TryParse(parts[idx].TrimEnd('p', 'x'), out var ox);
        float.TryParse(parts[idx + 1].TrimEnd('p', 'x'), out var oy);
        idx += 2;
        float br = 0, sp = 0;
        if (idx < parts.Length && parts[idx].Contains('x')) { float.TryParse(parts[idx].TrimEnd('p', 'x'), out br); idx++; }
        if (idx < parts.Length && parts[idx].Contains('x')) { float.TryParse(parts[idx].TrimEnd('p', 'x'), out sp); idx++; }
        var color = idx < parts.Length ? ColorParser.Parse(string.Join(" ", parts.Skip(idx))) : new SKColor(0, 0, 0, 80);
        style.BoxShadow = new List<BoxShadowValue> { new BoxShadowValue(color, ox, oy, br, sp, inset) };
    }

    private static string DecodeCssContent(string content)
    {
        // Handle attr(...) — extract attribute value from the element.
        // The element context is passed only at generation time; the raw
        // attr() text is left as-is and resolved later by the caller.
        var trimmed = content.Trim();
        if (trimmed.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            // Extract the attribute name: attr(data-*) → data-*
            var attrName = trimmed[5..^1].Trim();
            return "attr(" + attrName + ")";
        }

        // Handle url(...) — image references.
        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")"))
        {
            // Keep the url as-is for the image loader.
            return trimmed;
        }

        content = content.Trim('"', '\'');
        if (string.IsNullOrEmpty(content)) return content;

        // Decode CSS unicode escapes: \201C -> "
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                // Read hex digits for CSS unicode escape
                int hexStart = i + 1;
                int hexEnd = hexStart;
                while (hexEnd < content.Length && char.IsLetterOrDigit(content[hexEnd]) && hexEnd - hexStart < 6)
                    hexEnd++;
                if (hexEnd > hexStart)
                {
                    var hexStr = content[hexStart..hexEnd];
                    if (int.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
                    {
                        result.Append((char)codePoint);
                        i = hexEnd - 1;
                        continue;
                    }
                }
                result.Append(content[i]);
            }
            else
            {
                result.Append(content[i]);
            }
        }
        return result.ToString();
    }
}
