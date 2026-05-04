using UpBrowser.Core.Dom;
using SkiaSharp;
using System.Text.RegularExpressions;

namespace UpBrowser.Core.Css;

public class StyleComputer
{
    private readonly List<Stylesheet> _stylesheets = new();
    private ComputedStyle? _rootStyle;

    public static readonly HashSet<string> InheritedProperties = new()
    {
        "color", "font-family", "font-size", "font-style", "font-weight",
        "line-height", "letter-spacing", "text-align", "text-indent",
        "visibility", "cursor", "list-style-type", "list-style-position",
        "white-space", "word-spacing", "direction", "unicode-bidi"
    };

    public void AddStylesheet(Stylesheet stylesheet) => _stylesheets.Add(stylesheet);

    public void ComputeStyles(Document document)
    {
        _rootStyle = CreateUserAgentStyle("html");
        _rootStyle.FontSize = 16;

        if (document.DocumentElement != null)
            ComputeElementStyles(document.DocumentElement, _rootStyle);
        else if (document.Body != null)
            ComputeElementStyles(document.Body, _rootStyle);
    }

    private void ComputeElementStyles(Element element, ComputedStyle? parentStyle)
    {
        var style = new ComputedStyle();

        if (parentStyle != null && InheritedProperties.Any(p => true))
        {
            InheritProperties(style, parentStyle);
        }

        ApplyUserAgentStyles(style, element.TagName);

        foreach (var stylesheet in _stylesheets)
        {
            foreach (var rule in stylesheet.Rules)
            {
                var selector = CssSelector.Parse(rule.Selector);
                var isImportant = rule.IsImportant;
                var parent = element.ParentElement;

                if (selector.Matches(element, parent))
                {
                    foreach (var prop in rule.Properties)
                    {
                        var priority = isImportant ? PropertyPriority.Important : PropertyPriority.Normal;
                        ApplyStylePropertyWithPriority(style, prop.Key, prop.Value, priority);
                    }
                }
            }
        }

        var inlineStyleAttr = element.GetAttribute("style");
        if (!string.IsNullOrEmpty(inlineStyleAttr))
        {
            var parser = new CssParser();
            var inlineProps = parser.ParseInlineStyle(inlineStyleAttr);
            foreach (var prop in inlineProps)
            {
                ApplyStylePropertyWithPriority(style, prop.Key, prop.Value, PropertyPriority.Inline);
            }
        }

        element.ComputedStyle = style;

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                ComputeElementStyles(childElement, style);
            }
        }
    }

    private void InheritProperties(ComputedStyle child, ComputedStyle parent)
    {
        child.Color = parent.Color;
        child.FontFamily = parent.FontFamily;
        child.FontSize = parent.FontSize;
        child.FontWeight = parent.FontWeight;
        child.FontStyle = parent.FontStyle;
        child.LineHeight = parent.LineHeight;
        child.TextAlign = parent.TextAlign;
        child.WhiteSpace = parent.WhiteSpace;
        child.Visibility = parent.Visibility;
    }

    private void ApplyUserAgentStyles(ComputedStyle style, string tagName)
    {
        tagName = tagName.ToLowerInvariant();

        switch (tagName)
        {
            case "html": case "body":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                break;
            case "div": style.Display = DisplayType.Block; break;
            case "span": style.Display = DisplayType.Inline; break;
            case "p":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "h1":
                style.Display = DisplayType.Block;
                style.FontSize = 32;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(24);
                style.MarginBottom = new PixelLength(16);
                break;
            case "h2":
                style.Display = DisplayType.Block;
                style.FontSize = 24;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(20);
                style.MarginBottom = new PixelLength(12);
                break;
            case "h3":
                style.Display = DisplayType.Block;
                style.FontSize = 20;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(18);
                style.MarginBottom = new PixelLength(10);
                break;
            case "a":
                style.Display = DisplayType.Inline;
                style.Color = SKColor.Parse("#0000EE");
                break;
            case "ul": case "ol":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "li": style.Display = DisplayType.ListItem; break;
            case "table":
                style.Display = DisplayType.Table;
                style.BorderCollapse = true;
                break;
            case "tr": style.Display = DisplayType.TableRow; break;
            case "td": case "th":
                style.Display = DisplayType.TableCell;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                break;
            case "img":
                style.Display = DisplayType.InlineBlock;
                break;
            case "input": case "button": case "textarea": case "select":
                style.Display = DisplayType.InlineBlock;
                break;
            case "br": style.Display = DisplayType.Inline; break;
            case "hr":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                break;
            case "pre": case "code":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                break;
            case "strong": case "b":
                style.FontWeight = FontWeight.Bold;
                break;
            case "em": case "i":
                style.FontStyle = FontStyleType.Italic;
                break;
            case "u": case "ins":
                style.TextDecoration = TextDecorationType.Underline;
                break;
            case "s": case "del":
                style.TextDecoration = TextDecorationType.LineThrough;
                break;
        }
    }

    private void ApplyStylePropertyWithPriority(ComputedStyle style, string name, string value, PropertyPriority priority)
    {
        switch (name)
        {
            case "width": style.Width = Length.Parse(value); break;
            case "height": style.Height = Length.Parse(value); break;
            case "display": style.Display = ParseDisplay(value); break;
            case "position": style.Position = ParsePosition(value); break;
            case "float": style.Float = ParseFloat(value); break;
            case "clear": style.Clear = ParseClear(value); break;
            case "margin":
                ParseShorthand4(value, out var mt, out var mr, out var mb, out var ml);
                style.MarginTop = mt; style.MarginRight = mr; style.MarginBottom = mb; style.MarginLeft = ml;
                break;
            case "margin-top": style.MarginTop = Length.Parse(value); break;
            case "margin-bottom": style.MarginBottom = Length.Parse(value); break;
            case "margin-left": style.MarginLeft = Length.Parse(value); break;
            case "margin-right": style.MarginRight = Length.Parse(value); break;
            case "padding":
                ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl;
                break;
            case "padding-top": style.PaddingTop = Length.Parse(value); break;
            case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
            case "padding-left": style.PaddingLeft = Length.Parse(value); break;
            case "padding-right": style.PaddingRight = Length.Parse(value); break;
            case "color": style.Color = ParseColor(value); break;
            case "background-color": style.BackgroundColor = ParseColor(value); break;
            case "background-image": style.BackgroundImage = value == "none" ? null : ParseUrl(value); break;
            case "background-repeat": style.BackgroundRepeat = ParseBackgroundRepeat(value); break;
            case "background-position": ParseBackgroundPosition(value, style); break;
            case "font-family": style.FontFamily = value.Trim('"'); break;
            case "font-size": style.FontSize = ParseFontSize(value); break;
            case "font-weight": style.FontWeight = ParseFontWeight(value); break;
            case "font-style": style.FontStyle = value.ToLowerInvariant() == "italic" ? FontStyleType.Italic : FontStyleType.Normal; break;
            case "font": ParseFontShorthand(value, style); break;
            case "text-align": style.TextAlign = ParseTextAlign(value); break;
            case "text-decoration": style.TextDecoration = ParseTextDecoration(value); break;
            case "vertical-align": style.VerticalAlign = ParseVerticalAlign(value); break;
            case "line-height": style.LineHeight = ParseLineHeight(value); break;
            case "white-space": style.WhiteSpace = ParseWhiteSpace(value); break;
            case "visibility": style.Visibility = value.ToLowerInvariant() == "hidden" ? VisibilityType.Hidden : VisibilityType.Visible; break;
            case "overflow": style.Overflow = ParseOverflow(value); break;
            case "z-index": if (value != "auto") style.ZIndex = int.TryParse(value, out var z) ? z : null; break;
            case "border":
                ParseBorderShorthand(value, style);
                break;
            case "border-top": ParseBorderSide(style, "top", value); break;
            case "border-bottom": ParseBorderSide(style, "bottom", value); break;
            case "border-left": ParseBorderSide(style, "left", value); break;
            case "border-right": ParseBorderSide(style, "right", value); break;
            case "border-width": ParseBorderWidth(value, style); break;
            case "border-color": ParseBorderColor(value, style); break;
            case "border-style": ParseBorderStyle(value, style); break;
            case "border-radius":
                ParseBorderRadius(value, style);
                break;
            case "box-sizing": style.BoxSizing = value.Contains("border") ? BoxSizingType.BorderBox : BoxSizingType.ContentBox; break;
            case "flex-direction": style.FlexDirection = ParseFlexDirection(value); break;
            case "flex-wrap": style.FlexWrap = ParseFlexWrap(value); break;
            case "flex-grow": if (float.TryParse(value, out var g)) style.FlexGrow = g; break;
            case "flex-shrink": if (float.TryParse(value, out var s)) style.FlexShrink = s; break;
            case "flex-basis": style.FlexBasis = Length.Parse(value); break;
            case "flex": ParseFlexShorthand(value, style); break;
            case "justify-content": style.JustifyContent = ParseJustifyContent(value); break;
            case "align-items": style.AlignItems = ParseAlignItems(value); break;
            case "align-self": style.AlignSelf = ParseAlignSelf(value); break;
            case "min-width": style.MinWidth = Length.Parse(value); break;
            case "max-width": style.MaxWidth = Length.Parse(value); break;
            case "min-height": style.MinHeight = Length.Parse(value); break;
            case "max-height": style.MaxHeight = Length.Parse(value); break;
            case "top": style.Top = Length.Parse(value); break;
            case "bottom": style.Bottom = Length.Parse(value); break;
            case "left": style.Left = Length.Parse(value); break;
            case "right": style.Right = Length.Parse(value); break;
        }
    }

    private void ParseShorthand4(string value, out Length top, out Length right, out Length bottom, out Length left)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        top = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    private DisplayType ParseDisplay(string value) => value.ToLowerInvariant() switch
    {
        "block" => DisplayType.Block,
        "inline" => DisplayType.Inline,
        "inline-block" => DisplayType.InlineBlock,
        "flex" => DisplayType.Flex,
        "inline-flex" => DisplayType.InlineFlex,
        "list-item" => DisplayType.ListItem,
        "table" => DisplayType.Table,
        "table-row" => DisplayType.TableRow,
        "table-cell" => DisplayType.TableCell,
        "none" => DisplayType.None,
        _ => DisplayType.Block
    };

    private PositionType ParsePosition(string value) => value.ToLowerInvariant() switch
    {
        "relative" => PositionType.Relative,
        "absolute" => PositionType.Absolute,
        "fixed" => PositionType.Fixed,
        "sticky" => PositionType.Sticky,
        _ => PositionType.Static
    };

    private FloatType ParseFloat(string value) => value.ToLowerInvariant() switch
    {
        "left" => FloatType.Left,
        "right" => FloatType.Right,
        _ => FloatType.None
    };

    private ClearType ParseClear(string value) => value.ToLowerInvariant() switch
    {
        "left" => ClearType.Left,
        "right" => ClearType.Right,
        "both" => ClearType.Both,
        _ => ClearType.None
    };

    private float ParseFontSize(string value)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * 16;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * 16;
        if (value.EndsWith("%") && float.TryParse(value[..^2], out var pct)) return pct / 100f * 16;
        return value.ToLowerInvariant() switch
        {
            "xx-small" => 10,
            "x-small" => 12,
            "small" => 14,
            "medium" => 16,
            "large" => 18,
            "x-large" => 24,
            "xx-large" => 32,
            _ => 16
        };
    }

    private FontWeight ParseFontWeight(string value) => value.ToLowerInvariant() switch
    {
        "bold" => FontWeight.Bold,
        "bolder" => FontWeight.Bold,
        "lighter" => FontWeight.Normal,
        "normal" => FontWeight.Normal,
        "100" or "200" or "300" or "400" => FontWeight.Normal,
        "500" or "600" or "700" or "800" or "900" => FontWeight.Bold,
        _ => FontWeight.Normal
    };

    private TextAlignType ParseTextAlign(string value) => value.ToLowerInvariant() switch
    {
        "left" => TextAlignType.Left,
        "right" => TextAlignType.Right,
        "center" => TextAlignType.Center,
        "justify" => TextAlignType.Justify,
        "start" => TextAlignType.Start,
        "end" => TextAlignType.End,
        _ => TextAlignType.Start
    };

    private TextDecorationType ParseTextDecoration(string value) => value.ToLowerInvariant() switch
    {
        "underline" => TextDecorationType.Underline,
        "overline" => TextDecorationType.Overline,
        "line-through" => TextDecorationType.LineThrough,
        "none" => TextDecorationType.None,
        _ => TextDecorationType.None
    };

    private VerticalAlignType ParseVerticalAlign(string value) => value.ToLowerInvariant() switch
    {
        "top" => VerticalAlignType.Top,
        "bottom" => VerticalAlignType.Bottom,
        "middle" => VerticalAlignType.Middle,
        "sub" => VerticalAlignType.Sub,
        "super" => VerticalAlignType.Super,
        "text-top" => VerticalAlignType.TextTop,
        "text-bottom" => VerticalAlignType.TextBottom,
        _ => VerticalAlignType.Baseline
    };

    private float ParseLineHeight(string value)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px / 16;
        if (float.TryParse(value, out var num)) return num;
        return 1.2f;
    }

    private WhiteSpaceMode ParseWhiteSpace(string value) => value.ToLowerInvariant() switch
    {
        "nowrap" => WhiteSpaceMode.Nowrap,
        "pre" => WhiteSpaceMode.Pre,
        "pre-wrap" => WhiteSpaceMode.PreWrap,
        "pre-line" => WhiteSpaceMode.PreLine,
        _ => WhiteSpaceMode.Normal
    };

    private OverflowType ParseOverflow(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden,
        "scroll" => OverflowType.Scroll,
        "auto" => OverflowType.Auto,
        _ => OverflowType.Visible
    };

    private SKColor ParseColor(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "transparent" || value == "inherit")
            return SKColors.Transparent;

        value = value.Trim();

        if (value.StartsWith("#"))
        {
            var hex = value[1..];
            if (hex.Length == 3)
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            if (hex.Length == 6)
                return new SKColor(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
        }

        if (value.StartsWith("rgb("))
        {
            var match = Regex.Match(value, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
            if (match.Success)
                return new SKColor(
                    byte.Parse(match.Groups[1].Value),
                    byte.Parse(match.Groups[2].Value),
                    byte.Parse(match.Groups[3].Value));
        }

        if (value.StartsWith("rgba("))
        {
            var match = Regex.Match(value, @"rgba\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", RegexOptions.IgnoreCase);
            if (match.Success)
                return new SKColor(
                    byte.Parse(match.Groups[1].Value),
                    byte.Parse(match.Groups[2].Value),
                    byte.Parse(match.Groups[3].Value),
                    (byte)(float.Parse(match.Groups[4].Value) * 255));
        }

        return KnownColors.Get(value) ?? SKColors.Black;
    }

    private string? ParseUrl(string value)
    {
        if (value.StartsWith("url("))
        {
            var url = value[4..].Trim(' ', '"', '\'', ')');
            return url;
        }
        return null;
    }

    private BackgroundRepeat ParseBackgroundRepeat(string value) => value.ToLowerInvariant() switch
    {
        "repeat-x" => BackgroundRepeat.RepeatX,
        "repeat-y" => BackgroundRepeat.RepeatY,
        "no-repeat" => BackgroundRepeat.NoRepeat,
        _ => BackgroundRepeat.Repeat
    };

    private void ParseBackgroundPosition(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            style.BackgroundPositionX = parts[0].ToLowerInvariant() switch
            {
                "left" => new PixelLength(0),
                "center" => new PercentLength(0.5f),
                "right" => new PercentLength(1),
                _ => Length.Parse(parts[0])
            };
            style.BackgroundPositionY = parts.Length > 1 ? Length.Parse(parts[1]) : new PixelLength(0);
        }
    }

    private void ParseBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "solid" || part == "dashed" || part == "dotted" || part == "double" || part == "none")
            {
                style.BorderTopStyle = style.BorderRightStyle = style.BorderBottomStyle = style.BorderLeftStyle = ParseBorderStyleValue(part);
            }
            else if (part.EndsWith("px"))
            {
                var width = ParseSize(part);
                if (width.HasValue)
                    style.BorderTopWidth = style.BorderRightWidth = style.BorderBottomWidth = style.BorderLeftWidth = width.Value;
            }
            else
            {
                var color = ParseColor(part);
                style.BorderTopColor = style.BorderRightColor = style.BorderBottomColor = style.BorderLeftColor = color;
            }
        }
    }

    private void ParseBorderSide(ComputedStyle style, string side, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "solid" || part == "dashed" || part == "dotted" || part == "double" || part == "none")
            {
                var bs = ParseBorderStyleValue(part);
                if (side == "top") style.BorderTopStyle = bs;
                else if (side == "bottom") style.BorderBottomStyle = bs;
                else if (side == "left") style.BorderLeftStyle = bs;
                else if (side == "right") style.BorderRightStyle = bs;
            }
            else if (part.EndsWith("px"))
            {
                var width = ParseSize(part);
                if (width.HasValue)
                {
                    if (side == "top") style.BorderTopWidth = width.Value;
                    else if (side == "bottom") style.BorderBottomWidth = width.Value;
                    else if (side == "left") style.BorderLeftWidth = width.Value;
                    else if (side == "right") style.BorderRightWidth = width.Value;
                }
            }
            else
            {
                var color = ParseColor(part);
                if (side == "top") style.BorderTopColor = color;
                else if (side == "bottom") style.BorderBottomColor = color;
                else if (side == "left") style.BorderLeftColor = color;
                else if (side == "right") style.BorderRightColor = color;
            }
        }
    }

    private void ParseBorderWidth(string value, ComputedStyle style)
    {
        var widths = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var w = widths.Select(w => w switch
        {
            "thin" => 1f,
            "medium" => 3f,
            "thick" => 5f,
            _ => ParseSize(w) ?? 0
        }).ToList();

        style.BorderTopWidth = w.Count > 0 ? w[0] : 0;
        style.BorderRightWidth = w.Count > 1 ? w[1] : w[0];
        style.BorderBottomWidth = w.Count > 2 ? w[2] : w[0];
        style.BorderLeftWidth = w.Count > 3 ? w[3] : w[1];
    }

    private void ParseBorderColor(string value, ComputedStyle style)
    {
        var colors = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var c = colors.Select(ParseColor).ToList();

        style.BorderTopColor = c.Count > 0 ? c[0] : SKColors.Black;
        style.BorderRightColor = c.Count > 1 ? c[1] : c[0];
        style.BorderBottomColor = c.Count > 2 ? c[2] : c[0];
        style.BorderLeftColor = c.Count > 3 ? c[3] : c[1];
    }

    private void ParseBorderStyle(string value, ComputedStyle style)
    {
        var styles = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = styles.Select(ParseBorderStyleValue).ToList();

        style.BorderTopStyle = s.Count > 0 ? s[0] : BorderStyle.None;
        style.BorderRightStyle = s.Count > 1 ? s[1] : s[0];
        style.BorderBottomStyle = s.Count > 2 ? s[2] : s[0];
        style.BorderLeftStyle = s.Count > 3 ? s[3] : s[1];
    }

    private void ParseBorderRadius(string value, ComputedStyle style)
    {
        var radii = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var r = radii.Select(v => ParseSize(v) ?? 0).ToList();

        style.BorderTopLeftRadius = r.Count > 0 ? r[0] : 0;
        style.BorderTopRightRadius = r.Count > 1 ? r[1] : r[0];
        style.BorderBottomRightRadius = r.Count > 2 ? r[2] : r[0];
        style.BorderBottomLeftRadius = r.Count > 3 ? r[3] : (r.Count > 0 ? r[0] : 0);
    }

    private void ParseFontShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Contains('/'))
            {
                var fs = part.Split('/');
                style.FontSize = ParseFontSize(fs[0]);
                if (fs.Length > 1) style.LineHeight = ParseLineHeight(fs[1]);
            }
            else if (float.TryParse(part, out _)) { }
            else if (part == "italic" || part == "oblique") style.FontStyle = FontStyleType.Italic;
            else if (part == "bold" || part == "bolder") style.FontWeight = FontWeight.Bold;
            else if (part == "normal") { style.FontWeight = FontWeight.Normal; style.FontStyle = FontStyleType.Normal; }
            else style.FontFamily = part.Trim('"');
        }
    }

    private void ParseFlexShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        int i = 0;
        if (parts[0] == "none" || parts[0] == "auto")
        {
            style.FlexGrow = 0;
            style.FlexShrink = 1;
            style.FlexBasis = AutoLength.Instance;
            return;
        }

        if (float.TryParse(parts[0], out var g))
        {
            style.FlexGrow = g;
            i++;
            if (i < parts.Length && float.TryParse(parts[i], out var s))
            {
                style.FlexShrink = s;
                i++;
            }
        }
        else
        {
            style.FlexBasis = Length.Parse(parts[0]);
            i++;
            if (i < parts.Length && float.TryParse(parts[i], out var g2))
            {
                style.FlexGrow = g2;
            }
        }
    }

    private FlexDirectionType ParseFlexDirection(string value) => value.ToLowerInvariant() switch
    {
        "row-reverse" => FlexDirectionType.RowReverse,
        "column" => FlexDirectionType.Column,
        "column-reverse" => FlexDirectionType.ColumnReverse,
        _ => FlexDirectionType.Row
    };

    private FlexWrapType ParseFlexWrap(string value) => value.ToLowerInvariant() switch
    {
        "wrap" => FlexWrapType.Wrap,
        "wrap-reverse" => FlexWrapType.WrapReverse,
        _ => FlexWrapType.NoWrap
    };

    private JustifyContentType ParseJustifyContent(string value) => value.ToLowerInvariant() switch
    {
        "flex-end" => JustifyContentType.FlexEnd,
        "center" => JustifyContentType.Center,
        "space-between" => JustifyContentType.SpaceBetween,
        "space-around" => JustifyContentType.SpaceAround,
        "space-evenly" => JustifyContentType.SpaceEvenly,
        _ => JustifyContentType.FlexStart
    };

    private AlignItemsType ParseAlignItems(string value) => value.ToLowerInvariant() switch
    {
        "flex-start" => AlignItemsType.FlexStart,
        "flex-end" => AlignItemsType.FlexEnd,
        "center" => AlignItemsType.Center,
        "baseline" => AlignItemsType.Baseline,
        _ => AlignItemsType.Stretch
    };

    private AlignSelfType ParseAlignSelf(string value) => value.ToLowerInvariant() switch
    {
        "flex-start" => AlignSelfType.FlexStart,
        "flex-end" => AlignSelfType.FlexEnd,
        "center" => AlignSelfType.Center,
        "baseline" => AlignSelfType.Baseline,
        "stretch" => AlignSelfType.Stretch,
        _ => AlignSelfType.Auto
    };

    private BorderStyle ParseBorderStyleValue(string value) => value.ToLowerInvariant() switch
    {
        "solid" => BorderStyle.Solid,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "double" => BorderStyle.Double,
        _ => BorderStyle.None
    };

    private float? ParseSize(string value)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * 16;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * 16;
        if (value == "0") return 0;
        return null;
    }

    private ComputedStyle CreateUserAgentStyle(string tagName)
    {
        var style = new ComputedStyle();
        ApplyUserAgentStyles(style, tagName);
        return style;
    }

    private enum PropertyPriority { Normal, Important, Inline }
}

public static class KnownColors
{
    private static readonly Dictionary<string, SKColor> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "black", SKColors.Black },
        { "white", SKColors.White },
        { "red", SKColor.Parse("#FF0000") },
        { "green", SKColor.Parse("#008000") },
        { "blue", SKColor.Parse("#0000FF") },
        { "yellow", SKColors.Yellow },
        { "cyan", SKColors.Cyan },
        { "magenta", SKColors.Magenta },
        { "gray", SKColors.Gray },
        { "silver", SKColor.Parse("#C0C0C0") },
        { "maroon", SKColor.Parse("#800000") },
        { "olive", SKColor.Parse("#808000") },
        { "lime", SKColor.Parse("#00FF00") },
        { "aqua", SKColor.Parse("#00FFFF") },
        { "teal", SKColor.Parse("#008080") },
        { "navy", SKColor.Parse("#000080") },
        { "fuchsia", SKColor.Parse("#FF00FF") },
        { "purple", SKColors.Purple },
        { "orange", SKColor.Parse("#FFA500") },
        { "pink", SKColor.Parse("#FFC0CB") },
        { "coral", SKColor.Parse("#FF7F50") },
        { "salmon", SKColor.Parse("#FA8072") },
        { "gold", SKColor.Parse("#FFD700") },
        { "khaki", SKColor.Parse("#F0E68C") },
        { "plum", SKColor.Parse("#DDA0DD") },
        { "violet", SKColor.Parse("#EE82EE") },
        { "tan", SKColor.Parse("#D2B48C") },
        { "chocolate", SKColor.Parse("#D2691E") },
        { "transparent", SKColors.Transparent },
    };

    public static SKColor? Get(string name) => Colors.GetValueOrDefault(name);
}