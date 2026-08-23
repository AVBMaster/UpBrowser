using UpBrowser.Core.Css.Matcher;
using UpBrowser.Core.Css.Properties;
using UpBrowser.Core.Css.Rules;
using UpBrowser.Core.Css.Values;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.Cascade;

/// <summary>
/// The CSS cascade engine, mirroring Blink's StyleCascade.
/// Analyzes matched rules into a CascadeMap, then applies them to build a ComputedStyle.
/// </summary>
public class StyleCascade
{
    private readonly CascadeResolverState _state;
    private readonly CascadeMap _map = new();
    private uint _generation;
    private bool _needsAnalyze = true;

    public StyleCascade(CascadeResolverState state) => _state = state;
    public CascadeMap Map => _map;
    public bool InlineStyleLost => _map.InlineStyleLost;

    public void AnalyzeIfNeeded()
    {
        if (!_needsAnalyze) return;
        _map.Reset();
        _generation = 0;

        foreach (var entry in _state.MatchedRules)
        {
            var priority = entry.Priority;
            foreach (var prop in entry.Properties.Properties)
            {
                if (prop.Name.IsCustom)
                    _map.Add(prop.Name.CustomName!, priority);
                else
                    _map.Add(prop.Name.Id, priority);
            }
        }
        _needsAnalyze = false;
    }

    public void Apply(CascadeFilter? filter = null)
    {
        AnalyzeIfNeeded();

        ApplyCascadeAffecting();

        ApplyHighPriority();

        ApplyMatchResult(filter);

        ApplyInterpolations();
    }

    private void ApplyCascadeAffecting()
    {
        ApplyIfPresent(CssPropertyId.Direction);
        ApplyIfPresent(CssPropertyId.WritingMode);
        ApplyIfPresent(CssPropertyId.Zoom);
    }

    private void ApplyHighPriority()
    {
        foreach (var id in HighPriorityProperties)
            ApplyIfPresent(id);
    }

    private static readonly CssPropertyId[] HighPriorityProperties =
    {
        CssPropertyId.FontSize, CssPropertyId.FontWeight, CssPropertyId.FontStyle,
        CssPropertyId.FontFamily, CssPropertyId.FontVariant, CssPropertyId.FontStretch,
        CssPropertyId.FontKerning, CssPropertyId.FontSizeAdjust, CssPropertyId.Font,
        CssPropertyId.LineHeight, CssPropertyId.Color, CssPropertyId.Visibility,
        CssPropertyId.TextAlign, CssPropertyId.TextTransform, CssPropertyId.TextIndent,
        CssPropertyId.LetterSpacing, CssPropertyId.WordSpacing, CssPropertyId.WhiteSpace,
        CssPropertyId.Direction, CssPropertyId.WritingMode,
    };

    private void ApplyMatchResult(CascadeFilter? filter)
    {
        foreach (var id in _map.NativeIds)
        {
            if (filter?.Rejects(new CssPropertyName(id)) == true) continue;
            if (HighPriorityProperties.Contains(id)) continue;
            if (id is CssPropertyId.Direction or CssPropertyId.WritingMode or CssPropertyId.Zoom) continue;
            ApplyIfPresent(id);
        }
        foreach (var customName in _map.CustomNames)
        {
            if (filter?.Rejects(new CssPropertyName(customName)) == true) continue;
            ApplyCustomIfPresent(customName);
        }
    }

    private void ApplyInterpolations()
    {
        foreach (var entry in _state.Interpolations)
        {
            foreach (var prop in entry.Properties.Properties)
            {
                if (prop.Name.IsCustom)
                    _state.ApplyCustomProperty(prop.Name.CustomName!, prop.Value);
                else
                    ApplyProperty(prop.Name.Id, prop.Value);
            }
        }
    }

    private void ApplyIfPresent(CssPropertyId id)
    {
        if (!_map.TryGetWinner(id, out var priority)) return;
        if (!priority.IsRelevant) return;
        var matchedEntry = FindMatchedEntry(priority);
        if (matchedEntry == null) return;
        var prop = matchedEntry.Properties.Properties
            .FirstOrDefault(p => p.Name.Id == id && !p.Name.IsCustom);
        if (prop.Value != null)
            ApplyProperty(id, prop.Value);
    }

    private void ApplyCustomIfPresent(string customName)
    {
        if (!_map.TryGetWinner(customName, out var priority)) return;
        if (!priority.IsRelevant) return;
        var matchedEntry = FindMatchedEntry(priority);
        if (matchedEntry == null) return;
        var prop = matchedEntry.Properties.Properties
            .FirstOrDefault(p => p.Name.IsCustom && p.Name.CustomName == customName);
        if (prop.Value != null)
            _state.ApplyCustomProperty(customName, prop.Value);
    }

    private void ApplyProperty(CssPropertyId id, CssValue value)
    {
        _state.ApplyProperty(id, value);
    }

    private MatchedRuleEntry? FindMatchedEntry(CascadePriority priority)
    {
        foreach (var entry in _state.MatchedRules)
            if (entry.Priority.Equals(priority))
                return entry;
        return null;
    }

    public void Reset()
    {
        _map.Reset();
        _needsAnalyze = true;
        _generation = 0;
    }
}

/// <summary>
/// State accumulated during cascade resolution, bridging to the existing ComputedStyle.
/// </summary>
public class CascadeResolverState
{
    public List<MatchedRuleEntry> MatchedRules { get; } = new();
    public List<MatchedRuleEntry> Interpolations { get; } = new();
    public Element? Element { get; set; }
    public ComputedStyle? ParentStyle { get; set; }
    public float ViewportWidth { get; set; } = 1024;
    public float ViewportHeight { get; set; } = 768;
    public float RootFontSize { get; set; } = 16;
    public float FontSize { get; set; } = 16;
    public string ColorScheme { get; set; } = "light";

    public void ApplyProperty(CssPropertyId id, CssValue value)
    {
        var style = Element?.ComputedStyle;
        if (style == null) return;
        string text = value.CssText();

        switch (id)
        {
            case CssPropertyId.Color: style.Color = ParseColor(text); break;
            case CssPropertyId.FontSize: style.FontSize = Length.ParseFontSize(text, ParentStyle?.FontSize ?? 16); break;
            case CssPropertyId.FontWeight: style.FontWeight = ParseFontWeight(text); break;
            case CssPropertyId.FontFamily: style.FontFamily = text; break;
            case CssPropertyId.FontStyle: style.FontStyle = ParseFontStyle(text); break;
            case CssPropertyId.LineHeight: Fonts.LineBoxMetrics.ApplyLineHeight(style, text); break;
            case CssPropertyId.TextAlign: style.TextAlign = ParseTextAlign(text); break;
            case CssPropertyId.Visibility: style.Visibility = ParseVisibility(text); break;
            case CssPropertyId.WhiteSpace: style.WhiteSpace = ParseWhiteSpace(text); break;
            case CssPropertyId.Width: style.Width = ParseLength(text); break;
            case CssPropertyId.Height: style.Height = ParseLength(text); break;
            case CssPropertyId.MinWidth: style.MinWidth = ParseLength(text); break;
            case CssPropertyId.MinHeight: style.MinHeight = ParseLength(text); break;
            case CssPropertyId.MaxWidth: style.MaxWidth = ParseLength(text); break;
            case CssPropertyId.MaxHeight: style.MaxHeight = ParseLength(text); break;
            case CssPropertyId.MarginLeft: style.MarginLeft = ParseLength(text); break;
            case CssPropertyId.MarginRight: style.MarginRight = ParseLength(text); break;
            case CssPropertyId.MarginTop: style.MarginTop = ParseLength(text); break;
            case CssPropertyId.MarginBottom: style.MarginBottom = ParseLength(text); break;
            case CssPropertyId.PaddingLeft: style.PaddingLeft = ParseLength(text); break;
            case CssPropertyId.PaddingRight: style.PaddingRight = ParseLength(text); break;
            case CssPropertyId.PaddingTop: style.PaddingTop = ParseLength(text); break;
            case CssPropertyId.PaddingBottom: style.PaddingBottom = ParseLength(text); break;
            case CssPropertyId.BorderLeftWidth: style.BorderLeftWidth = ParseBorderWidth(text); break;
            case CssPropertyId.BorderRightWidth: style.BorderRightWidth = ParseBorderWidth(text); break;
            case CssPropertyId.BorderTopWidth: style.BorderTopWidth = ParseBorderWidth(text); break;
            case CssPropertyId.BorderBottomWidth: style.BorderBottomWidth = ParseBorderWidth(text); break;
            case CssPropertyId.BorderLeftColor: style.BorderLeftColor = ParseColor(text); break;
            case CssPropertyId.BorderRightColor: style.BorderRightColor = ParseColor(text); break;
            case CssPropertyId.BorderTopColor: style.BorderTopColor = ParseColor(text); break;
            case CssPropertyId.BorderBottomColor: style.BorderBottomColor = ParseColor(text); break;
            case CssPropertyId.BorderLeftStyle: style.BorderLeftStyle = ParseBorderStyle(text); break;
            case CssPropertyId.BorderRightStyle: style.BorderRightStyle = ParseBorderStyle(text); break;
            case CssPropertyId.BorderTopStyle: style.BorderTopStyle = ParseBorderStyle(text); break;
            case CssPropertyId.BorderBottomStyle: style.BorderBottomStyle = ParseBorderStyle(text); break;
            case CssPropertyId.BorderCollapse: style.BorderCollapse = text.Equals("collapse", StringComparison.OrdinalIgnoreCase); break;
            case CssPropertyId.Display: style.Display = ParseDisplay(text); break;
            case CssPropertyId.Position: style.Position = ParsePosition(text); break;
            case CssPropertyId.BackgroundColor: style.BackgroundColor = ParseColor(text); break;
            case CssPropertyId.Opacity: style.Opacity = ParseFloat(text, 1); break;
            case CssPropertyId.Float: style.Float = ParseFloatType(text); break;
            case CssPropertyId.Clear: style.Clear = ParseClearType(text); break;
            case CssPropertyId.Overflow: style.OverflowX = style.OverflowY = ParseOverflow(text); break;
            case CssPropertyId.OverflowX: style.OverflowX = ParseOverflow(text); break;
            case CssPropertyId.OverflowY: style.OverflowY = ParseOverflow(text); break;
            case CssPropertyId.ZIndex: style.ZIndex = (int)ParseFloat(text, 0); break;
            case CssPropertyId.Left: style.Left = ParseLength(text); break;
            case CssPropertyId.Right: style.Right = ParseLength(text); break;
            case CssPropertyId.Top: style.Top = ParseLength(text); break;
            case CssPropertyId.Bottom: style.Bottom = ParseLength(text); break;
            case CssPropertyId.BoxSizing: style.BoxSizing = text.Equals("border-box", StringComparison.OrdinalIgnoreCase) ? Dom.BoxSizingType.BorderBox : Dom.BoxSizingType.ContentBox; break;
            case CssPropertyId.TextDecoration: style.TextDecoration = ParseTextDecoration(text); break;
            case CssPropertyId.TextTransform: style.TextTransform = text; break;
            case CssPropertyId.LetterSpacing: style.LetterSpacing = ParseFloat(text, 0); break;
            case CssPropertyId.WordSpacing: style.WordSpacing = ParseFloat(text, 0); break;
            case CssPropertyId.WordBreak: style.WordBreak = ParseWordBreak(text); break;
            case CssPropertyId.OverflowWrap: style.OverflowWrap = ParseOverflowWrap(text); break;
            case CssPropertyId.FontVariant: style.FontVariant = text; break;
            case CssPropertyId.Cursor: style.Cursor = text; break;
            case CssPropertyId.BorderTopLeftRadius: style.BorderTopLeftRadius = ParseFloat(text, 0); break;
            case CssPropertyId.BorderTopRightRadius: style.BorderTopRightRadius = ParseFloat(text, 0); break;
            case CssPropertyId.BorderBottomLeftRadius: style.BorderBottomLeftRadius = ParseFloat(text, 0); break;
            case CssPropertyId.BorderBottomRightRadius: style.BorderBottomRightRadius = ParseFloat(text, 0); break;
            case CssPropertyId.ListStyleType: style.ListStyleType = ParseListStyleType(text); break;
            case CssPropertyId.ListStylePosition: style.ListStylePosition = text.Equals("inside", StringComparison.OrdinalIgnoreCase) ? Dom.ListStylePosition.Inside : Dom.ListStylePosition.Outside; break;
            case CssPropertyId.Content: style.Content = text; break;
            case CssPropertyId.BackgroundImage: style.BackgroundImage = new List<string> { text }; break;
            case CssPropertyId.FlexDirection: style.FlexDirection = ParseFlexDirection(text); break;
            case CssPropertyId.FlexWrap: style.FlexWrap = ParseFlexWrap(text); break;
            case CssPropertyId.JustifyContent: style.JustifyContent = ParseJustifyContent(text); break;
            case CssPropertyId.AlignItems: style.AlignItems = ParseAlignItems(text); break;
            case CssPropertyId.AlignSelf: style.AlignSelf = ParseAlignSelf(text); break;
            case CssPropertyId.FlexGrow: style.FlexGrow = ParseFloat(text, 0); break;
            case CssPropertyId.FlexShrink: style.FlexShrink = ParseFloat(text, 1); break;
            case CssPropertyId.FlexBasis: style.FlexBasis = ParseLength(text); break;
            case CssPropertyId.Order: style.Order = (int)ParseFloat(text, 0); break;
            case CssPropertyId.RowGap: style.RowGap = ParseLength(text); break;
            case CssPropertyId.ColumnGap: style.ColumnGap = ParseLength(text); break;
            case CssPropertyId.GridTemplateColumns: style.GridTemplateColumns = text; break;
            case CssPropertyId.GridTemplateRows: style.GridTemplateRows = text; break;
            case CssPropertyId.GridAutoColumns: style.GridAutoColumns = text; break;
            case CssPropertyId.GridAutoRows: style.GridAutoRows = text; break;
            case CssPropertyId.GridAutoFlow: style.GridAutoFlow = ParseGridAutoFlow(text); break;
            case CssPropertyId.GridColumn: style.GridColumn = text; break;
            case CssPropertyId.GridRow: style.GridRow = text; break;
            case CssPropertyId.GridColumnStart: style.GridColumnStart = text; break;
            case CssPropertyId.GridColumnEnd: style.GridColumnEnd = text; break;
            case CssPropertyId.GridRowStart: style.GridRowStart = text; break;
            case CssPropertyId.GridRowEnd: style.GridRowEnd = text; break;
            case CssPropertyId.GridTemplateAreas: style.GridTemplateAreas = text; break;
            case CssPropertyId.Transform: style.Transform = text; break;
            case CssPropertyId.TransformOrigin: style.TransformOrigin = text; break;
            case CssPropertyId.Transition: style.Transition = text; break;
            case CssPropertyId.Animation: style.Animation = text; break;
            case CssPropertyId.AnimationName: style.AnimationName = text; break;
            case CssPropertyId.AnimationDuration: style.AnimationDuration = text; break;
            case CssPropertyId.AnimationTimingFunction: style.AnimationTimingFunction = text; break;
            case CssPropertyId.AnimationDelay: style.AnimationDelay = text; break;
            case CssPropertyId.AnimationIterationCount: style.AnimationIterationCount = text; break;
            case CssPropertyId.AnimationDirection: style.AnimationDirection = text; break;
            case CssPropertyId.AnimationFillMode: style.AnimationFillMode = text; break;
            case CssPropertyId.AnimationPlayState: style.AnimationPlayState = text; break;
            case CssPropertyId.AspectRatio: style.AspectRatio = ParseFloat(text, 0); break;
            case CssPropertyId.ObjectFit: style.ObjectFit = ParseObjectFit(text); break;
            case CssPropertyId.PointerEvents: style.PointerEvents = text; break;
            case CssPropertyId.UserSelect: style.UserSelect = text; break;
            case CssPropertyId.Resize: style.Resize = ParseResizeType(text); break;
            case CssPropertyId.WillChange: style.WillChange = text; break;
            case CssPropertyId.Filter: style.Filter = text; break;
            case CssPropertyId.BackdropFilter: style.BackdropFilter = text; break;
            case CssPropertyId.MixBlendMode: style.MixBlendMode = ParseMixBlendMode(text); break;
            case CssPropertyId.Isolation: style.Isolation = ParseIsolationType(text); break;
            case CssPropertyId.Contain: style.Contain = ParseContainType(text); break;
            case CssPropertyId.ContentVisibility: style.ContentVisibility = ParseContentVisibility(text); break;
            case CssPropertyId.ScrollBehavior: style.ScrollBehavior = ParseScrollBehavior(text); break;
            case CssPropertyId.OverscrollBehavior: style.OverscrollBehavior = ParseOverscrollBehavior(text); break;
            case CssPropertyId.AccentColor: style.AccentColor = ParseColor(text); break;
            case CssPropertyId.ColorScheme: style.ColorScheme = text; break;
            case CssPropertyId.CaretColor: style.CaretColor = ParseColor(text); break;
            case CssPropertyId.Appearance: style.Appearance = text; break;
            case CssPropertyId.TableLayout: style.TableLayout = text; break;
            case CssPropertyId.BorderSpacing: style.BorderSpacing = ParseFloat(text, 0); break;
            case CssPropertyId.CaptionSide: style.CaptionSide = text; break;
            case CssPropertyId.EmptyCells: style.EmptyCells = text; break;
            case CssPropertyId.VerticalAlign: style.VerticalAlign = ParseVerticalAlign(text); break;
            case CssPropertyId.Direction: style.Direction = text; break;
            case CssPropertyId.WritingMode: style.WritingMode = ParseWritingMode(text); break;
            case CssPropertyId.Zoom: style.Zoom = ParseFloat(text, 1); break;
            case CssPropertyId.BackgroundRepeat: style.BackgroundRepeat = ParseBackgroundRepeat(text); break;
            case CssPropertyId.BackgroundAttachment: style.BackgroundAttachment = ParseBackgroundAttachment(text); break;
            case CssPropertyId.BackgroundSize: style.BackgroundSize = ParseBackgroundSize(text); break;
            case CssPropertyId.TextOverflow: style.TextOverflow = ParseTextOverflow(text); break;
            case CssPropertyId.TextDecorationLine: style.TextDecorationLine = ParseTextDecorationLine(text); break;
            case CssPropertyId.TextDecorationStyle: style.TextDecorationStyle = ParseTextDecorationStyle(text); break;
            case CssPropertyId.TextDecorationColor: style.TextDecorationColor = ParseColor(text); break;
            case CssPropertyId.TextDecorationThickness: style.TextDecorationThickness = ParseFloat(text, 0); break;
            case CssPropertyId.TextUnderlineOffset: style.TextUnderlineOffset = ParseFloat(text, 0); break;
            case CssPropertyId.OutlineColor: style.OutlineColor = ParseColor(text); break;
            case CssPropertyId.OutlineWidth: style.OutlineWidth = ParseFloat(text, 0); break;
            case CssPropertyId.OutlineStyle: style.OutlineStyle = ParseBorderStyle(text); break;
            case CssPropertyId.OutlineOffset: style.OutlineOffset = ParseFloat(text, 0); break;
            // TabSize mapped to a specific property
            case CssPropertyId.Orphans: style.Orphans = (int)ParseFloat(text, 2); break;
            case CssPropertyId.Widows: style.Widows = (int)ParseFloat(text, 2); break;
            case CssPropertyId.BackgroundBlendMode: style.BackgroundBlendMode = ParseBackgroundBlendMode(text); break;
            case CssPropertyId.ImageRendering: style.ImageRendering = ParseImageRendering(text); break;
            case CssPropertyId.ForcedColorAdjust: style.ForcedColorAdjust = ParseForcedColorAdjust(text); break;
            case CssPropertyId.Hyphens: style.Hyphens = ParseHyphens(text); break;
            case CssPropertyId.LineBreak: style.LineBreak = ParseLineBreak(text); break;
            case CssPropertyId.TextJustify: style.TextJustify = ParseTextJustify(text); break;
            case CssPropertyId.OverflowAnchor: style.OverflowAnchor = ParseOverflowAnchor(text); break;
        }
    }

    public void ApplyCustomProperty(string name, CssValue value)
    {
        Element?.ComputedStyle?.SetCustomProperty(name, value.CssText());
    }

    private static float ParseFloat(string text, float defaultVal) =>
        float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : defaultVal;

    private static SkiaSharp.SKColor ParseColor(string value)
    {
        try { return ColorParser.Parse(value); } catch { return SkiaSharp.SKColors.Transparent; }
    }

    private static Dom.FontWeight ParseFontWeight(string value)
    {
        return ParseFloat(value, 400) switch
        {
            700 => Dom.FontWeight.Bold,
            _ => Dom.FontWeight.Normal
        };
    }

    private static Dom.FontStyleType ParseFontStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" => Dom.FontStyleType.Italic,
        "oblique" => Dom.FontStyleType.Oblique,
        _ => Dom.FontStyleType.Normal
    };

    private static Dom.TextDecorationType ParseTextDecoration(string value) => value.ToLowerInvariant() switch
    {
        "underline" => Dom.TextDecorationType.Underline,
        "overline" => Dom.TextDecorationType.Overline,
        "line-through" => Dom.TextDecorationType.LineThrough,
        _ => Dom.TextDecorationType.None
    };

    private static Dom.GridAutoFlowType ParseGridAutoFlow(string value) => value.ToLowerInvariant() switch
    {
        "column" => Dom.GridAutoFlowType.Column,
        "dense" => Dom.GridAutoFlowType.Dense,
        "row dense" => Dom.GridAutoFlowType.Dense,
        "column dense" => Dom.GridAutoFlowType.Dense,
        _ => Dom.GridAutoFlowType.Row
    };

    private static Dom.ResizeType ParseResizeType(string value) => value.ToLowerInvariant() switch
    {
        "both" => Dom.ResizeType.Both,
        "horizontal" => Dom.ResizeType.Horizontal,
        "vertical" => Dom.ResizeType.Vertical,
        _ => Dom.ResizeType.None
    };

    private static Dom.MixBlendModeType ParseMixBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => Dom.MixBlendModeType.Multiply,
        "screen" => Dom.MixBlendModeType.Screen,
        "overlay" => Dom.MixBlendModeType.Overlay,
        "darken" => Dom.MixBlendModeType.Darken,
        "lighten" => Dom.MixBlendModeType.Lighten,
        "color-dodge" => Dom.MixBlendModeType.ColorDodge,
        "color-burn" => Dom.MixBlendModeType.ColorBurn,
        "hard-light" => Dom.MixBlendModeType.HardLight,
        "soft-light" => Dom.MixBlendModeType.SoftLight,
        "difference" => Dom.MixBlendModeType.Difference,
        "exclusion" => Dom.MixBlendModeType.Exclusion,
        "hue" => Dom.MixBlendModeType.Hue,
        "saturation" => Dom.MixBlendModeType.Saturation,
        "color" => Dom.MixBlendModeType.Color,
        "luminosity" => Dom.MixBlendModeType.Luminosity,
        _ => Dom.MixBlendModeType.Normal
    };

    private static Dom.IsolationType ParseIsolationType(string value) =>
        value.Equals("isolate", StringComparison.OrdinalIgnoreCase) ? Dom.IsolationType.Isolate : Dom.IsolationType.Auto;

    private static Dom.ContainType ParseContainType(string value) => value.ToLowerInvariant() switch
    {
        "strict" => Dom.ContainType.Strict,
        "content" => Dom.ContainType.Content,
        "layout" => Dom.ContainType.Layout,
        "paint" => Dom.ContainType.Paint,
        "size" => Dom.ContainType.Size,
        _ => Dom.ContainType.None
    };

    private static Dom.ContentVisibilityType ParseContentVisibility(string value) => value.ToLowerInvariant() switch
    {
        "auto" => Dom.ContentVisibilityType.Auto,
        "hidden" => Dom.ContentVisibilityType.Hidden,
        _ => Dom.ContentVisibilityType.Visible
    };

    private static Dom.ScrollBehaviorType ParseScrollBehavior(string value) =>
        value.Equals("smooth", StringComparison.OrdinalIgnoreCase) ? Dom.ScrollBehaviorType.Smooth : Dom.ScrollBehaviorType.Auto;

    private static Dom.OverscrollBehaviorType ParseOverscrollBehavior(string value) => value.ToLowerInvariant() switch
    {
        "contain" => Dom.OverscrollBehaviorType.Contain,
        "none" => Dom.OverscrollBehaviorType.None,
        _ => Dom.OverscrollBehaviorType.Auto
    };

    private static Dom.VerticalAlignType ParseVerticalAlign(string value) => value.ToLowerInvariant() switch
    {
        "top" => Dom.VerticalAlignType.Top,
        "middle" => Dom.VerticalAlignType.Middle,
        "bottom" => Dom.VerticalAlignType.Bottom,
        "sub" => Dom.VerticalAlignType.Sub,
        "super" => Dom.VerticalAlignType.Super,
        "text-top" => Dom.VerticalAlignType.TextTop,
        "text-bottom" => Dom.VerticalAlignType.TextBottom,
        "inherit" => Dom.VerticalAlignType.Inherit,
        _ => Dom.VerticalAlignType.Baseline
    };

    private static Dom.WritingModeType ParseWritingMode(string value) => value.ToLowerInvariant() switch
    {
        "vertical-rl" => Dom.WritingModeType.VerticalRl,
        "vertical-lr" => Dom.WritingModeType.VerticalLr,
        _ => Dom.WritingModeType.HorizontalTb
    };

    private static Dom.BackgroundRepeat ParseBackgroundRepeat(string value) => value.ToLowerInvariant() switch
    {
        "repeat-x" => Dom.BackgroundRepeat.RepeatX,
        "repeat-y" => Dom.BackgroundRepeat.RepeatY,
        "no-repeat" => Dom.BackgroundRepeat.NoRepeat,
        _ => Dom.BackgroundRepeat.Repeat
    };

    private static Dom.BackgroundAttachment ParseBackgroundAttachment(string value) => value.ToLowerInvariant() switch
    {
        "fixed" => Dom.BackgroundAttachment.Fixed,
        "local" => Dom.BackgroundAttachment.Local,
        _ => Dom.BackgroundAttachment.Scroll
    };

    private static Dom.BackgroundSizeType ParseBackgroundSize(string value) => value.ToLowerInvariant() switch
    {
        "cover" => Dom.BackgroundSizeType.Cover,
        "contain" => Dom.BackgroundSizeType.Contain,
        _ => Dom.BackgroundSizeType.Auto
    };

    private static Dom.TextOverflowType ParseTextOverflow(string value) =>
        value.Equals("ellipsis", StringComparison.OrdinalIgnoreCase) ? Dom.TextOverflowType.Ellipsis : Dom.TextOverflowType.Clip;

    private static Dom.TextDecorationLineType ParseTextDecorationLine(string value) => value.ToLowerInvariant() switch
    {
        "underline" => Dom.TextDecorationLineType.Underline,
        "overline" => Dom.TextDecorationLineType.Overline,
        "line-through" => Dom.TextDecorationLineType.LineThrough,
        _ => Dom.TextDecorationLineType.None
    };

    private static Dom.TextDecorationStyleType ParseTextDecorationStyle(string value) => value.ToLowerInvariant() switch
    {
        "double" => Dom.TextDecorationStyleType.Double,
        "dotted" => Dom.TextDecorationStyleType.Dotted,
        "dashed" => Dom.TextDecorationStyleType.Dashed,
        "wavy" => Dom.TextDecorationStyleType.Wavy,
        _ => Dom.TextDecorationStyleType.Solid
    };

    private static Dom.BackgroundBlendModeType ParseBackgroundBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => Dom.BackgroundBlendModeType.Multiply,
        "screen" => Dom.BackgroundBlendModeType.Screen,
        "overlay" => Dom.BackgroundBlendModeType.Overlay,
        "darken" => Dom.BackgroundBlendModeType.Darken,
        "lighten" => Dom.BackgroundBlendModeType.Lighten,
        "color-dodge" => Dom.BackgroundBlendModeType.ColorDodge,
        "color-burn" => Dom.BackgroundBlendModeType.ColorBurn,
        "hard-light" => Dom.BackgroundBlendModeType.HardLight,
        "soft-light" => Dom.BackgroundBlendModeType.SoftLight,
        "difference" => Dom.BackgroundBlendModeType.Difference,
        "exclusion" => Dom.BackgroundBlendModeType.Exclusion,
        "hue" => Dom.BackgroundBlendModeType.Hue,
        "saturation" => Dom.BackgroundBlendModeType.Saturation,
        "color" => Dom.BackgroundBlendModeType.Color,
        "luminosity" => Dom.BackgroundBlendModeType.Luminosity,
        _ => Dom.BackgroundBlendModeType.Normal
    };

    private static Dom.ImageRenderingType ParseImageRendering(string value) => value.ToLowerInvariant() switch
    {
        "crisp-edges" => Dom.ImageRenderingType.CrispEdges,
        "pixelated" => Dom.ImageRenderingType.Pixelated,
        _ => Dom.ImageRenderingType.Auto
    };

    private static Dom.ForcedColorAdjustType ParseForcedColorAdjust(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? Dom.ForcedColorAdjustType.None : Dom.ForcedColorAdjustType.Auto;

    private static Dom.HyphensType ParseHyphens(string value) => value.ToLowerInvariant() switch
    {
        "manual" => Dom.HyphensType.Manual,
        "auto" => Dom.HyphensType.Auto,
        _ => Dom.HyphensType.None
    };

    private static Dom.LineBreakType ParseLineBreak(string value) => value.ToLowerInvariant() switch
    {
        "loose" => Dom.LineBreakType.Loose,
        "normal" => Dom.LineBreakType.Normal,
        "strict" => Dom.LineBreakType.Strict,
        "anywhere" => Dom.LineBreakType.Anywhere,
        _ => Dom.LineBreakType.Auto
    };

    private static Dom.TextJustifyType ParseTextJustify(string value) => value.ToLowerInvariant() switch
    {
        "inter-word" => Dom.TextJustifyType.InterWord,
        "inter-character" => Dom.TextJustifyType.InterCharacter,
        "none" => Dom.TextJustifyType.None,
        _ => Dom.TextJustifyType.Auto
    };

    private static Dom.OverflowAnchorType ParseOverflowAnchor(string value) =>
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ? Dom.OverflowAnchorType.None : Dom.OverflowAnchorType.Auto;

    private static float ParseLineHeight(string text, float fontSize)
    {
        if (text == "normal") return fontSize * 1.2f;
        if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num > 10 ? num : num * fontSize;
        return Length.ParseFontSize(text, fontSize);
    }

    private static Length? ParseLength(string text, float defaultVal = 0)
    {
        if (text == "auto" || text == "inherit" || text == "initial") return Dom.AutoLength.Instance;
        if (text == "0") return new Dom.PixelLength(0);
        return Length.Parse(text);
    }

    private static float ParseBorderWidth(string text)
    {
        if (text == "thin") return 1; if (text == "medium") return 3; if (text == "thick") return 5;
        var len = Length.Parse(text);
        return len.ToPixels(16, 16, 1024, 768);
    }

    private static Dom.BorderStyle ParseBorderStyle(string text) => text.ToLowerInvariant() switch
    {
        "solid" => Dom.BorderStyle.Solid, "dashed" => Dom.BorderStyle.Dashed,
        "dotted" => Dom.BorderStyle.Dotted, "double" => Dom.BorderStyle.Double,
        "groove" => Dom.BorderStyle.Groove, "ridge" => Dom.BorderStyle.Ridge,
        "inset" => Dom.BorderStyle.Inset, "outset" => Dom.BorderStyle.Outset,
        _ => Dom.BorderStyle.None
    };

    private static Dom.DisplayType ParseDisplay(string text) => text.ToLowerInvariant() switch
    {
        "none" => Dom.DisplayType.None, "block" => Dom.DisplayType.Block,
        "inline" => Dom.DisplayType.Inline, "inline-block" => Dom.DisplayType.InlineBlock,
        "flex" => Dom.DisplayType.Flex, "inline-flex" => Dom.DisplayType.InlineFlex,
        "grid" => Dom.DisplayType.Grid, "inline-grid" => Dom.DisplayType.InlineGrid,
        "table" => Dom.DisplayType.Table,
        "table-row" => Dom.DisplayType.TableRow, "table-cell" => Dom.DisplayType.TableCell,
        "table-column" => Dom.DisplayType.TableColumn, "table-caption" => Dom.DisplayType.TableCaption,
        "list-item" => Dom.DisplayType.ListItem,
        "contents" => Dom.DisplayType.Contents, _ => Dom.DisplayType.Inline
    };

    private static Dom.PositionType ParsePosition(string text) => text.ToLowerInvariant() switch
    {
        "static" => Dom.PositionType.Static, "relative" => Dom.PositionType.Relative,
        "absolute" => Dom.PositionType.Absolute, "fixed" => Dom.PositionType.Fixed,
        "sticky" => Dom.PositionType.Sticky, _ => Dom.PositionType.Static
    };

    private static Dom.FloatType ParseFloatType(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.FloatType.Left, "right" => Dom.FloatType.Right, _ => Dom.FloatType.None
    };

    private static Dom.ClearType ParseClearType(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.ClearType.Left, "right" => Dom.ClearType.Right,
        "both" => Dom.ClearType.Both, _ => Dom.ClearType.None
    };

    private static Dom.OverflowType ParseOverflow(string text) => text.ToLowerInvariant() switch
    {
        "visible" => Dom.OverflowType.Visible, "hidden" => Dom.OverflowType.Hidden,
        "scroll" => Dom.OverflowType.Scroll, "auto" => Dom.OverflowType.Auto,
        _ => Dom.OverflowType.Visible
    };

    private static Dom.VisibilityType ParseVisibility(string text) => text.ToLowerInvariant() switch
    {
        "hidden" => Dom.VisibilityType.Hidden, "collapse" => Dom.VisibilityType.Collapse,
        _ => Dom.VisibilityType.Visible
    };

    private static Dom.TextAlignType ParseTextAlign(string text) => text.ToLowerInvariant() switch
    {
        "left" => Dom.TextAlignType.Left, "right" => Dom.TextAlignType.Right,
        "center" => Dom.TextAlignType.Center, "justify" => Dom.TextAlignType.Justify,
        "start" => Dom.TextAlignType.Start, "end" => Dom.TextAlignType.End, _ => Dom.TextAlignType.Left
    };

    private static Dom.WhiteSpaceMode ParseWhiteSpace(string text) => text.ToLowerInvariant() switch
    {
        "normal" => Dom.WhiteSpaceMode.Normal, "pre" => Dom.WhiteSpaceMode.Pre,
        "nowrap" => Dom.WhiteSpaceMode.Nowrap, "pre-wrap" => Dom.WhiteSpaceMode.PreWrap,
        "pre-line" => Dom.WhiteSpaceMode.PreLine,
        _ => Dom.WhiteSpaceMode.Normal
    };

    private static Dom.WordBreakMode ParseWordBreak(string text) => text.ToLowerInvariant() switch
    {
        "break-all" => Dom.WordBreakMode.BreakAll,
        "break-word" => Dom.WordBreakMode.BreakWord, _ => Dom.WordBreakMode.Normal
    };

    private static Dom.OverflowWrapMode ParseOverflowWrap(string text) => text.ToLowerInvariant() switch
    {
        "break-word" => Dom.OverflowWrapMode.BreakWord, "anywhere" => Dom.OverflowWrapMode.Anywhere,
        _ => Dom.OverflowWrapMode.Normal
    };

    private static Dom.ListStyleType ParseListStyleType(string text) => text.ToLowerInvariant() switch
    {
        "disc" => Dom.ListStyleType.Disc, "circle" => Dom.ListStyleType.Circle,
        "square" => Dom.ListStyleType.Square, "decimal" => Dom.ListStyleType.Decimal,
        "lower-roman" => Dom.ListStyleType.LowerRoman, "upper-roman" => Dom.ListStyleType.UpperRoman,
        "lower-alpha" => Dom.ListStyleType.LowerAlpha, "upper-alpha" => Dom.ListStyleType.UpperAlpha,
        "none" => Dom.ListStyleType.None, _ => Dom.ListStyleType.Disc
    };

    private static Dom.FlexDirectionType ParseFlexDirection(string text) => text.ToLowerInvariant() switch
    {
        "row" => Dom.FlexDirectionType.Row, "row-reverse" => Dom.FlexDirectionType.RowReverse,
        "column" => Dom.FlexDirectionType.Column, "column-reverse" => Dom.FlexDirectionType.ColumnReverse,
        _ => Dom.FlexDirectionType.Row
    };

    private static Dom.FlexWrapType ParseFlexWrap(string text) => text.ToLowerInvariant() switch
    {
        "nowrap" => Dom.FlexWrapType.NoWrap, "wrap" => Dom.FlexWrapType.Wrap,
        "wrap-reverse" => Dom.FlexWrapType.WrapReverse, _ => Dom.FlexWrapType.NoWrap
    };

    private static Dom.JustifyContentType ParseJustifyContent(string text) => text.ToLowerInvariant() switch
    {
        "flex-start" => Dom.JustifyContentType.FlexStart, "flex-end" => Dom.JustifyContentType.FlexEnd,
        "center" => Dom.JustifyContentType.Center, "space-between" => Dom.JustifyContentType.SpaceBetween,
        "space-around" => Dom.JustifyContentType.SpaceAround,
        "space-evenly" => Dom.JustifyContentType.SpaceEvenly, _ => Dom.JustifyContentType.FlexStart
    };

    private static Dom.AlignItemsType ParseAlignItems(string text) => text.ToLowerInvariant() switch
    {
        "flex-start" => Dom.AlignItemsType.FlexStart, "flex-end" => Dom.AlignItemsType.FlexEnd,
        "center" => Dom.AlignItemsType.Center, "baseline" => Dom.AlignItemsType.Baseline,
        "stretch" => Dom.AlignItemsType.Stretch, _ => Dom.AlignItemsType.Stretch
    };

    private static Dom.AlignSelfType ParseAlignSelf(string text) => text.ToLowerInvariant() switch
    {
        "auto" => Dom.AlignSelfType.Auto, "flex-start" => Dom.AlignSelfType.FlexStart,
        "flex-end" => Dom.AlignSelfType.FlexEnd, "center" => Dom.AlignSelfType.Center,
        "baseline" => Dom.AlignSelfType.Baseline, "stretch" => Dom.AlignSelfType.Stretch,
        _ => Dom.AlignSelfType.Auto
    };

    private static Dom.ObjectFitType ParseObjectFit(string text) => text.ToLowerInvariant() switch
    {
        "fill" => Dom.ObjectFitType.Fill, "contain" => Dom.ObjectFitType.Contain,
        "cover" => Dom.ObjectFitType.Cover, "none" => Dom.ObjectFitType.None,
        "scale-down" => Dom.ObjectFitType.ScaleDown, _ => Dom.ObjectFitType.Fill
    };
}

/// <summary>A matched rule entry with its cascade priority.</summary>
public class MatchedRuleEntry
{
    public CssPropertyValueSet Properties { get; set; } = new();
    public CascadePriority Priority { get; set; }
    public RuleSet.RuleData? RuleData { get; set; }
}

/// <summary>Filter for cascade application, used to skip certain properties.</summary>
public class CascadeFilter
{
    private readonly HashSet<CssPropertyId> _rejected = new();
    public CascadeFilter Add(CssPropertyId id, bool enabled) { if (enabled) _rejected.Add(id); return this; }
    public bool Rejects(CssPropertyName name) => !name.IsCustom && _rejected.Contains(name.Id);
}