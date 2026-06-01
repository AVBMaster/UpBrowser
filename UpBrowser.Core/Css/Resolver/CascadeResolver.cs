using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Css.ElementStyles;

namespace UpBrowser.Core.Css.Resolver;

/// <summary>
/// CSS Cascade resolution system - inspired by Blink's StyleCascade.
/// Two-phase design: Analyze (build CascadeMap) → Apply (build ComputedStyle).
/// Priority encoding: Importance → Origin → TreeOrder → LayerOrder → Position
/// </summary>
public class CascadeResolver
{
    private readonly List<Stylesheet> _stylesheets = new();
    private readonly CssParser _inlineParser = new();
    private readonly SelectorMatcher _matcher = new();
    private readonly CascadeMap _cascadeMap = new();
    private readonly MatchedPropertiesCache _cache = new();

    private ComputedStyle? _rootStyle;
    private int _treeOrderCounter;
    private float _viewportWidth = 1024f;
    private float _viewportHeight = 768f;
    private string _colorScheme = "light";

    public void SetViewport(float width, float height, string colorScheme = "light")
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _colorScheme = colorScheme;
    }

    public void AddStylesheet(Stylesheet stylesheet) => _stylesheets.Add(stylesheet);

    public void ResolveStyles(Document document, float viewportWidth = 1024f, float viewportHeight = 768f, string colorScheme = "light")
    {
        _treeOrderCounter = 0;
        _cache.Clear();
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _colorScheme = colorScheme;

        _rootStyle = CreateUserAgentStyle("html");
        _rootStyle.FontSize = 16;

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
            ResolveElement(root, _rootStyle);
    }

    private void ResolveElement(Element element, ComputedStyle? parentStyle)
    {
        int treeOrder = _treeOrderCounter++;

        var style = new ComputedStyle();

        if (parentStyle != null)
            InheritProperties(style, parentStyle);

        var cacheKey = new CacheKey(element, _stylesheets.Count);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            CopyStyle(style, cached);
            element.ComputedStyle = style;
            ResolveChildren(element, style);
            return;
        }

        _cascadeMap.Clear();

        AnalyzeUAStyles(element, treeOrder);
        Analyze(element, treeOrder);
        AnalyzeInlineStyle(element, treeOrder);
        AnalyzeJsModifiedStyle(element, treeOrder);

        ApplyCascadeAffecting(style);
        ApplyHighPriority(style, parentStyle);
        ApplyMatchResult(style);

        _cache.Set(cacheKey, style.Clone());

        element.ComputedStyle = style;
        ResolveChildren(element, style);
    }

    private void ResolveChildren(Element element, ComputedStyle parentStyle)
    {
        foreach (var child in element.Children)
        {
            if (child is Element childElement)
                ResolveElement(childElement, parentStyle);
        }
    }

    /// <summary>
    /// Analyze phase: traverse all author stylesheet declarations into CascadeMap.
    /// </summary>
    private void Analyze(Element element, int treeOrder)
    {
        foreach (var stylesheet in _stylesheets)
        {
            foreach (var rule in stylesheet.Rules)
            {
                if (_matcher.Matches(rule, element))
                {
                    bool isBefore = rule.Selector.Contains("::before");
                    bool isAfter = rule.Selector.Contains("::after");

                    if (isBefore)
                    {
                        element.BeforeStyles ??= new Dictionary<string, string>();
                        foreach (var prop in rule.Properties)
                            element.BeforeStyles[prop.Key] = prop.Value;
                    }
                    else if (isAfter)
                    {
                        element.AfterStyles ??= new Dictionary<string, string>();
                        foreach (var prop in rule.Properties)
                            element.AfterStyles[prop.Key] = prop.Value;
                    }
                    else
                    {
                        var expandedProps = ShorthandExpander.Expand(rule.Properties);
                        foreach (var prop in expandedProps)
                        {
                            bool isImportant = rule.IsPropertyImportant(prop.Key) || rule.IsPropertyImportant(GetOriginalShorthand(prop.Key));
                            var priority = new CascadePriority(
                                importance: isImportant,
                                origin: CascadeOrigin.Author,
                                treeOrder: treeOrder
                            );
                            _cascadeMap.Insert(prop.Key, prop.Value, priority);

                            if (prop.Key.StartsWith("--"))
                                CssFunctionEvaluator.SetCustomProperty(prop.Key[2..], prop.Value);
                        }
                    }
                }
            }

            foreach (var mediaRule in stylesheet.MediaRules)
            {
                if (MediaQueryEvaluator.Evaluate(mediaRule.Condition, _viewportWidth, _viewportHeight, _colorScheme))
                {
                    foreach (var rule in mediaRule.Rules)
                    {
                        if (_matcher.Matches(rule, element))
                        {
                            bool isBefore = rule.Selector.Contains("::before");
                            bool isAfter = rule.Selector.Contains("::after");

                            if (isBefore)
                            {
                                element.BeforeStyles ??= new Dictionary<string, string>();
                                foreach (var prop in rule.Properties)
                                    element.BeforeStyles[prop.Key] = prop.Value;
                            }
                            else if (isAfter)
                            {
                                element.AfterStyles ??= new Dictionary<string, string>();
                                foreach (var prop in rule.Properties)
                                    element.AfterStyles[prop.Key] = prop.Value;
                            }
                            else
                            {
                                var expandedProps = ShorthandExpander.Expand(rule.Properties);
                                foreach (var prop in expandedProps)
                                {
                                    bool isImportant = rule.IsPropertyImportant(prop.Key) || rule.IsPropertyImportant(GetOriginalShorthand(prop.Key));
                                    var priority = new CascadePriority(
                                        importance: isImportant,
                                        origin: CascadeOrigin.Author,
                                        treeOrder: treeOrder
                                    );
                                    _cascadeMap.Insert(prop.Key, prop.Value, priority);

                                    if (prop.Key.StartsWith("--"))
                                        CssFunctionEvaluator.SetCustomProperty(prop.Key[2..], prop.Value);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void AnalyzeUAStyles(Element element, int treeOrder)
    {
        var style = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(style, element.TagName);

        var props = new Dictionary<string, string>();
        CollectNonDefaultProperties(style, props);

        foreach (var kv in props)
        {
            var priority = new CascadePriority(false, CascadeOrigin.UserAgent, treeOrder);
            _cascadeMap.Insert(kv.Key, kv.Value, priority);
        }
    }

    private void CollectNonDefaultProperties(ComputedStyle style, Dictionary<string, string> props)
    {
        var def = new ComputedStyle();
        if (style.Display != def.Display) props["display"] = style.Display.ToCssString();
        if (style.FontSize != def.FontSize) props["font-size"] = $"{style.FontSize}px";
        if (style.FontWeight != def.FontWeight) props["font-weight"] = style.FontWeight == FontWeight.Bold ? "700" : "400";
        if (style.FontStyle != def.FontStyle) props["font-style"] = style.FontStyle == FontStyleType.Italic ? "italic" : "normal";
        if (style.FontFamily != def.FontFamily) props["font-family"] = style.FontFamily;
        if (style.LineHeight != def.LineHeight) props["line-height"] = style.LineHeight.ToString("0.000");
        if (style.Color != def.Color) props["color"] = $"rgba({style.Color.Red},{style.Color.Green},{style.Color.Blue},{style.Color.Alpha / 255f})";
        if (style.BackgroundColor != def.BackgroundColor && style.BackgroundColor.HasValue) props["background-color"] = $"rgba({style.BackgroundColor.Value.Red},{style.BackgroundColor.Value.Green},{style.BackgroundColor.Value.Blue},{style.BackgroundColor.Value.Alpha / 255f})";
        AddLengthProp(props, style.MarginTop, "margin-top", def.MarginTop);
        AddLengthProp(props, style.MarginBottom, "margin-bottom", def.MarginBottom);
        AddLengthProp(props, style.MarginLeft, "margin-left", def.MarginLeft);
        AddLengthProp(props, style.MarginRight, "margin-right", def.MarginRight);
        AddLengthProp(props, style.PaddingTop, "padding-top", def.PaddingTop);
        AddLengthProp(props, style.PaddingBottom, "padding-bottom", def.PaddingBottom);
        AddLengthProp(props, style.PaddingLeft, "padding-left", def.PaddingLeft);
        AddLengthProp(props, style.PaddingRight, "padding-right", def.PaddingRight);
        if (style.WhiteSpace != def.WhiteSpace) props["white-space"] = style.WhiteSpace.ToString().ToLowerInvariant();
        if (style.ListStyleType != def.ListStyleType) props["list-style-type"] = style.ListStyleType.ToString().ToLowerInvariant();
        if (style.BorderCollapse != def.BorderCollapse) props["border-collapse"] = style.BorderCollapse ? "collapse" : "separate";
        if (!string.IsNullOrEmpty(style.BackgroundImage)) props["background-image"] = style.BackgroundImage;
        if (style.TextAlign != def.TextAlign) props["text-align"] = style.TextAlign.ToString().ToLowerInvariant();
        if (style.TextDecoration != def.TextDecoration) props["text-decoration"] = style.TextDecoration.ToString().ToLowerInvariant();
        if (style.VerticalAlign != def.VerticalAlign) props["vertical-align"] = style.VerticalAlign.ToString().ToLowerInvariant();
        if (style.Overflow != def.Overflow) props["overflow"] = style.Overflow.ToString().ToLowerInvariant();
        if (style.Position != def.Position) props["position"] = style.Position.ToString().ToLowerInvariant();
        if (style.Float != def.Float) props["float"] = style.Float.ToString().ToLowerInvariant();
        if (style.Clear != def.Clear) props["clear"] = style.Clear.ToString().ToLowerInvariant();
        if (style.BoxSizing != def.BoxSizing) props["box-sizing"] = style.BoxSizing == BoxSizingType.BorderBox ? "border-box" : "content-box";
        if (style.Cursor != def.Cursor) props["cursor"] = style.Cursor ?? "auto";
    }

    private static void AddLengthProp(Dictionary<string, string> props, Length length, string name, Length defaultLength)
    {
        if (length == null || length == defaultLength || length is AutoLength) return;
        if (length is PixelLength px) { if (px.Value != 0) props[name] = $"{px.Value}px"; }
        else { props[name] = length.ToString(); }
    }

    private void AnalyzeInlineStyle(Element element, int treeOrder)
    {
        var inlineStyleAttr = element.GetAttribute("style");
        if (string.IsNullOrEmpty(inlineStyleAttr)) return;

        var inlineProps = _inlineParser.ParseInlineStyle(inlineStyleAttr);
        var expandedProps = ShorthandExpander.Expand(inlineProps);
        foreach (var prop in expandedProps)
        {
            var priority = new CascadePriority(false, CascadeOrigin.Inline, treeOrder);
            _cascadeMap.Insert(prop.Key, prop.Value, priority);
        }
    }

    private void AnalyzeJsModifiedStyle(Element element, int treeOrder)
    {
        foreach (var kv in element.Style)
        {
            if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
            {
                var priority = new CascadePriority(false, CascadeOrigin.JsModified, treeOrder);
                _cascadeMap.Insert(kv.Key, kv.Value, priority);
            }
        }
    }

    /// <summary>
    /// Apply cascade-affecting properties first (direction, writing-mode, zoom).
    /// These affect how other properties are resolved.
    /// </summary>
    private void ApplyCascadeAffecting(ComputedStyle style)
    {
        if (_cascadeMap.TryGetValue("direction", out var dirVal))
            style.Direction = dirVal.ToLowerInvariant() == "rtl" ? "rtl" : "ltr";
        if (_cascadeMap.TryGetValue("writing-mode", out var wmVal))
            style.WritingMode = ParseWritingMode(wmVal);
        if (_cascadeMap.TryGetValue("zoom", out var zoomVal))
            style.Zoom = ParseZoom(zoomVal);
    }

    /// <summary>
    /// Apply high-priority properties first (font properties).
    /// em/ch units depend on font-size, so font must be resolved first.
    /// </summary>
    private void ApplyHighPriority(ComputedStyle style, ComputedStyle? parentStyle)
    {
        if (_cascadeMap.TryGetValue("font-size", out var fontSizeVal))
            style.FontSize = ParseFontSize(fontSizeVal, parentStyle);

        if (_cascadeMap.TryGetValue("font-weight", out var fontWeightVal))
            style.FontWeight = ParseFontWeight(fontWeightVal);

        if (_cascadeMap.TryGetValue("font-style", out var fontStyleVal))
            style.FontStyle = ParseFontStyle(fontStyleVal);

        if (_cascadeMap.TryGetValue("font-family", out var fontFamilyVal))
            style.FontFamily = ParseFontFamily(fontFamilyVal);

        if (_cascadeMap.TryGetValue("line-height", out var lineHeightVal))
            style.LineHeight = ParseLineHeight(lineHeightVal, style.FontSize);
    }

    /// <summary>
    /// Apply all remaining cascade-winning declarations.
    /// </summary>
    private void ApplyMatchResult(ComputedStyle style)
    {
        foreach (var (name, value) in _cascadeMap.GetAll())
        {
            if (IsHighPriorityProperty(name)) continue;
            ApplyProperty(style, name, value);
        }
    }

    private void ApplyProperty(ComputedStyle style, string name, string value)
    {
        switch (name)
        {
            case "width": style.Width = Length.Parse(value); break;
            case "height": style.Height = Length.Parse(value); break;
            case "min-width": style.MinWidth = Length.Parse(value); break;
            case "min-height": style.MinHeight = Length.Parse(value); break;
            case "max-width": style.MaxWidth = Length.Parse(value); break;
            case "max-height": style.MaxHeight = Length.Parse(value); break;
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
            case "margin-block": ParseShorthand2(value, out var mbt, out var mbb); style.MarginTop = mbt; style.MarginBottom = mbb; break;
            case "margin-inline": ParseShorthand2(value, out var mil, out var mir); style.MarginLeft = mil; style.MarginRight = mir; break;
            case "margin-block-start": style.MarginTop = Length.Parse(value); break;
            case "margin-block-end": style.MarginBottom = Length.Parse(value); break;
            case "margin-inline-start": style.MarginLeft = Length.Parse(value); break;
            case "margin-inline-end": style.MarginRight = Length.Parse(value); break;
            case "padding":
                ParseShorthand4(value, out var pt, out var pr, out var pb, out var pl);
                style.PaddingTop = pt; style.PaddingRight = pr; style.PaddingBottom = pb; style.PaddingLeft = pl;
                break;
            case "padding-top": style.PaddingTop = Length.Parse(value); break;
            case "padding-bottom": style.PaddingBottom = Length.Parse(value); break;
            case "padding-left": style.PaddingLeft = Length.Parse(value); break;
            case "padding-right": style.PaddingRight = Length.Parse(value); break;
            case "padding-block": ParseShorthand2(value, out var pbt, out var pbb); style.PaddingTop = pbt; style.PaddingBottom = pbb; break;
            case "padding-inline": ParseShorthand2(value, out var pil, out var pir); style.PaddingLeft = pil; style.PaddingRight = pir; break;
            case "padding-block-start": style.PaddingTop = Length.Parse(value); break;
            case "padding-block-end": style.PaddingBottom = Length.Parse(value); break;
            case "padding-inline-start": style.PaddingLeft = Length.Parse(value); break;
            case "padding-inline-end": style.PaddingRight = Length.Parse(value); break;
            case "color": style.Color = ColorParser.Parse(value); break;
            case "accent-color": style.AccentColor = value == "auto" ? null : ColorParser.Parse(value); break;
            case "caret-color": style.CaretColor = value == "auto" ? null : ColorParser.Parse(value); break;
            case "color-scheme":
                var cs = value.ToLowerInvariant();
                style.ColorScheme = cs switch { "light" => "light", "dark" => "dark", "light dark" => "light dark", _ => "normal" };
                break;
            case "forced-color-adjust": style.ForcedColorAdjust = value.ToLowerInvariant() == "none" ? ForcedColorAdjustType.None : ForcedColorAdjustType.Auto; break;
            case "background": ParseBackgroundShorthand(value, style); break;
            case "background-color": style.BackgroundColor = ColorParser.Parse(value); break;
            case "background-image":
                if (value == "none")
                    style.BackgroundImage = null;
                else if (CssFunctionEvaluator.IsGradient(value))
                    style.BackgroundImage = value;
                else
                    style.BackgroundImage = ParseUrl(value);
                break;
            case "background-repeat": style.BackgroundRepeat = ParseBackgroundRepeat(value); break;
            case "background-position": ParseBackgroundPosition(value, style); break;
            case "background-position-x": style.BackgroundPositionX = ParsePositionKeywordOrLength(value); break;
            case "background-position-y": style.BackgroundPositionY = ParsePositionKeywordOrLength(value); break;
            case "background-size": ParseBackgroundSize(value, style); break;
            case "background-attachment": style.BackgroundAttachment = ParseBackgroundAttachment(value); break;
            case "background-clip": style.BackgroundClip = value.ToLowerInvariant(); break;
            case "background-origin": style.BackgroundOrigin = value.ToLowerInvariant(); break;
            case "background-blend-mode": style.BackgroundBlendMode = ParseBackgroundBlendMode(value); break;
            case "text-align": style.TextAlign = ParseTextAlign(value); break;
            case "text-decoration": ParseTextDecorationShorthand(value, style); break;
            case "text-decoration-line": style.TextDecorationLine = ParseTextDecorationLine(value); break;
            case "text-decoration-style": style.TextDecorationStyle = ParseTextDecorationStyle(value); break;
            case "text-decoration-color": style.TextDecorationColor = ColorParser.Parse(value); break;
            case "text-decoration-thickness":
                if (value == "auto") style.TextDecorationThickness = 0;
                else if (Length.TryParse(value, out var tdt)) style.TextDecorationThickness = tdt.ToPixels(0, 0, 0, 0);
                break;
            case "text-underline-offset":
                if (value == "auto") style.TextUnderlineOffset = 0;
                else if (Length.TryParse(value, out var tuo)) style.TextUnderlineOffset = tuo.ToPixels(0, 0, 0, 0);
                break;
            case "text-emphasis": style.TextEmphasis = value; break;
            case "text-emphasis-color": style.TextEmphasisColor = value; break;
            case "text-emphasis-style": style.TextEmphasisStyle = value; break;
            case "text-shadow": style.TextShadow = ParseTextShadow(value); break;
            case "text-overflow": style.TextOverflow = value.ToLowerInvariant() == "ellipsis" ? TextOverflowType.Ellipsis : TextOverflowType.Clip; break;
            case "vertical-align": style.VerticalAlign = ParseVerticalAlign(value); break;
            case "white-space": style.WhiteSpace = ParseWhiteSpace(value); break;
            case "word-break": style.WordBreak = ParseWordBreak(value); break;
            case "overflow-wrap": case "word-wrap": style.OverflowWrap = ParseOverflowWrap(value); break;
            case "visibility": style.Visibility = ParseVisibility(value); break;
            case "overflow":
                var overflow = ParseOverflow(value);
                style.Overflow = overflow; style.OverflowX = overflow; style.OverflowY = overflow;
                break;
            case "overflow-x": style.OverflowX = ParseOverflow(value); break;
            case "overflow-y": style.OverflowY = ParseOverflow(value); break;
            case "overflow-anchor": style.OverflowAnchor = value.ToLowerInvariant() == "none" ? OverflowAnchorType.None : OverflowAnchorType.Auto; break;
            case "overscroll-behavior": style.OverscrollBehavior = ParseOverscrollBehavior(value); style.OverscrollBehaviorX = style.OverscrollBehavior; style.OverscrollBehaviorY = style.OverscrollBehavior; break;
            case "overscroll-behavior-x": style.OverscrollBehaviorX = ParseOverscrollBehavior(value); break;
            case "overscroll-behavior-y": style.OverscrollBehaviorY = ParseOverscrollBehavior(value); break;
            case "z-index": if (value != "auto") style.ZIndex = int.TryParse(value, out var z) ? z : null; break;
            case "border": ParseBorderShorthand(value, style); break;
            case "border-top": ParseBorderSide(style, "top", value); break;
            case "border-bottom": ParseBorderSide(style, "bottom", value); break;
            case "border-left": ParseBorderSide(style, "left", value); break;
            case "border-right": ParseBorderSide(style, "right", value); break;
            case "border-width": ParseBorderWidth(value, style); break;
            case "border-color": ParseBorderColor(value, style); break;
            case "border-style": ParseBorderStyle(value, style); break;
            case "border-radius": ParseBorderRadius(value, style); break;
            case "border-top-left-radius": style.BorderTopLeftRadius = ParseSize(value) ?? 0; break;
            case "border-top-right-radius": style.BorderTopRightRadius = ParseSize(value) ?? 0; break;
            case "border-bottom-left-radius": style.BorderBottomLeftRadius = ParseSize(value) ?? 0; break;
            case "border-bottom-right-radius": style.BorderBottomRightRadius = ParseSize(value) ?? 0; break;
            case "border-collapse": style.BorderCollapse = value.ToLowerInvariant() == "collapse"; break;
            case "border-spacing": style.BorderSpacing = ParseSize(value) ?? 0; break;
            case "border-image": style.BorderImageSource = ParseUrl(value); break;
            case "border-image-source": style.BorderImageSource = ParseUrl(value); break;
            case "border-image-slice": style.BorderImageSlice = value; break;
            case "border-image-width": style.BorderImageWidth = value; break;
            case "border-image-repeat": style.BorderImageRepeat = value; break;
            case "border-image-outset": style.BorderImageOutset = value; break;
            case "box-sizing": style.BoxSizing = value.Contains("border") ? BoxSizingType.BorderBox : BoxSizingType.ContentBox; break;
            case "opacity": if (float.TryParse(value, out var o)) style.Opacity = Math.Clamp(o, 0, 1); break;
            case "box-shadow": style.BoxShadow = ParseBoxShadow(value); break;
            case "flex-direction": style.FlexDirection = ParseFlexDirection(value); break;
            case "flex-wrap": style.FlexWrap = ParseFlexWrap(value); break;
            case "flex-grow": if (float.TryParse(value, out var g)) style.FlexGrow = g; break;
            case "flex-shrink": if (float.TryParse(value, out var s)) style.FlexShrink = s; break;
            case "flex-basis": style.FlexBasis = Length.Parse(value); break;
            case "flex": ParseFlexShorthand(value, style); break;
            case "flex-flow": style.FlexFlow = value; ParseFlexFlow(value, style); break;
            case "order": if (int.TryParse(value, out var ord)) style.Order = ord; break;
            case "justify-content": style.JustifyContent = ParseJustifyContent(value); break;
            case "justify-items": style.JustifyItems = value.ToLowerInvariant(); break;
            case "justify-self": style.JustifySelf = value.ToLowerInvariant(); break;
            case "align-items": style.AlignItems = ParseAlignItems(value); break;
            case "align-self": style.AlignSelf = ParseAlignSelf(value); break;
            case "align-content": style.AlignContent = value.ToLowerInvariant(); break;
            case "place-content": style.PlaceContent = value.ToLowerInvariant(); break;
            case "place-items": style.PlaceItems = value.ToLowerInvariant(); break;
            case "place-self": style.PlaceSelf = value.ToLowerInvariant(); break;
            case "gap": ParseGap(value, style); break;
            case "row-gap": if (Length.TryParse(value, out var rg)) style.RowGap = rg; break;
            case "column-gap": if (Length.TryParse(value, out var cg)) style.ColumnGap = cg; break;
            case "column-count": if (int.TryParse(value, out var cc)) style.ColumnCount = cc; break;
            case "column-width": if (Length.TryParse(value, out var cw)) style.ColumnWidth = cw; break;
            case "grid": style.Grid = value; break;
            case "grid-template": ParseGridTemplateShorthand(value, style); break;
            case "grid-template-columns": style.GridTemplateColumns = value == "none" ? null : value; break;
            case "grid-template-rows": style.GridTemplateRows = value == "none" ? null : value; break;
            case "grid-template-areas": style.GridTemplateAreas = value == "none" ? null : value; break;
            case "grid-auto-columns": style.GridAutoColumns = value; break;
            case "grid-auto-rows": style.GridAutoRows = value; break;
            case "grid-auto-flow": style.GridAutoFlow = ParseGridAutoFlow(value); break;
            case "grid-column": style.GridColumn = value; if (value.Contains("/")) { var parts = value.Split('/'); style.GridColumnStart = parts[0].Trim(); style.GridColumnEnd = parts.Length > 1 ? parts[1].Trim() : null; } break;
            case "grid-column-start": style.GridColumnStart = value; break;
            case "grid-column-end": style.GridColumnEnd = value; break;
            case "grid-row": style.GridRow = value; if (value.Contains("/")) { var parts = value.Split('/'); style.GridRowStart = parts[0].Trim(); style.GridRowEnd = parts.Length > 1 ? parts[1].Trim() : null; } break;
            case "grid-row-start": style.GridRowStart = value; break;
            case "grid-row-end": style.GridRowEnd = value; break;
            case "grid-area": style.GridArea = value; break;
            case "top": style.Top = Length.Parse(value); break;
            case "bottom": style.Bottom = Length.Parse(value); break;
            case "left": style.Left = Length.Parse(value); break;
            case "right": style.Right = Length.Parse(value); break;
            case "inset": ParseInsetShorthand(value, style); break;
            case "inset-block": ParseShorthand2(value, out var ibt, out var ibb); style.Top = ibt; style.Bottom = ibb; break;
            case "inset-inline": ParseShorthand2(value, out var iil, out var iir); style.Left = iil; style.Right = iir; break;
            case "inset-block-start": style.Top = Length.Parse(value); break;
            case "inset-block-end": style.Bottom = Length.Parse(value); break;
            case "inset-inline-start": style.Left = Length.Parse(value); break;
            case "inset-inline-end": style.Right = Length.Parse(value); break;
            case "list-style-type": style.ListStyleType = ParseListStyleType(value); break;
            case "list-style-position": style.ListStylePosition = value.Contains("inside") ? ListStylePosition.Inside : ListStylePosition.Outside; break;
            case "list-style-image": style.ListStyleImage = value == "none" ? null : ParseUrl(value); break;
            case "list-style": ParseListStyle(value, style); break;
            case "cursor": style.Cursor = value; break;
            case "transform": style.Transform = value; break;
            case "transform-origin": style.TransformOrigin = value; break;
            case "transition": style.Transition = value; break;
            case "transition-delay": style.TransitionDelay = value; break;
            case "transition-duration": style.TransitionDuration = value; break;
            case "transition-property": style.TransitionProperty = value; break;
            case "transition-timing-function": style.TransitionTimingFunction = value; break;
            case "animation": style.Animation = value; break;
            case "animation-name": style.AnimationName = value; break;
            case "animation-duration": style.AnimationDuration = value; break;
            case "animation-timing-function": style.AnimationTimingFunction = value; break;
            case "animation-delay": style.AnimationDelay = value; break;
            case "animation-iteration-count": style.AnimationIterationCount = value; break;
            case "animation-direction": style.AnimationDirection = value; break;
            case "animation-fill-mode": style.AnimationFillMode = value; break;
            case "animation-play-state": style.AnimationPlayState = value; break;
            case "pointer-events": style.PointerEvents = value; break;
            case "user-select": style.UserSelect = value; break;
            case "text-indent":
                if (Length.TryParse(value, out var ti))
                    style.TextIndent = ti.ToPixels(0, 0, 0, 0);
                break;
            case "letter-spacing":
                if (value == "normal") style.LetterSpacing = 0;
                else if (Length.TryParse(value, out var ls))
                    style.LetterSpacing = ls.ToPixels(0, 0, 0, 0);
                break;
            case "word-spacing":
                if (value == "normal") style.WordSpacing = 0;
                else if (Length.TryParse(value, out var ws))
                    style.WordSpacing = ws.ToPixels(0, 0, 0, 0);
                break;
            case "direction": style.Direction = value.ToLowerInvariant() == "rtl" ? "rtl" : "ltr"; break;
            case "unicode-bidi": style.UnicodeBidi = value.ToLowerInvariant(); break;
            case "writing-mode": style.WritingMode = ParseWritingMode(value); break;
            case "text-orientation": break; // recognized but minimal handling
            case "text-transform": style.TextTransform = value.ToLowerInvariant(); break;
            case "text-rendering": style.TextRendering = value.ToLowerInvariant(); break;
            case "font":
                ParseFontShorthand(value, style);
                break;
            case "font-family": style.FontFamily = ParseFontFamily(value); break;
            case "font-size": break; // handled in high-priority
            case "font-weight": break; // handled in high-priority
            case "font-style": break; // handled in high-priority
            case "line-height": break; // handled in high-priority
            case "font-variant": style.FontVariant = value.ToLowerInvariant(); break;
            case "font-stretch": style.FontStretch = value.ToLowerInvariant(); break;
            case "font-kerning": style.FontKerning = value.ToLowerInvariant(); break;
            case "font-synthesis": style.FontSynthesis = value.ToLowerInvariant(); break;
            case "font-optical-sizing": style.FontOpticalSizing = value.ToLowerInvariant(); break;
            case "font-variation-settings": style.FontVariationSettings = value; break;
            case "font-feature-settings": style.FontFeatureSettings = value; break;
            case "font-size-adjust": if (value != "none" && float.TryParse(value, out var fsa)) style.FontSizeAdjust = fsa; break;
            case "outline": ParseOutlineShorthand(value, style); break;
            case "outline-width":
                if (float.TryParse(value.Replace("px", ""), out var ow))
                    style.OutlineWidth = ow;
                break;
            case "outline-color": style.OutlineColor = ColorParser.Parse(value); break;
            case "outline-style": style.OutlineStyle = ParseBorderStyleValue(value); break;
            case "outline-offset": style.OutlineOffset = ParseSize(value) ?? 0; break;
            case "table-layout": style.TableLayout = value.ToLowerInvariant() == "fixed" ? "fixed" : "auto"; break;
            case "caption-side": style.CaptionSide = value.ToLowerInvariant() == "bottom" ? "bottom" : "top"; break;
            case "empty-cells": style.EmptyCells = value.ToLowerInvariant() == "hide" ? "hide" : "show"; break;
            case "content": style.Content = value; break;
            case "counter-increment": style.CounterIncrement = value; break;
            case "counter-reset": style.CounterReset = value; break;
            case "counter-set": style.CounterSet = value; break;
            case "quotes": style.Quotes = value; break;
            case "aspect-ratio":
                if (value == "auto") style.AspectRatio = 0;
                else if (float.TryParse(value, out var ar)) style.AspectRatio = ar;
                break;
            case "object-fit": style.ObjectFit = ParseObjectFit(value); break;
            case "object-position": ParsePosition(value, out var opx, out var opy); style.ObjectPositionX = opx; style.ObjectPositionY = opy; break;
            case "filter": style.Filter = value; break;
            case "backdrop-filter": style.BackdropFilter = value; break;
            case "clip-path": style.ClipPath = value; break;
            case "mask": style.Mask = value; break;
            case "mask-image": style.MaskImage = value; break;
            case "mask-clip": style.MaskClip = value; break;
            case "mask-composite": style.MaskComposite = value; break;
            case "mask-mode": style.MaskMode = value; break;
            case "mask-origin": style.MaskOrigin = value; break;
            case "mask-position": style.MaskPosition = value; break;
            case "mask-repeat": style.MaskRepeat = value; break;
            case "mask-size": style.MaskSize = value; break;
            case "isolation": style.Isolation = value.ToLowerInvariant() == "isolate" ? IsolationType.Isolate : IsolationType.Auto; break;
            case "mix-blend-mode": style.MixBlendMode = ParseMixBlendMode(value); break;
            case "image-rendering": style.ImageRendering = ParseImageRendering(value); break;
            case "contain": style.Contain = ParseContain(value); break;
            case "content-visibility": style.ContentVisibility = ParseContentVisibility(value); break;
            case "will-change": style.WillChange = value; break;
            case "scroll-behavior": style.ScrollBehavior = value.ToLowerInvariant() == "smooth" ? ScrollBehaviorType.Smooth : ScrollBehaviorType.Auto; break;
            case "tab-size": if (float.TryParse(value.Replace("px", ""), out var ts)) style.TabSize = ts; break;
            case "hyphens": style.Hyphens = ParseHyphens(value); break;
            case "line-break": style.LineBreak = ParseLineBreak(value); break;
            case "text-justify": style.TextJustify = ParseTextJustify(value); break;
            case "hanging-punctuation": style.HangingPunctuation = value.ToLowerInvariant(); break;
            case "resize": style.Resize = ParseResize(value); break;
            case "zoom": style.Zoom = ParseZoom(value); break;
            case "all": break; // all shorthand - handled via reset cascade
            case "initial-letter": break; // recognized, minimal handling
            case "box-decoration-break": break; // recognized, minimal handling
            case "page-break-after": break;
            case "page-break-before": break;
            case "page-break-inside": break;
            case "orphans": break;
            case "widows": break;
        }
    }

    private bool IsHighPriorityProperty(string name) => name switch
    {
        "font-size" or "font-weight" or "font-style" or "font-family" or "line-height" => true,
        _ => false
    };

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
        child.WordBreak = parent.WordBreak;
        child.OverflowWrap = parent.OverflowWrap;
        child.Visibility = parent.Visibility;
        child.Cursor = parent.Cursor;
        child.Direction = parent.Direction;
        child.LetterSpacing = parent.LetterSpacing;
        child.WordSpacing = parent.WordSpacing;
        child.TextIndent = parent.TextIndent;
        child.TextTransform = parent.TextTransform;
        child.CaptionSide = parent.CaptionSide;
        child.EmptyCells = parent.EmptyCells;
        child.WritingMode = parent.WritingMode;
        child.Orphans = parent.Orphans;
        child.Widows = parent.Widows;
        child.Hyphens = parent.Hyphens;
        child.LineBreak = parent.LineBreak;
        child.TextJustify = parent.TextJustify;
        child.TextRendering = parent.TextRendering;
        child.TextShadow = new List<TextShadowValue>(parent.TextShadow);
        child.TextDecorationLine = parent.TextDecorationLine;
        child.TextDecorationStyle = parent.TextDecorationStyle;
        child.TextDecorationColor = parent.TextDecorationColor;
        child.TextEmphasis = parent.TextEmphasis;
        child.TextEmphasisColor = parent.TextEmphasisColor;
        child.TextEmphasisStyle = parent.TextEmphasisStyle;
        child.FontVariant = parent.FontVariant;
        child.FontKerning = parent.FontKerning;
        child.FontStretch = parent.FontStretch;
        child.FontSynthesis = parent.FontSynthesis;
        child.FontOpticalSizing = parent.FontOpticalSizing;
        child.FontVariationSettings = parent.FontVariationSettings;
        child.FontFeatureSettings = parent.FontFeatureSettings;
        child.FontSizeAdjust = parent.FontSizeAdjust;
        child.Quotes = parent.Quotes;
        child.ImageRendering = parent.ImageRendering;
        child.AccentColor = parent.AccentColor;
        child.CaretColor = parent.CaretColor;
        child.ColorScheme = parent.ColorScheme;
        child.ForcedColorAdjust = parent.ForcedColorAdjust;
    }

    private ComputedStyle CreateUserAgentStyle(string tagName)
    {
        var style = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(style, tagName);
        return style;
    }

    private void CopyStyle(ComputedStyle dest, ComputedStyle src)
    {
        dest.Width = src.Width; dest.Height = src.Height;
        dest.MinWidth = src.MinWidth; dest.MinHeight = src.MinHeight;
        dest.MaxWidth = src.MaxWidth; dest.MaxHeight = src.MaxHeight;
        dest.Display = src.Display; dest.Position = src.Position;
        dest.Float = src.Float; dest.Clear = src.Clear;
        dest.MarginTop = src.MarginTop; dest.MarginRight = src.MarginRight;
        dest.MarginBottom = src.MarginBottom; dest.MarginLeft = src.MarginLeft;
        dest.PaddingTop = src.PaddingTop; dest.PaddingRight = src.PaddingRight;
        dest.PaddingBottom = src.PaddingBottom; dest.PaddingLeft = src.PaddingLeft;
        dest.Color = src.Color; dest.BackgroundColor = src.BackgroundColor;
        dest.BackgroundImage = src.BackgroundImage;
        dest.BackgroundRepeat = src.BackgroundRepeat;
        dest.BackgroundPositionX = src.BackgroundPositionX;
        dest.BackgroundPositionY = src.BackgroundPositionY;
        dest.BackgroundSize = src.BackgroundSize;
        dest.BackgroundSizeWidth = src.BackgroundSizeWidth;
        dest.BackgroundSizeHeight = src.BackgroundSizeHeight;
        dest.BackgroundAttachment = src.BackgroundAttachment;
        dest.BackgroundClip = src.BackgroundClip;
        dest.BackgroundOrigin = src.BackgroundOrigin;
        dest.BackgroundBlendMode = src.BackgroundBlendMode;
        dest.FontFamily = src.FontFamily; dest.FontSize = src.FontSize;
        dest.FontWeight = src.FontWeight; dest.FontStyle = src.FontStyle;
        dest.LineHeight = src.LineHeight;
        dest.TextAlign = src.TextAlign;
        dest.TextDecoration = src.TextDecoration;
        dest.TextDecorationLine = src.TextDecorationLine;
        dest.TextDecorationStyle = src.TextDecorationStyle;
        dest.TextDecorationColor = src.TextDecorationColor;
        dest.TextDecorationThickness = src.TextDecorationThickness;
        dest.TextUnderlineOffset = src.TextUnderlineOffset;
        dest.VerticalAlign = src.VerticalAlign;
        dest.WhiteSpace = src.WhiteSpace;
        dest.WordBreak = src.WordBreak;
        dest.OverflowWrap = src.OverflowWrap;
        dest.Visibility = src.Visibility;
        dest.Overflow = src.Overflow;
        dest.OverflowX = src.OverflowX; dest.OverflowY = src.OverflowY;
        dest.OverflowAnchor = src.OverflowAnchor;
        dest.OverscrollBehavior = src.OverscrollBehavior;
        dest.OverscrollBehaviorX = src.OverscrollBehaviorX;
        dest.OverscrollBehaviorY = src.OverscrollBehaviorY;
        dest.ZIndex = src.ZIndex;
        dest.BorderTopWidth = src.BorderTopWidth;
        dest.BorderRightWidth = src.BorderRightWidth;
        dest.BorderBottomWidth = src.BorderBottomWidth;
        dest.BorderLeftWidth = src.BorderLeftWidth;
        dest.BorderTopColor = src.BorderTopColor;
        dest.BorderRightColor = src.BorderRightColor;
        dest.BorderBottomColor = src.BorderBottomColor;
        dest.BorderLeftColor = src.BorderLeftColor;
        dest.BorderTopStyle = src.BorderTopStyle;
        dest.BorderRightStyle = src.BorderRightStyle;
        dest.BorderBottomStyle = src.BorderBottomStyle;
        dest.BorderLeftStyle = src.BorderLeftStyle;
        dest.BorderTopLeftRadius = src.BorderTopLeftRadius;
        dest.BorderTopRightRadius = src.BorderTopRightRadius;
        dest.BorderBottomRightRadius = src.BorderBottomRightRadius;
        dest.BorderBottomLeftRadius = src.BorderBottomLeftRadius;
        dest.BorderCollapse = src.BorderCollapse;
        dest.BorderSpacing = src.BorderSpacing;
        dest.BoxSizing = src.BoxSizing;
        dest.Opacity = src.Opacity;
        dest.BoxShadow = src.BoxShadow;
        dest.FlexDirection = src.FlexDirection;
        dest.FlexWrap = src.FlexWrap;
        dest.FlexGrow = src.FlexGrow;
        dest.FlexShrink = src.FlexShrink;
        dest.FlexBasis = src.FlexBasis;
        dest.FlexFlow = src.FlexFlow;
        dest.JustifyContent = src.JustifyContent;
        dest.AlignItems = src.AlignItems;
        dest.AlignSelf = src.AlignSelf;
        dest.AlignContent = src.AlignContent;
        dest.JustifyItems = src.JustifyItems;
        dest.JustifySelf = src.JustifySelf;
        dest.PlaceContent = src.PlaceContent;
        dest.PlaceItems = src.PlaceItems;
        dest.PlaceSelf = src.PlaceSelf;
        dest.Order = src.Order;
        dest.Top = src.Top; dest.Bottom = src.Bottom;
        dest.Left = src.Left; dest.Right = src.Right;
        dest.ListStyleType = src.ListStyleType;
        dest.ListStylePosition = src.ListStylePosition;
        dest.ListStyleImage = src.ListStyleImage;
        dest.Cursor = src.Cursor;
        dest.Transform = src.Transform; dest.TransformOrigin = src.TransformOrigin;
        dest.Transition = src.Transition; dest.TransitionDelay = src.TransitionDelay;
        dest.TransitionDuration = src.TransitionDuration; dest.TransitionProperty = src.TransitionProperty;
        dest.TransitionTimingFunction = src.TransitionTimingFunction;
        dest.Animation = src.Animation; dest.AnimationName = src.AnimationName;
        dest.AnimationDuration = src.AnimationDuration; dest.AnimationTimingFunction = src.AnimationTimingFunction;
        dest.AnimationDelay = src.AnimationDelay; dest.AnimationIterationCount = src.AnimationIterationCount;
        dest.AnimationDirection = src.AnimationDirection; dest.AnimationFillMode = src.AnimationFillMode;
        dest.AnimationPlayState = src.AnimationPlayState;
        dest.PointerEvents = src.PointerEvents; dest.UserSelect = src.UserSelect;
        dest.Direction = src.Direction;
        dest.UnicodeBidi = src.UnicodeBidi;
        dest.WritingMode = src.WritingMode;
        dest.LetterSpacing = src.LetterSpacing;
        dest.WordSpacing = src.WordSpacing;
        dest.TextIndent = src.TextIndent;
        dest.TextTransform = src.TextTransform;
        dest.TextRendering = src.TextRendering;
        dest.TextOverflow = src.TextOverflow;
        dest.TextShadow = src.TextShadow;
        dest.TextEmphasis = src.TextEmphasis;
        dest.TextEmphasisColor = src.TextEmphasisColor;
        dest.TextEmphasisStyle = src.TextEmphasisStyle;
        dest.OutlineWidth = src.OutlineWidth;
        dest.OutlineColor = src.OutlineColor;
        dest.OutlineStyle = src.OutlineStyle;
        dest.OutlineOffset = src.OutlineOffset;
        dest.TableLayout = src.TableLayout;
        dest.CaptionSide = src.CaptionSide;
        dest.EmptyCells = src.EmptyCells;
        dest.Content = src.Content;
        dest.CounterIncrement = src.CounterIncrement;
        dest.CounterReset = src.CounterReset;
        dest.CounterSet = src.CounterSet;
        dest.Quotes = src.Quotes;
        dest.AspectRatio = src.AspectRatio;
        dest.ObjectFit = src.ObjectFit;
        dest.ObjectPositionX = src.ObjectPositionX;
        dest.ObjectPositionY = src.ObjectPositionY;
        dest.Filter = src.Filter;
        dest.BackdropFilter = src.BackdropFilter;
        dest.ClipPath = src.ClipPath;
        dest.Mask = src.Mask; dest.MaskImage = src.MaskImage;
        dest.MaskClip = src.MaskClip; dest.MaskComposite = src.MaskComposite;
        dest.MaskMode = src.MaskMode; dest.MaskOrigin = src.MaskOrigin;
        dest.MaskPosition = src.MaskPosition; dest.MaskRepeat = src.MaskRepeat;
        dest.MaskSize = src.MaskSize;
        dest.Isolation = src.Isolation;
        dest.MixBlendMode = src.MixBlendMode;
        dest.ImageRendering = src.ImageRendering;
        dest.Contain = src.Contain;
        dest.ContentVisibility = src.ContentVisibility;
        dest.WillChange = src.WillChange;
        dest.ScrollBehavior = src.ScrollBehavior;
        dest.TabSize = src.TabSize;
        dest.Hyphens = src.Hyphens;
        dest.LineBreak = src.LineBreak;
        dest.TextJustify = src.TextJustify;
        dest.HangingPunctuation = src.HangingPunctuation;
        dest.Resize = src.Resize;
        dest.Zoom = src.Zoom;
        dest.FontVariant = src.FontVariant;
        dest.FontKerning = src.FontKerning;
        dest.FontStretch = src.FontStretch;
        dest.FontSynthesis = src.FontSynthesis;
        dest.FontOpticalSizing = src.FontOpticalSizing;
        dest.FontVariationSettings = src.FontVariationSettings;
        dest.FontFeatureSettings = src.FontFeatureSettings;
        dest.FontSizeAdjust = src.FontSizeAdjust;
        dest.AccentColor = src.AccentColor;
        dest.CaretColor = src.CaretColor;
        dest.ColorScheme = src.ColorScheme;
        dest.ForcedColorAdjust = src.ForcedColorAdjust;
        dest.Orphans = src.Orphans;
        dest.Widows = src.Widows;
        dest.RubyPosition = src.RubyPosition;
        dest.BorderImageSource = src.BorderImageSource;
        dest.BorderImageSlice = src.BorderImageSlice;
        dest.BorderImageWidth = src.BorderImageWidth;
        dest.BorderImageRepeat = src.BorderImageRepeat;
        dest.BorderImageOutset = src.BorderImageOutset;
        dest.RowGap = src.RowGap;
        dest.ColumnGap = src.ColumnGap;
        dest.ColumnCount = src.ColumnCount;
        dest.ColumnWidth = src.ColumnWidth;
        dest.GridTemplateColumns = src.GridTemplateColumns;
        dest.GridTemplateRows = src.GridTemplateRows;
        dest.GridTemplateAreas = src.GridTemplateAreas;
        dest.GridAutoColumns = src.GridAutoColumns;
        dest.GridAutoRows = src.GridAutoRows;
        dest.GridAutoFlow = src.GridAutoFlow;
        dest.GridColumnStart = src.GridColumnStart;
        dest.GridColumnEnd = src.GridColumnEnd;
        dest.GridRowStart = src.GridRowStart;
        dest.GridRowEnd = src.GridRowEnd;
        dest.GridColumn = src.GridColumn;
        dest.GridRow = src.GridRow;
        dest.GridArea = src.GridArea;
        dest.Grid = src.Grid;
    }

    // ── Parsing helpers (delegated to specialized parsers) ──

    private void ParseShorthand4(string value, out Length top, out Length right, out Length bottom, out Length left)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        top = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    private float ParseFontSize(string value, ComputedStyle? parentStyle)
    {
        float parentFontSize = parentStyle?.FontSize ?? 16;
        return Length.ParseFontSize(value, parentFontSize);
    }

    private FontWeight ParseFontWeight(string value) => value.ToLowerInvariant() switch
    {
        "bold" or "bolder" or "500" or "600" or "700" or "800" or "900" => FontWeight.Bold,
        _ => FontWeight.Normal
    };

    private FontStyleType ParseFontStyle(string value) => value.ToLowerInvariant() switch
    {
        "italic" or "oblique" => FontStyleType.Italic,
        _ => FontStyleType.Normal
    };

    private string ParseFontFamily(string value)
    {
        var families = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (families.Length == 0) return "Arial, sans-serif";
        var first = families[0].Trim().Trim('"', '\'');
        return string.IsNullOrEmpty(first) ? "Arial, sans-serif" : first;
    }

    private float ParseLineHeight(string value, float fontSize)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px / fontSize;
        if (float.TryParse(value, out var num)) return num;
        return 1.2f;
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
        "table-header-group" => DisplayType.TableHeaderGroup,
        "table-row-group" => DisplayType.TableRowGroup,
        "table-footer-group" => DisplayType.TableFooterGroup,
        "none" => DisplayType.None,
        "contents" => DisplayType.Contents,
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

    private WhiteSpaceMode ParseWhiteSpace(string value) => value.ToLowerInvariant() switch
    {
        "nowrap" => WhiteSpaceMode.Nowrap,
        "pre" => WhiteSpaceMode.Pre,
        "pre-wrap" => WhiteSpaceMode.PreWrap,
        "pre-line" => WhiteSpaceMode.PreLine,
        _ => WhiteSpaceMode.Normal
    };

    private WordBreakMode ParseWordBreak(string value) => value.ToLowerInvariant() switch
    {
        "break-all" => WordBreakMode.BreakAll,
        "break-word" => WordBreakMode.BreakWord,
        _ => WordBreakMode.Normal
    };

    private OverflowWrapMode ParseOverflowWrap(string value) => value.ToLowerInvariant() switch
    {
        "break-word" => OverflowWrapMode.BreakWord,
        "anywhere" => OverflowWrapMode.Anywhere,
        _ => OverflowWrapMode.Normal
    };

    private VisibilityType ParseVisibility(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => VisibilityType.Hidden,
        "collapse" => VisibilityType.Collapse,
        _ => VisibilityType.Visible
    };

    private OverflowType ParseOverflow(string value) => value.ToLowerInvariant() switch
    {
        "hidden" => OverflowType.Hidden,
        "scroll" => OverflowType.Scroll,
        "auto" => OverflowType.Auto,
        _ => OverflowType.Visible
    };

    private BackgroundRepeat ParseBackgroundRepeat(string value) => value.ToLowerInvariant() switch
    {
        "repeat-x" => BackgroundRepeat.RepeatX,
        "repeat-y" => BackgroundRepeat.RepeatY,
        "no-repeat" => BackgroundRepeat.NoRepeat,
        _ => BackgroundRepeat.Repeat
    };

    private BackgroundAttachment ParseBackgroundAttachment(string value) => value.ToLowerInvariant() switch
    {
        "fixed" => BackgroundAttachment.Fixed,
        "local" => BackgroundAttachment.Local,
        _ => BackgroundAttachment.Scroll
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

    private static void ParseBackgroundSize(string value, ComputedStyle style)
    {
        if (value == "cover") { style.BackgroundSize = BackgroundSizeType.Cover; return; }
        if (value == "contain") { style.BackgroundSize = BackgroundSizeType.Contain; return; }
        if (value == "auto") { style.BackgroundSize = BackgroundSizeType.Auto; return; }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0] != "auto")
            style.BackgroundSizeWidth = Length.Parse(parts[0]);
        if (parts.Length > 1 && parts[1] != "auto")
            style.BackgroundSizeHeight = Length.Parse(parts[1]);
        style.BackgroundSize = BackgroundSizeType.Length;
    }

    private void ParseBackgroundShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower == "none" || lower == "transparent")
            {
                style.BackgroundColor = SKColors.Transparent;
                style.BackgroundImage = null;
            }
            else if (lower.StartsWith("#") || lower.StartsWith("rgb") || lower.StartsWith("rgba") || ColorParser.IsColorName(lower))
            {
                style.BackgroundColor = ColorParser.Parse(part);
            }
            else if (lower.StartsWith("url("))
            {
                style.BackgroundImage = ParseUrl(part);
            }
            else if (lower is "repeat" or "repeat-x" or "repeat-y" or "no-repeat")
            {
                style.BackgroundRepeat = ParseBackgroundRepeat(part);
            }
            else if (lower is "scroll" or "fixed" or "local")
            {
                style.BackgroundAttachment = ParseBackgroundAttachment(part);
            }
            else if (lower is "cover" or "contain")
            {
                style.BackgroundSize = lower == "cover" ? BackgroundSizeType.Cover : BackgroundSizeType.Contain;
            }
        }
    }

    private string? ParseUrl(string value)
    {
        if (value.StartsWith("url("))
            return value[4..].Trim(' ', '"', '\'', ')');
        return null;
    }

    private void ParseBorderShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "none")
            {
                var bs = ParseBorderStyleValue(part);
                style.BorderTopStyle = bs; style.BorderRightStyle = bs;
                style.BorderBottomStyle = bs; style.BorderLeftStyle = bs;
            }
            else if (part.EndsWith("px"))
            {
                var width = ParseSize(part);
                if (width.HasValue)
                {
                    style.BorderTopWidth = width.Value; style.BorderRightWidth = width.Value;
                    style.BorderBottomWidth = width.Value; style.BorderLeftWidth = width.Value;
                }
            }
            else
            {
                var color = ColorParser.Parse(part);
                style.BorderTopColor = color; style.BorderRightColor = color;
                style.BorderBottomColor = color; style.BorderLeftColor = color;
            }
        }
    }

    private void ParseBorderSide(ComputedStyle style, string side, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "none")
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
                var color = ColorParser.Parse(part);
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
        var w = widths.Select(v => v switch
        {
            "thin" => 1f,
            "medium" => 3f,
            "thick" => 5f,
            _ => ParseSize(v) ?? 0
        }).ToList();

        style.BorderTopWidth = w.Count > 0 ? w[0] : 0;
        style.BorderRightWidth = w.Count > 1 ? w[1] : w[0];
        style.BorderBottomWidth = w.Count > 2 ? w[2] : w[0];
        style.BorderLeftWidth = w.Count > 3 ? w[3] : (w.Count > 1 ? w[1] : w[0]);
    }

    private void ParseBorderColor(string value, ComputedStyle style)
    {
        var colors = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var c = colors.Select(ColorParser.Parse).ToList();
        style.BorderTopColor = c.Count > 0 ? c[0] : SKColors.Black;
        style.BorderRightColor = c.Count > 1 ? c[1] : c[0];
        style.BorderBottomColor = c.Count > 2 ? c[2] : c[0];
        style.BorderLeftColor = c.Count > 3 ? c[3] : (c.Count > 1 ? c[1] : c[0]);
    }

    private void ParseBorderStyle(string value, ComputedStyle style)
    {
        var styles = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = styles.Select(ParseBorderStyleValue).ToList();
        style.BorderTopStyle = s.Count > 0 ? s[0] : BorderStyle.None;
        style.BorderRightStyle = s.Count > 1 ? s[1] : (s.Count > 0 ? s[0] : BorderStyle.None);
        style.BorderBottomStyle = s.Count > 2 ? s[2] : (s.Count > 0 ? s[0] : BorderStyle.None);
        style.BorderLeftStyle = s.Count > 3 ? s[3] : (s.Count > 1 ? s[1] : (s.Count > 0 ? s[0] : BorderStyle.None));
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

    private BorderStyle ParseBorderStyleValue(string value) => value.ToLowerInvariant() switch
    {
        "solid" => BorderStyle.Solid,
        "dashed" => BorderStyle.Dashed,
        "dotted" => BorderStyle.Dotted,
        "double" => BorderStyle.Double,
        "groove" => BorderStyle.Groove,
        "ridge" => BorderStyle.Ridge,
        "inset" => BorderStyle.Inset,
        "outset" => BorderStyle.Outset,
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

    private void ParseFlexShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        if (parts[0] == "none" || parts[0] == "auto")
        {
            style.FlexGrow = 0; style.FlexShrink = 1;
            style.FlexBasis = AutoLength.Instance;
            return;
        }

        int i = 0;
        if (float.TryParse(parts[0], out var g))
        {
            style.FlexGrow = g; i++;
            if (i < parts.Length && float.TryParse(parts[i], out var s))
            { style.FlexShrink = s; i++; }
        }
        else
        {
            style.FlexBasis = Length.Parse(parts[0]); i++;
            if (i < parts.Length && float.TryParse(parts[i], out var g2))
                style.FlexGrow = g2;
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

    private ListStyleType ParseListStyleType(string value) => value.ToLowerInvariant() switch
    {
        "disc" => ListStyleType.Disc,
        "circle" => ListStyleType.Circle,
        "square" => ListStyleType.Square,
        "decimal" => ListStyleType.Decimal,
        "decimal-leading-zero" => ListStyleType.DecimalLeadingZero,
        "lower-roman" => ListStyleType.LowerRoman,
        "upper-roman" => ListStyleType.UpperRoman,
        "lower-alpha" => ListStyleType.LowerAlpha,
        "upper-alpha" => ListStyleType.UpperAlpha,
        "none" => ListStyleType.None,
        _ => ListStyleType.Disc
    };

    private void ParseListStyle(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "inside" or "outside")
                style.ListStylePosition = lower == "inside" ? ListStylePosition.Inside : ListStylePosition.Outside;
            else if (lower == "none")
                style.ListStyleType = ListStyleType.None;
            else if (lower is "disc" or "circle" or "square" or "decimal" or "lower-roman" or "upper-roman")
                style.ListStyleType = ParseListStyleType(part);
            else if (lower.StartsWith("url("))
                style.ListStyleImage = ParseUrl(part);
        }
    }

    private void ParseFontShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;

        while (i < parts.Length)
        {
            var lower = parts[i].ToLowerInvariant();
            if (lower is "normal" or "italic" or "oblique")
            {
                if (lower == "italic" || lower == "oblique") style.FontStyle = FontStyleType.Italic;
                i++;
            }
            else if (lower is "bold" or "bolder" or "lighter" ||
                     lower is "100" or "200" or "300" or "400" or "500" or "600" or "700" or "800" or "900")
            {
                style.FontWeight = ParseFontWeight(parts[i]);
                i++;
            }
            else break;
        }

        if (i < parts.Length && (parts[i].EndsWith("px") || parts[i].EndsWith("em") || parts[i].EndsWith("rem") ||
            parts[i].EndsWith("%") || parts[i] is "xx-small" or "x-small" or "small" or "medium" or
            "large" or "x-large" or "xx-large"))
        {
            style.FontSize = Length.ParseFontSize(parts[i], style.FontSize);
            i++;
        }

        if (i < parts.Length && parts[i] == "/")
        {
            i++;
            if (i < parts.Length && float.TryParse(parts[i], out var lh))
                style.LineHeight = lh / style.FontSize;
            i++;
        }

        if (i < parts.Length)
        {
            var family = string.Join(" ", parts.Skip(i));
            style.FontFamily = family.Trim().Trim('"', '\'');
        }
    }

    private void ParseOutlineShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.EndsWith("px"))
            {
                if (float.TryParse(part.Replace("px", ""), out var w))
                    style.OutlineWidth = w;
            }
            else if (part is "solid" or "dashed" or "dotted" or "double" or "none")
            {
                style.OutlineStyle = ParseBorderStyleValue(part);
            }
            else
            {
                style.OutlineColor = ColorParser.Parse(part);
            }
        }
    }

    private void ParseShorthand2(string value, out Length a, out Length b)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        a = Length.Parse(parts.Length > 0 ? parts[0] : "0");
        b = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
    }

    private Length? ParsePositionKeywordOrLength(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "left" or "top" => new PixelLength(0),
            "center" => new PercentLength(0.5f),
            "right" or "bottom" => new PercentLength(1),
            _ => Length.TryParse(value, out var l) ? l : null
        };
    }

    private void ParsePosition(string value, out Length? x, out Length? y)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        x = parts.Length > 0 ? ParsePositionKeywordOrLength(parts[0]) : null;
        y = parts.Length > 1 ? ParsePositionKeywordOrLength(parts[1]) : null;
    }

    private void ParseInsetShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        style.Top = Length.Parse(parts[0]);
        style.Right = Length.Parse(parts.Length > 1 ? parts[1] : parts[0]);
        style.Bottom = Length.Parse(parts.Length > 2 ? parts[2] : parts[0]);
        style.Left = Length.Parse(parts.Length > 3 ? parts[3] : (parts.Length > 1 ? parts[1] : parts[0]));
    }

    private TextDecorationLineType ParseTextDecorationLine(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower == "none") return TextDecorationLineType.None;
        var result = TextDecorationLineType.None;
        if (lower.Contains("underline")) result |= TextDecorationLineType.Underline;
        if (lower.Contains("overline")) result |= TextDecorationLineType.Overline;
        if (lower.Contains("line-through")) result |= TextDecorationLineType.LineThrough;
        return result;
    }

    private TextDecorationStyleType ParseTextDecorationStyle(string value) => value.ToLowerInvariant() switch
    {
        "double" => TextDecorationStyleType.Double,
        "dotted" => TextDecorationStyleType.Dotted,
        "dashed" => TextDecorationStyleType.Dashed,
        "wavy" => TextDecorationStyleType.Wavy,
        _ => TextDecorationStyleType.Solid
    };

    private void ParseTextDecorationShorthand(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower == "none" || lower == "underline" || lower == "overline" || lower == "line-through")
                style.TextDecorationLine = ParseTextDecorationLine(part);
            else if (lower == "solid" || lower == "double" || lower == "dotted" || lower == "dashed" || lower == "wavy")
                style.TextDecorationStyle = ParseTextDecorationStyle(part);
            else if (lower.StartsWith("#") || lower.StartsWith("rgb"))
                style.TextDecorationColor = ColorParser.Parse(part);
        }
    }

    private List<TextShadowValue> ParseTextShadow(string value)
    {
        var shadows = new List<TextShadowValue>();
        if (string.IsNullOrEmpty(value) || value == "none") return shadows;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return shadows;

        float offsetX = float.TryParse(parts[0].TrimEnd('p', 'x'), out var ox) ? ox : 0;
        float offsetY = float.TryParse(parts[1].TrimEnd('p', 'x'), out var oy) ? oy : 0;
        float blurRadius = 0;
        int index = 2;
        if (index < parts.Length && parts[index].Contains("px"))
        {
            float.TryParse(parts[index].TrimEnd('p', 'x'), out blurRadius);
            index++;
        }
        var color = index < parts.Length ? ColorParser.Parse(string.Join(" ", parts.Skip(index))) : new SKColor(0, 0, 0, 255);
        shadows.Add(new TextShadowValue(color, offsetX, offsetY, blurRadius));
        return shadows;
    }

    private ObjectFitType ParseObjectFit(string value) => value.ToLowerInvariant() switch
    {
        "contain" => ObjectFitType.Contain,
        "cover" => ObjectFitType.Cover,
        "none" => ObjectFitType.None,
        "scale-down" => ObjectFitType.ScaleDown,
        _ => ObjectFitType.Fill
    };

    private GridAutoFlowType ParseGridAutoFlow(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower.Contains("column")) return GridAutoFlowType.Column;
        if (lower.Contains("dense")) return GridAutoFlowType.Dense;
        return GridAutoFlowType.Row;
    }

    private void ParseGridTemplateShorthand(string value, ComputedStyle style)
    {
        if (value == "none") return;
        if (value.Contains("/"))
        {
            var parts = value.Split('/');
            style.GridTemplateRows = parts[0].Trim();
            style.GridTemplateColumns = parts.Length > 1 ? parts[1].Trim() : null;
        }
    }

    private void ParseFlexFlow(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "row" or "row-reverse" or "column" or "column-reverse")
                style.FlexDirection = ParseFlexDirection(part);
            else if (lower is "nowrap" or "wrap" or "wrap-reverse")
                style.FlexWrap = ParseFlexWrap(part);
        }
    }

    private WritingModeType ParseWritingMode(string value) => value.ToLowerInvariant() switch
    {
        "vertical-rl" => WritingModeType.VerticalRl,
        "vertical-lr" => WritingModeType.VerticalLr,
        _ => WritingModeType.HorizontalTb
    };

    private HyphensType ParseHyphens(string value) => value.ToLowerInvariant() switch
    {
        "manual" => HyphensType.Manual,
        "auto" => HyphensType.Auto,
        _ => HyphensType.None
    };

    private LineBreakType ParseLineBreak(string value) => value.ToLowerInvariant() switch
    {
        "loose" => LineBreakType.Loose,
        "normal" => LineBreakType.Normal,
        "strict" => LineBreakType.Strict,
        "anywhere" => LineBreakType.Anywhere,
        _ => LineBreakType.Auto
    };

    private TextJustifyType ParseTextJustify(string value) => value.ToLowerInvariant() switch
    {
        "inter-word" => TextJustifyType.InterWord,
        "inter-character" => TextJustifyType.InterCharacter,
        "none" => TextJustifyType.None,
        _ => TextJustifyType.Auto
    };

    private ResizeType ParseResize(string value) => value.ToLowerInvariant() switch
    {
        "both" => ResizeType.Both,
        "horizontal" => ResizeType.Horizontal,
        "vertical" => ResizeType.Vertical,
        _ => ResizeType.None
    };

    private ContainType ParseContain(string value)
    {
        var lower = value.ToLowerInvariant();
        if (lower == "none") return ContainType.None;
        if (lower == "strict") return ContainType.Strict;
        if (lower == "content") return ContainType.Content;
        if (lower == "layout") return ContainType.Layout;
        if (lower == "paint") return ContainType.Paint;
        if (lower == "size") return ContainType.Size;
        return ContainType.None;
    }

    private ContentVisibilityType ParseContentVisibility(string value) => value.ToLowerInvariant() switch
    {
        "auto" => ContentVisibilityType.Auto,
        "hidden" => ContentVisibilityType.Hidden,
        _ => ContentVisibilityType.Visible
    };

    private ImageRenderingType ParseImageRendering(string value) => value.ToLowerInvariant() switch
    {
        "crisp-edges" => ImageRenderingType.CrispEdges,
        "pixelated" => ImageRenderingType.Pixelated,
        _ => ImageRenderingType.Auto
    };

    private MixBlendModeType ParseMixBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => MixBlendModeType.Multiply,
        "screen" => MixBlendModeType.Screen,
        "overlay" => MixBlendModeType.Overlay,
        "darken" => MixBlendModeType.Darken,
        "lighten" => MixBlendModeType.Lighten,
        "color-dodge" => MixBlendModeType.ColorDodge,
        "color-burn" => MixBlendModeType.ColorBurn,
        "hard-light" => MixBlendModeType.HardLight,
        "soft-light" => MixBlendModeType.SoftLight,
        "difference" => MixBlendModeType.Difference,
        "exclusion" => MixBlendModeType.Exclusion,
        "hue" => MixBlendModeType.Hue,
        "saturation" => MixBlendModeType.Saturation,
        "color" => MixBlendModeType.Color,
        "luminosity" => MixBlendModeType.Luminosity,
        _ => MixBlendModeType.Normal
    };

    private BackgroundBlendModeType ParseBackgroundBlendMode(string value) => value.ToLowerInvariant() switch
    {
        "multiply" => BackgroundBlendModeType.Multiply,
        "screen" => BackgroundBlendModeType.Screen,
        "overlay" => BackgroundBlendModeType.Overlay,
        "darken" => BackgroundBlendModeType.Darken,
        "lighten" => BackgroundBlendModeType.Lighten,
        "color-dodge" => BackgroundBlendModeType.ColorDodge,
        "color-burn" => BackgroundBlendModeType.ColorBurn,
        "hard-light" => BackgroundBlendModeType.HardLight,
        "soft-light" => BackgroundBlendModeType.SoftLight,
        "difference" => BackgroundBlendModeType.Difference,
        "exclusion" => BackgroundBlendModeType.Exclusion,
        "hue" => BackgroundBlendModeType.Hue,
        "saturation" => BackgroundBlendModeType.Saturation,
        "color" => BackgroundBlendModeType.Color,
        "luminosity" => BackgroundBlendModeType.Luminosity,
        _ => BackgroundBlendModeType.Normal
    };

    private OverscrollBehaviorType ParseOverscrollBehavior(string value) => value.ToLowerInvariant() switch
    {
        "contain" => OverscrollBehaviorType.Contain,
        "none" => OverscrollBehaviorType.None,
        _ => OverscrollBehaviorType.Auto
    };

    private float ParseZoom(string value)
    {
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f;
        if (float.TryParse(value, out var num)) return num;
        return 1;
    }

    private void ParseGap(string value, ComputedStyle style)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && Length.TryParse(parts[0], out var gap))
        {
            style.RowGap = gap;
            style.ColumnGap = parts.Length > 1 ? Length.Parse(parts[1]) : gap;
        }
    }

    private static BoxShadowValue? ParseBoxShadow(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "none") return null;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (parts[0] == "inset") return null;

        float offsetX = float.TryParse(parts[0].TrimEnd('p', 'x'), out var ox) ? ox : 0;
        float offsetY = float.TryParse(parts[1].TrimEnd('p', 'x'), out var oy) ? oy : 0;
        float blurRadius = 0, spread = 0;
        int index = 2;

        if (index < parts.Length && parts[index].Contains("px") && float.TryParse(parts[index].TrimEnd('p', 'x'), out var br))
        { blurRadius = br; index++; }
        if (index < parts.Length && parts[index].Contains("px") && float.TryParse(parts[index].TrimEnd('p', 'x'), out var sp))
        { spread = sp; index++; }

        var color = index < parts.Length ? ColorParser.Parse(string.Join(" ", parts.Skip(index))) : new SKColor(0, 0, 0, 80);
        return new BoxShadowValue(color, offsetX, offsetY, blurRadius, spread);
    }

    private static string GetOriginalShorthand(string longhand)
    {
        return longhand switch
        {
            var s when s.StartsWith("margin-") => "margin",
            var s when s.StartsWith("padding-") => "padding",
            var s when s.StartsWith("border-top-") || s.StartsWith("border-right-") || s.StartsWith("border-bottom-") || s.StartsWith("border-left-") => "border",
            var s when s.StartsWith("flex-") => "flex",
            var s when s.StartsWith("grid-") => "grid",
            _ => longhand
        };
    }
}

/// <summary>
/// Cascade priority encoding - similar to Blink's 96-bit priority integer.
/// Order: Importance → Origin → TreeOrder
/// Per CSS Cascading 4 spec:
///   Normal: Inline(5) > Author(3) > User(2) > UA(1)
///   Important: UA(5) > User(4) > Author(3) > Inline(2)
///   (JS-modified same as Inline)
/// </summary>
public readonly struct CascadePriority : IComparable<CascadePriority>
{
    public readonly bool Importance;
    public readonly CascadeOrigin Origin;
    public readonly int TreeOrder;

    public CascadePriority(bool importance, CascadeOrigin origin, int treeOrder)
    {
        Importance = importance;
        Origin = origin;
        TreeOrder = treeOrder;
    }

    public int CompareTo(CascadePriority other)
    {
        if (Importance != other.Importance)
            return Importance.CompareTo(other.Importance);

        int thisWeight = GetOriginWeight(Origin, Importance);
        int otherWeight = GetOriginWeight(other.Origin, other.Importance);
        if (thisWeight != otherWeight)
            return thisWeight.CompareTo(otherWeight);

        return TreeOrder.CompareTo(other.TreeOrder);
    }

    private static int GetOriginWeight(CascadeOrigin origin, bool isImportant)
    {
        if (isImportant)
        {
            return origin switch
            {
                CascadeOrigin.JsModified => 1,
                CascadeOrigin.Inline => 2,
                CascadeOrigin.Author => 3,
                CascadeOrigin.User => 4,
                CascadeOrigin.UserAgent => 5,
                _ => 0
            };
        }
        else
        {
            return origin switch
            {
                CascadeOrigin.UserAgent => 1,
                CascadeOrigin.User => 2,
                CascadeOrigin.Author => 3,
                CascadeOrigin.Inline => 4,
                CascadeOrigin.JsModified => 5,
                _ => 0
            };
        }
    }
}

public enum CascadeOrigin { UserAgent, User, Author, Inline, JsModified }

/// <summary>
/// CascadeMap stores property declarations and keeps only the highest-priority one per property.
/// Similar to Blink's CascadeMap in style_cascade.cc.
/// </summary>
public class CascadeMap
{
    private readonly Dictionary<string, (string value, CascadePriority priority)> _map = new();

    public void Clear() => _map.Clear();

    public void Insert(string property, string value, CascadePriority priority)
    {
        if (_map.TryGetValue(property, out var existing))
        {
            if (priority.CompareTo(existing.priority) >= 0)
                _map[property] = (value, priority);
        }
        else
        {
            _map[property] = (value, priority);
        }
    }

    public bool TryGetValue(string property, out string value)
    {
        if (_map.TryGetValue(property, out var entry))
        {
            value = entry.value;
            return true;
        }
        value = string.Empty;
        return false;
    }

    public IEnumerable<KeyValuePair<string, string>> GetAll()
    {
        foreach (var kv in _map)
            yield return new KeyValuePair<string, string>(kv.Key, kv.Value.value);
    }
}

/// <summary>
/// SelectorMatcher - pre-compiles selectors for faster matching.
/// Inspired by Blink's CSS selector matching optimization.
/// </summary>
public class SelectorMatcher
{
    private readonly Dictionary<string, CssSelector> _selectorCache = new();

    public bool Matches(CssRule rule, Element element)
    {
        if (!_selectorCache.TryGetValue(rule.Selector, out var selector))
        {
            selector = CssSelector.Parse(rule.Selector);
            _selectorCache[rule.Selector] = selector;
        }

        return selector.Matches(element, element.ParentElement);
    }

    public void ClearCache() => _selectorCache.Clear();
}

/// <summary>
/// MatchedPropertiesCache - caches resolved styles for elements to avoid re-computation.
/// Similar to Blink's MatchedPropertiesCache.
/// </summary>
public class MatchedPropertiesCache
{
    private readonly Dictionary<CacheKey, ComputedStyle> _cache = new();
    private const int MaxSize = 1024;

    public bool TryGet(CacheKey key, out ComputedStyle? style)
    {
        return _cache.TryGetValue(key, out style);
    }

    public void Set(CacheKey key, ComputedStyle style)
    {
        if (_cache.Count >= MaxSize)
        {
            var oldest = _cache.Keys.First();
            _cache.Remove(oldest);
        }
        _cache[key] = style;
    }

    public void Clear() => _cache.Clear();
}

public readonly struct CacheKey : IEquatable<CacheKey>
{
    public readonly int ElementId;
    public readonly int StylesheetCount;
    public readonly int StyleAttrHash;
    public readonly int ClassHash;

    public CacheKey(Element element, int stylesheetCount)
    {
        ElementId = element.GetHashCode();
        StylesheetCount = stylesheetCount;
        StyleAttrHash = element.GetAttribute("style")?.GetHashCode() ?? 0;
        ClassHash = element.ClassName?.GetHashCode() ?? 0;
    }

    public bool Equals(CacheKey other) =>
        ElementId == other.ElementId &&
        StylesheetCount == other.StylesheetCount &&
        StyleAttrHash == other.StyleAttrHash &&
        ClassHash == other.ClassHash;

    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ElementId, StylesheetCount, StyleAttrHash, ClassHash);
}
