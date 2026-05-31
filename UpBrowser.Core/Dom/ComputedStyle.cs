using SkiaSharp;
using UpBrowser.Core.Css;

namespace UpBrowser.Core.Dom;

public abstract class Length
{
    public abstract float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight);

    public static Length Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "auto" || value == "inherit" || value == "initial")
            return AutoLength.Instance;

        // Check longest units first to avoid false matches
        if (value.EndsWith("cqmin"))
            return new CqMinLength(float.Parse(value[..^5]));
        if (value.EndsWith("cqmax"))
            return new CqMaxLength(float.Parse(value[..^5]));
        if (value.EndsWith("cqw"))
            return new CqWLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqh"))
            return new CqHLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqi"))
            return new CqILength(float.Parse(value[..^3]));
        if (value.EndsWith("cqb"))
            return new CqBLength(float.Parse(value[..^3]));
        if (value.EndsWith("dvw"))
            return new DVwLength(float.Parse(value[..^3]));
        if (value.EndsWith("dvh"))
            return new DVhLength(float.Parse(value[..^3]));
        if (value.EndsWith("svw"))
            return new SVwLength(float.Parse(value[..^3]));
        if (value.EndsWith("svh"))
            return new SVhLength(float.Parse(value[..^3]));
        if (value.EndsWith("lvw"))
            return new LVwLength(float.Parse(value[..^3]));
        if (value.EndsWith("lvh"))
            return new LVhLength(float.Parse(value[..^3]));
        if (value.EndsWith("vmin"))
            return new VminLength(float.Parse(value[..^4]));
        if (value.EndsWith("vmax"))
            return new VmaxLength(float.Parse(value[..^4]));
        if (value.EndsWith("vw"))
            return new VwLength(float.Parse(value[..^2]));
        if (value.EndsWith("vh"))
            return new VhLength(float.Parse(value[..^2]));
        if (value.EndsWith("vi"))
            return new ViLength(float.Parse(value[..^2]));
        if (value.EndsWith("vb"))
            return new VbLength(float.Parse(value[..^2]));
        if (value.EndsWith("rem"))
            return new RemLength(float.Parse(value[..^2]));
        if (value.EndsWith("rex"))
            return new RexLength(float.Parse(value[..^3]));
        if (value.EndsWith("ric"))
            return new RicLength(float.Parse(value[..^3]));
        if (value.EndsWith("rlh"))
            return new RlhLength(float.Parse(value[..^3]));
        if (value.EndsWith("cap"))
            return new CapLength(float.Parse(value[..^3]));
        if (value.EndsWith("rcap"))
            return new RcapLength(float.Parse(value[..^4]));
        if (value.EndsWith("lh"))
            return new LhLength(float.Parse(value[..^2]));
        if (value.EndsWith("px"))
            return new PixelLength(float.Parse(value[..^2]));
        if (value.EndsWith("em"))
            return new EmLength(float.Parse(value[..^2]));
        if (value.EndsWith("ex"))
            return new ExLength(float.Parse(value[..^2]));
        if (value.EndsWith("ch"))
            return new ChLength(float.Parse(value[..^2]));
        if (value.EndsWith("%"))
            return new PercentLength(float.Parse(value[..^1]) / 100f);
        if (value.EndsWith("pt"))
            return new PixelLength(float.Parse(value[..^2]) * 1.33333f);
        if (value.EndsWith("pc"))
            return new PixelLength(float.Parse(value[..^2]) * 16f);
        if (value.EndsWith("in"))
            return new PixelLength(float.Parse(value[..^2]) * 96f);
        if (value.EndsWith("cm"))
            return new PixelLength(float.Parse(value[..^2]) * 37.7953f);
        if (value.EndsWith("mm"))
            return new PixelLength(float.Parse(value[..^2]) * 3.77953f);
        if (value == "0")
            return new PixelLength(0);

        if (value.StartsWith("calc(") || value.StartsWith("min(") || value.StartsWith("max(") || value.StartsWith("clamp(") || value.StartsWith("fit-content("))
        {
            return new MathLength(value);
        }

        return AutoLength.Instance;
    }

    public static bool TryParse(string value, out Length length)
    {
        try
        {
            length = Parse(value);
            return true;
        }
        catch
        {
            length = AutoLength.Instance;
            return false;
        }
    }
    public static float ParseFontSize(string value, float parentFontSize)
    {
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * 16;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * parentFontSize;
        if (value.EndsWith("%") && float.TryParse(value[..^2], out var pct)) return pct / 100f * parentFontSize;
        return value.ToLowerInvariant() switch
        {
            "xx-small" => 10,
            "x-small" => 12,
            "small" => 14,
            "medium" => 16,
            "large" => 18,
            "x-large" => 24,
            "xx-large" => 32,
            "larger" => parentFontSize * 1.2f,
            "smaller" => parentFontSize / 1.2f,
            _ => 16
        };
    }

    public static float ToPixelsOrDefault(Length length, float defaultValue = 0, float viewportWidth = 0, float viewportHeight = 0)
    {
        if (length == null) return defaultValue;
        if (length is PixelLength p) return p.Value;
        if (length is EmLength e)
        {
            var reference = defaultValue > 0 ? defaultValue : 16f;
            return e.Value * reference;
        }
        if (length is RemLength r)
        {
            var root = defaultValue > 0 ? defaultValue : 16f;
            return r.Value * root;
        }
        if (length is PercentLength perc)
        {
            var reference = defaultValue > 0 ? defaultValue : 0f;
            return perc.Value * reference;
        }
        if (length is MathLength ml)
        {
            var reference = defaultValue > 0 ? defaultValue : 16f;
            return ml.ToPixels(reference, 16, viewportWidth, viewportHeight);
        }
        return defaultValue;
    }
}

public class AutoLength : Length
{
    public static readonly AutoLength Instance = new();
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => float.NaN;
    public override string ToString() => "auto";
}

public class PixelLength : Length
{
    public float Value { get; }
    public PixelLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value;
    public override string ToString() => $"{Value}px";
}

public class EmLength : Length
{
    public float Value { get; }
    public EmLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference;
    public override string ToString() => $"{Value}em";
}

public class RemLength : Length
{
    public float Value { get; }
    public RemLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize;
    public override string ToString() => $"{Value}rem";
}

public class PercentLength : Length
{
    public float Value { get; }
    public PercentLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference;
    public override string ToString() => $"{Value * 100}%";
}

public class VwLength : Length
{
    public float Value { get; }
    public VwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}vw";
}

public class VhLength : Length
{
    public float Value { get; }
    public VhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}vh";
}

public class VminLength : Length
{
    public float Value { get; }
    public VminLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Min(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}vmin";
}

public class VmaxLength : Length
{
    public float Value { get; }
    public VmaxLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Max(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}vmax";
}

public class ExLength : Length
{
    public float Value { get; }
    public ExLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference * 0.5f;
    public override string ToString() => $"{Value}ex";
}

public class ChLength : Length
{
    public float Value { get; }
    public ChLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference * 0.5f;
    public override string ToString() => $"{Value}ch";
}

// Container query units (temporarily mapped to viewport until container support)
public class CqWLength : Length
{
    public float Value { get; }
    public CqWLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}cqw";
}

public class CqHLength : Length
{
    public float Value { get; }
    public CqHLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}cqh";
}

public class CqILength : Length
{
    public float Value { get; }
    public CqILength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}cqi";
}

public class CqBLength : Length
{
    public float Value { get; }
    public CqBLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}cqb";
}

public class CqMinLength : Length
{
    public float Value { get; }
    public CqMinLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Min(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}cqmin";
}

public class CqMaxLength : Length
{
    public float Value { get; }
    public CqMaxLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * Math.Max(viewportWidth, viewportHeight) / 100f;
    public override string ToString() => $"{Value}cqmax";
}

// Dynamic viewport units
public class DVwLength : Length
{
    public float Value { get; }
    public DVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}dvw";
}

public class DVhLength : Length
{
    public float Value { get; }
    public DVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}dvh";
}

// Small viewport units
public class SVwLength : Length
{
    public float Value { get; }
    public SVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}svw";
}

public class SVhLength : Length
{
    public float Value { get; }
    public SVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}svh";
}

// Large viewport units
public class LVwLength : Length
{
    public float Value { get; }
    public LVwLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}lvw";
}

public class LVhLength : Length
{
    public float Value { get; }
    public LVhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}lvh";
}

// Inline/block-axis viewport units (vi = viewport inline, vb = viewport block)
public class ViLength : Length
{
    public float Value { get; }
    public ViLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportWidth / 100f;
    public override string ToString() => $"{Value}vi";
}

public class VbLength : Length
{
    public float Value { get; }
    public VbLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * viewportHeight / 100f;
    public override string ToString() => $"{Value}vb";
}

// Font-relative units
public class RexLength : Length
{
    public float Value { get; }
    public RexLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize * 0.5f;
    public override string ToString() => $"{Value}rex";
}

public class RicLength : Length
{
    public float Value { get; }
    public RicLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize * 0.5f;
    public override string ToString() => $"{Value}ric";
}

public class LhLength : Length
{
    public float Value { get; }
    public LhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference;
    public override string ToString() => $"{Value}lh";
}

public class RlhLength : Length
{
    public float Value { get; }
    public RlhLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize;
    public override string ToString() => $"{Value}rlh";
}

public class CapLength : Length
{
    public float Value { get; }
    public CapLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * reference * 0.7f;
    public override string ToString() => $"{Value}cap";
}

public class RcapLength : Length
{
    public float Value { get; }
    public RcapLength(float value) => Value = value;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight) => Value * rootFontSize * 0.7f;
    public override string ToString() => $"{Value}rcap";
}

public class MathLength : Length
{
    public string Expression { get; }
    public MathLength(string expression) => Expression = expression;
    public override float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var evaluated = CssFunctionEvaluator.Evaluate(Expression, null, reference, rootFontSize, viewportWidth, viewportHeight);
        if (evaluated.EndsWith("px") && float.TryParse(evaluated[..^2], out var epx))
            return epx;
        return float.NaN;
    }
    public override string ToString() => Expression;
}

public class ComputedStyle
{
    private readonly Dictionary<string, string> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    public void SetCustomProperty(string name, string value) => _customProperties[name] = value;
    public string? GetCustomProperty(string name) => _customProperties.GetValueOrDefault(name);
    public bool HasCustomProperty(string name) => _customProperties.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, string>> GetAllCustomProperties() => _customProperties;
    public Length Width { get; set; } = AutoLength.Instance;
    public Length Height { get; set; } = AutoLength.Instance;
    public Length Top { get; set; } = AutoLength.Instance;
    public Length Left { get; set; } = AutoLength.Instance;
    public Length Right { get; set; } = AutoLength.Instance;
    public Length Bottom { get; set; } = AutoLength.Instance;

    public Length MarginTop { get; set; } = new PixelLength(0);
    public Length MarginBottom { get; set; } = new PixelLength(0);
    public Length MarginLeft { get; set; } = new PixelLength(0);
    public Length MarginRight { get; set; } = new PixelLength(0);

    public Length PaddingTop { get; set; } = new PixelLength(0);
    public Length PaddingBottom { get; set; } = new PixelLength(0);
    public Length PaddingLeft { get; set; } = new PixelLength(0);
    public Length PaddingRight { get; set; } = new PixelLength(0);

    public float BorderTopWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }
    public float BorderRightWidth { get; set; }
    public SKColor BorderTopColor { get; set; } = SKColors.Black;
    public SKColor BorderBottomColor { get; set; } = SKColors.Black;
    public SKColor BorderLeftColor { get; set; } = SKColors.Black;
    public SKColor BorderRightColor { get; set; } = SKColors.Black;
    public BorderStyle BorderTopStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderBottomStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderLeftStyle { get; set; } = BorderStyle.None;
    public BorderStyle BorderRightStyle { get; set; } = BorderStyle.None;
    public float BorderTopLeftRadius { get; set; }
    public float BorderTopRightRadius { get; set; }
    public float BorderBottomLeftRadius { get; set; }
    public float BorderBottomRightRadius { get; set; }

    public DisplayType Display { get; set; } = DisplayType.Block;
    public PositionType Position { get; set; } = PositionType.Static;
    public FloatType Float { get; set; } = FloatType.None;
    public ClearType Clear { get; set; } = ClearType.None;

    public string FontFamily { get; set; } = "Arial, sans-serif";
    public float FontSize { get; set; } = 16;
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public FontStyleType FontStyle { get; set; } = FontStyleType.Normal;
    public float LineHeight { get; set; } = 1.5f;

    public SKColor Color { get; set; } = SKColors.Black;
    public SKColor? BackgroundColor { get; set; }
    public string? BackgroundImage { get; set; }
    public Length? BackgroundPositionX { get; set; }
    public Length? BackgroundPositionY { get; set; }
    public BackgroundRepeat BackgroundRepeat { get; set; } = BackgroundRepeat.Repeat;
    public BackgroundAttachment BackgroundAttachment { get; set; } = BackgroundAttachment.Scroll;

    public TextAlignType TextAlign { get; set; } = TextAlignType.Start;
    public TextDecorationType TextDecoration { get; set; } = TextDecorationType.None;
    public VerticalAlignType VerticalAlign { get; set; } = VerticalAlignType.Baseline;
    public WhiteSpaceMode WhiteSpace { get; set; } = WhiteSpaceMode.Normal;
    public WordBreakMode WordBreak { get; set; } = WordBreakMode.Normal;
    public OverflowWrapMode OverflowWrap { get; set; } = OverflowWrapMode.Normal;

    public OverflowType Overflow { get; set; } = OverflowType.Visible;
    public OverflowType OverflowX { get; set; } = OverflowType.Visible;
    public OverflowType OverflowY { get; set; } = OverflowType.Visible;
    public VisibilityType Visibility { get; set; } = VisibilityType.Visible;
    public int? ZIndex { get; set; }
    public string? Cursor { get; set; } = "auto";
    public float Opacity { get; set; } = 1.0f;
    public BoxShadowValue? BoxShadow { get; set; }
    public BackgroundSizeType BackgroundSize { get; set; } = BackgroundSizeType.Auto;
    public Length? BackgroundSizeWidth { get; set; }
    public Length? BackgroundSizeHeight { get; set; }

    public FlexDirectionType FlexDirection { get; set; } = FlexDirectionType.Row;
    public FlexWrapType FlexWrap { get; set; } = FlexWrapType.NoWrap;
    public float FlexGrow { get; set; } = 0;
    public float FlexShrink { get; set; } = 1;
    public Length FlexBasis { get; set; } = AutoLength.Instance;
    public JustifyContentType JustifyContent { get; set; } = JustifyContentType.FlexStart;
    public AlignItemsType AlignItems { get; set; } = AlignItemsType.Stretch;
    public AlignSelfType AlignSelf { get; set; } = AlignSelfType.Auto;

    public Length? MinWidth { get; set; }
    public Length? MaxWidth { get; set; }
    public Length? MinHeight { get; set; }
    public Length? MaxHeight { get; set; }

    public BoxSizingType BoxSizing { get; set; } = BoxSizingType.ContentBox;
    public bool BorderCollapse { get; set; }
    public ListStyleType ListStyleType { get; set; } = ListStyleType.Disc;
    public string? ListStyleImage { get; set; }
    public ListStylePosition ListStylePosition { get; set; } = ListStylePosition.Outside;

    public string? Transform { get; set; }
    public string? TransformOrigin { get; set; } = "50% 50% 0";
    public string? Transition { get; set; }
    public string? TransitionDelay { get; set; }
    public string? TransitionDuration { get; set; }
    public string? TransitionProperty { get; set; }
    public string? TransitionTimingFunction { get; set; }
    public string? Animation { get; set; }
    public string? AnimationName { get; set; }
    public string? AnimationDuration { get; set; }
    public string? AnimationTimingFunction { get; set; }
    public string? AnimationDelay { get; set; }
    public string? AnimationIterationCount { get; set; }
    public string? AnimationDirection { get; set; }
    public string? AnimationFillMode { get; set; }
    public string? AnimationPlayState { get; set; }
    public string? PointerEvents { get; set; } = "auto";
    public string? UserSelect { get; set; } = "auto";
    public string Direction { get; set; } = "ltr";
    public float LetterSpacing { get; set; }
    public float WordSpacing { get; set; }
    public float TextIndent { get; set; }
    public string TextTransform { get; set; } = "none";
    public TextOverflowType TextOverflow { get; set; } = TextOverflowType.Clip;
    public List<TextShadowValue> TextShadow { get; set; } = new();
    public TextDecorationLineType TextDecorationLine { get; set; } = TextDecorationLineType.None;
    public TextDecorationStyleType TextDecorationStyle { get; set; } = TextDecorationStyleType.Solid;
    public SKColor TextDecorationColor { get; set; } = SKColors.Black;
    public float TextDecorationThickness { get; set; }
    public float TextUnderlineOffset { get; set; }
    public string TextEmphasis { get; set; } = "none";
    public string TextEmphasisColor { get; set; } = "currentcolor";
    public string TextEmphasisStyle { get; set; } = "none";

    public Length RowGap { get; set; } = new PixelLength(0);
    public Length ColumnGap { get; set; } = new PixelLength(0);

    public float OutlineWidth { get; set; }
    public SKColor OutlineColor { get; set; } = SKColors.Black;
    public BorderStyle OutlineStyle { get; set; } = BorderStyle.None;
    public float OutlineOffset { get; set; }
    public string TableLayout { get; set; } = "auto";
    public string CaptionSide { get; set; } = "top";
    public string EmptyCells { get; set; } = "show";
    public string? Content { get; set; }
    public string CounterIncrement { get; set; } = "none";
    public string CounterReset { get; set; } = "none";
    public string CounterSet { get; set; } = "none";
    public string Quotes { get; set; } = "auto";

    public int Order { get; set; }
    public float AspectRatio { get; set; }
    public ObjectFitType ObjectFit { get; set; } = ObjectFitType.Fill;
    public Length? ObjectPositionX { get; set; }
    public Length? ObjectPositionY { get; set; }
    public string FlexFlow { get; set; } = "row nowrap";
    public string AlignContent { get; set; } = "stretch";
    public string JustifyItems { get; set; } = "legacy";
    public string JustifySelf { get; set; } = "auto";
    public string PlaceContent { get; set; } = "normal";
    public string PlaceItems { get; set; } = "normal";
    public string PlaceSelf { get; set; } = "auto";

    public string? GridTemplateColumns { get; set; }
    public string? GridTemplateRows { get; set; }
    public string? GridTemplateAreas { get; set; }
    public string? GridAutoColumns { get; set; } = "auto";
    public string? GridAutoRows { get; set; } = "auto";
    public GridAutoFlowType GridAutoFlow { get; set; } = GridAutoFlowType.Row;
    public string? GridColumnStart { get; set; }
    public string? GridColumnEnd { get; set; }
    public string? GridRowStart { get; set; }
    public string? GridRowEnd { get; set; }
    public string? GridColumn { get; set; }
    public string? GridRow { get; set; }
    public string? GridArea { get; set; }
    public string? Grid { get; set; }

    public string BackgroundClip { get; set; } = "border-box";
    public string BackgroundOrigin { get; set; } = "padding-box";
    public BackgroundBlendModeType BackgroundBlendMode { get; set; } = BackgroundBlendModeType.Normal;

    public WritingModeType WritingMode { get; set; } = WritingModeType.HorizontalTb;
    public HyphensType Hyphens { get; set; } = HyphensType.None;
    public float TabSize { get; set; } = 8;
    public ScrollBehaviorType ScrollBehavior { get; set; } = ScrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehavior { get; set; } = OverscrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehaviorX { get; set; } = OverscrollBehaviorType.Auto;
    public OverscrollBehaviorType OverscrollBehaviorY { get; set; } = OverscrollBehaviorType.Auto;
    public OverflowAnchorType OverflowAnchor { get; set; } = OverflowAnchorType.Auto;
    public ContainType Contain { get; set; } = ContainType.None;
    public ContentVisibilityType ContentVisibility { get; set; } = ContentVisibilityType.Visible;
    public string WillChange { get; set; } = "auto";
    public SKColor? AccentColor { get; set; }
    public SKColor? CaretColor { get; set; }
    public string ColorScheme { get; set; } = "normal";
    public ForcedColorAdjustType ForcedColorAdjust { get; set; } = ForcedColorAdjustType.Auto;
    public ImageRenderingType ImageRendering { get; set; } = ImageRenderingType.Auto;
    public IsolationType Isolation { get; set; } = IsolationType.Auto;
    public MixBlendModeType MixBlendMode { get; set; } = MixBlendModeType.Normal;
    public string? Filter { get; set; }
    public string? BackdropFilter { get; set; }
    public string? ClipPath { get; set; }
    public string? Mask { get; set; }
    public string? MaskImage { get; set; }
    public string? MaskClip { get; set; }
    public string? MaskComposite { get; set; }
    public string? MaskMode { get; set; }
    public string? MaskOrigin { get; set; }
    public string? MaskPosition { get; set; }
    public string? MaskRepeat { get; set; }
    public string? MaskSize { get; set; }
    public LineBreakType LineBreak { get; set; } = LineBreakType.Auto;
    public TextJustifyType TextJustify { get; set; } = TextJustifyType.Auto;
    public ResizeType Resize { get; set; } = ResizeType.None;
    public string? HangingPunctuation { get; set; } = "none";
    public string? RubyAlign { get; set; } = "space-around";
    public string RubyPosition { get; set; } = "over";
    public float BorderSpacing { get; set; }
    public string? BorderImageSource { get; set; }
    public string BorderImageSlice { get; set; } = "100%";
    public string BorderImageWidth { get; set; } = "1";
    public string BorderImageRepeat { get; set; } = "stretch";
    public string BorderImageOutset { get; set; } = "0";
    public float Zoom { get; set; } = 1;
    public int Orphans { get; set; } = 2;
    public int Widows { get; set; } = 2;
    public string FontVariant { get; set; } = "normal";
    public string FontKerning { get; set; } = "auto";
    public string FontStretch { get; set; } = "normal";
    public string FontSynthesis { get; set; } = "weight style";
    public string FontOpticalSizing { get; set; } = "auto";
    public string FontVariationSettings { get; set; } = "normal";
    public string FontFeatureSettings { get; set; } = "normal";
    public float FontSizeAdjust { get; set; }
    public string TextRendering { get; set; } = "auto";
    public string UnicodeBidi { get; set; } = "normal";

    public float GetWidth(float viewportWidth, float rootFontSize)
    {
        if (Width is AutoLength) return float.NaN;
        return Width.ToPixels(viewportWidth, rootFontSize, viewportWidth, 0);
    }

    public float GetHeight(float viewportHeight, float rootFontSize)
    {
        if (Height is AutoLength) return float.NaN;
        return Height.ToPixels(viewportHeight, rootFontSize, 0, viewportHeight);
    }

    /// <summary>
    /// 获取计算后的像素值（用于 getComputedStyle）
    /// </summary>
    public float GetComputedPixel(Length length, float reference = 16f, float rootFontSize = 16f,
        float viewportWidth = 0, float viewportHeight = 0)
    {
        if (length is PixelLength p) return p.Value;
        if (length is AutoLength) return float.NaN;
        return length.ToPixels(reference, rootFontSize, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// 格式化长度为 CSS 像素字符串
    /// </summary>
    public string FormatComputedLength(Length length, float reference = 16f)
    {
        var px = GetComputedPixel(length, reference, FontSize, 0, 0);
        if (float.IsNaN(px)) return "auto";
        return $"{px:F1}px";
    }

    public ComputedStyle Clone()
    {
        return new ComputedStyle
        {
            Width = Width, Height = Height,
            Top = Top, Left = Left, Right = Right, Bottom = Bottom,
            MarginTop = MarginTop, MarginRight = MarginRight, MarginBottom = MarginBottom, MarginLeft = MarginLeft,
            PaddingTop = PaddingTop, PaddingRight = PaddingRight, PaddingBottom = PaddingBottom, PaddingLeft = PaddingLeft,
            BorderTopWidth = BorderTopWidth, BorderRightWidth = BorderRightWidth,
            BorderBottomWidth = BorderBottomWidth, BorderLeftWidth = BorderLeftWidth,
            BorderTopColor = BorderTopColor, BorderRightColor = BorderRightColor,
            BorderBottomColor = BorderBottomColor, BorderLeftColor = BorderLeftColor,
            BorderTopStyle = BorderTopStyle, BorderRightStyle = BorderRightStyle,
            BorderBottomStyle = BorderBottomStyle, BorderLeftStyle = BorderLeftStyle,
            BorderTopLeftRadius = BorderTopLeftRadius, BorderTopRightRadius = BorderTopRightRadius,
            BorderBottomRightRadius = BorderBottomRightRadius, BorderBottomLeftRadius = BorderBottomLeftRadius,
            Display = Display, Position = Position, Float = Float, Clear = Clear,
            FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight,
            FontStyle = FontStyle, LineHeight = LineHeight,
            Color = Color, BackgroundColor = BackgroundColor, BackgroundImage = BackgroundImage,
            BackgroundPositionX = BackgroundPositionX, BackgroundPositionY = BackgroundPositionY,
            BackgroundRepeat = BackgroundRepeat, BackgroundAttachment = BackgroundAttachment,
            TextAlign = TextAlign, TextDecoration = TextDecoration, VerticalAlign = VerticalAlign,
            WhiteSpace = WhiteSpace, WordBreak = WordBreak, OverflowWrap = OverflowWrap,
            Overflow = Overflow, OverflowX = OverflowX, OverflowY = OverflowY,
            Visibility = Visibility, ZIndex = ZIndex, Cursor = Cursor, Opacity = Opacity,
            BoxShadow = BoxShadow, BackgroundSize = BackgroundSize,
            BackgroundSizeWidth = BackgroundSizeWidth, BackgroundSizeHeight = BackgroundSizeHeight,
            FlexDirection = FlexDirection, FlexWrap = FlexWrap, FlexGrow = FlexGrow,
            FlexShrink = FlexShrink, FlexBasis = FlexBasis, JustifyContent = JustifyContent,
            AlignItems = AlignItems, AlignSelf = AlignSelf,
            MinWidth = MinWidth, MaxWidth = MaxWidth, MinHeight = MinHeight, MaxHeight = MaxHeight,
            BoxSizing = BoxSizing, BorderCollapse = BorderCollapse,
            ListStyleType = ListStyleType, ListStyleImage = ListStyleImage, ListStylePosition = ListStylePosition,
            Transform = Transform, TransformOrigin = TransformOrigin,
            Transition = Transition, TransitionDelay = TransitionDelay, TransitionDuration = TransitionDuration,
            TransitionProperty = TransitionProperty, TransitionTimingFunction = TransitionTimingFunction,
            Animation = Animation, AnimationName = AnimationName, AnimationDuration = AnimationDuration,
            AnimationTimingFunction = AnimationTimingFunction, AnimationDelay = AnimationDelay,
            AnimationIterationCount = AnimationIterationCount, AnimationDirection = AnimationDirection,
            AnimationFillMode = AnimationFillMode, AnimationPlayState = AnimationPlayState,
            PointerEvents = PointerEvents, UserSelect = UserSelect,
            Direction = Direction, LetterSpacing = LetterSpacing, WordSpacing = WordSpacing,
            TextIndent = TextIndent, TextTransform = TextTransform,
            TextOverflow = TextOverflow, TextShadow = TextShadow,
            TextDecorationLine = TextDecorationLine, TextDecorationStyle = TextDecorationStyle,
            TextDecorationColor = TextDecorationColor, TextDecorationThickness = TextDecorationThickness,
            TextUnderlineOffset = TextUnderlineOffset,
            TextEmphasis = TextEmphasis, TextEmphasisColor = TextEmphasisColor, TextEmphasisStyle = TextEmphasisStyle,
            RowGap = RowGap, ColumnGap = ColumnGap,
            OutlineWidth = OutlineWidth, OutlineColor = OutlineColor, OutlineStyle = OutlineStyle, OutlineOffset = OutlineOffset,
            TableLayout = TableLayout, CaptionSide = CaptionSide, EmptyCells = EmptyCells, Content = Content,
            CounterIncrement = CounterIncrement, CounterReset = CounterReset, CounterSet = CounterSet, Quotes = Quotes,
            Order = Order, AspectRatio = AspectRatio, ObjectFit = ObjectFit,
            ObjectPositionX = ObjectPositionX, ObjectPositionY = ObjectPositionY,
            FlexFlow = FlexFlow, AlignContent = AlignContent, JustifyItems = JustifyItems, JustifySelf = JustifySelf,
            PlaceContent = PlaceContent, PlaceItems = PlaceItems, PlaceSelf = PlaceSelf,
            GridTemplateColumns = GridTemplateColumns, GridTemplateRows = GridTemplateRows, GridTemplateAreas = GridTemplateAreas,
            GridAutoColumns = GridAutoColumns, GridAutoRows = GridAutoRows, GridAutoFlow = GridAutoFlow,
            GridColumnStart = GridColumnStart, GridColumnEnd = GridColumnEnd,
            GridRowStart = GridRowStart, GridRowEnd = GridRowEnd,
            GridColumn = GridColumn, GridRow = GridRow, GridArea = GridArea, Grid = Grid,
            BackgroundClip = BackgroundClip, BackgroundOrigin = BackgroundOrigin, BackgroundBlendMode = BackgroundBlendMode,
            WritingMode = WritingMode, Hyphens = Hyphens, TabSize = TabSize,
            ScrollBehavior = ScrollBehavior, OverscrollBehavior = OverscrollBehavior,
            OverscrollBehaviorX = OverscrollBehaviorX, OverscrollBehaviorY = OverscrollBehaviorY,
            OverflowAnchor = OverflowAnchor, Contain = Contain, ContentVisibility = ContentVisibility,
            WillChange = WillChange, AccentColor = AccentColor, CaretColor = CaretColor,
            ColorScheme = ColorScheme, ForcedColorAdjust = ForcedColorAdjust,
            ImageRendering = ImageRendering, Isolation = Isolation, MixBlendMode = MixBlendMode,
            Filter = Filter, BackdropFilter = BackdropFilter, ClipPath = ClipPath,
            Mask = Mask, MaskImage = MaskImage, MaskClip = MaskClip, MaskComposite = MaskComposite,
            MaskMode = MaskMode, MaskOrigin = MaskOrigin, MaskPosition = MaskPosition,
            MaskRepeat = MaskRepeat, MaskSize = MaskSize,
            LineBreak = LineBreak, TextJustify = TextJustify, Resize = Resize,
            HangingPunctuation = HangingPunctuation, RubyAlign = RubyAlign, RubyPosition = RubyPosition,
            BorderSpacing = BorderSpacing, Zoom = Zoom, Orphans = Orphans, Widows = Widows,
            BorderImageSource = BorderImageSource, BorderImageSlice = BorderImageSlice,
            BorderImageWidth = BorderImageWidth, BorderImageRepeat = BorderImageRepeat, BorderImageOutset = BorderImageOutset,
            FontVariant = FontVariant, FontKerning = FontKerning, FontStretch = FontStretch,
            FontSynthesis = FontSynthesis, FontOpticalSizing = FontOpticalSizing,
            FontVariationSettings = FontVariationSettings, FontFeatureSettings = FontFeatureSettings,
            FontSizeAdjust = FontSizeAdjust, TextRendering = TextRendering, UnicodeBidi = UnicodeBidi
        };
    }

    public static ComputedStyle CreateDefault() => new();
}

public enum DisplayType { Block, Inline, InlineBlock, Flex, InlineFlex, ListItem, Table, TableRow, TableRowGroup, TableHeaderGroup, TableFooterGroup, TableCell, TableCaption, TableColumnGroup, TableColumn, Ruby, Contents, None }
public enum PositionType { Static, Relative, Absolute, Fixed, Sticky }
public enum FloatType { None, Left, Right }
public enum ClearType { None, Left, Right, Both }
public enum BorderStyle { None, Solid, Dashed, Dotted, Double, Groove, Ridge, Inset, Outset }
public enum FontWeight { Normal = 400, Bold = 700 }
public enum FontStyleType { Normal, Italic, Oblique }
public enum TextAlignType { Start, End, Left, Right, Center, Justify }
public enum TextDecorationType { None, Underline, Overline, LineThrough }
public enum VerticalAlignType { Baseline, Top, Middle, Bottom, Sub, Super, TextTop, TextBottom, Inherit }
public enum WhiteSpaceMode { Normal, Nowrap, Pre, PreWrap, PreLine }
public enum WordBreakMode { Normal, BreakAll, BreakWord }
public enum OverflowWrapMode { Normal, BreakWord, Anywhere }
public enum OverflowType { Visible, Hidden, Scroll, Auto }
public enum VisibilityType { Visible, Hidden, Collapse }
public enum FlexDirectionType { Row, RowReverse, Column, ColumnReverse }
public enum FlexWrapType { NoWrap, Wrap, WrapReverse }
public enum JustifyContentType { FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum AlignItemsType { Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum AlignSelfType { Auto, Stretch, FlexStart, FlexEnd, Center, Baseline }
public enum BackgroundRepeat { Repeat, RepeatX, RepeatY, NoRepeat }
public enum BackgroundAttachment { Scroll, Fixed, Local }
public enum BoxSizingType { ContentBox, BorderBox }
public enum ListStyleType { Disc, Circle, Square, Decimal, DecimalLeadingZero, LowerRoman, UpperRoman, LowerAlpha, UpperAlpha, None }
public enum ListStylePosition { Inside, Outside }

public static class LengthExtensions
{
    public static string ToCssString(this Length? length)
    {
        if (length == null) return "0px";
        try
        {
            return length.ToString();
        }
        catch
        {
            return "0px";
        }
    }
}

public enum BackgroundSizeType { Auto, Cover, Contain, Length }
public enum ObjectFitType { Fill, Contain, Cover, None, ScaleDown }
public enum OverflowAnchorType { Auto, None }
public enum ContainType { None, Strict, Content, Layout, Paint, Size }
public enum ContentVisibilityType { Visible, Auto, Hidden }
public enum ScrollBehaviorType { Auto, Smooth }
public enum OverscrollBehaviorType { Auto, Contain, None }
public enum ImageRenderingType { Auto, CrispEdges, Pixelated }
public enum IsolationType { Auto, Isolate }
public enum MixBlendModeType { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }
public enum LineBreakType { Auto, Loose, Normal, Strict, Anywhere }
public enum TextJustifyType { Auto, InterWord, InterCharacter, None }
public enum HyphensType { None, Manual, Auto }
public enum WritingModeType { HorizontalTb, VerticalRl, VerticalLr }
public enum ResizeType { None, Both, Horizontal, Vertical }
public enum ForcedColorAdjustType { Auto, None }
public enum TextOverflowType { Clip, Ellipsis }
public enum ColorSchemeType { Normal, Light, Dark, Only }
public enum BackgroundClipType { BorderBox, PaddingBox, ContentBox, Text }
public enum BackgroundOriginType { PaddingBox, BorderBox, ContentBox }
public enum BackgroundBlendModeType { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }
public enum TextDecorationLineType { None, Underline, Overline, LineThrough }
public enum TextDecorationStyleType { Solid, Double, Dotted, Dashed, Wavy }
public enum GridAutoFlowType { Row, Column, Dense }
public enum ZoomType { Normal, Reset }

public record BoxShadowValue(SKColor Color, float OffsetX, float OffsetY, float BlurRadius, float Spread, bool Inset = false);
public record TextShadowValue(SKColor Color, float OffsetX, float OffsetY, float BlurRadius);

public class BoxDimensions
{
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }

    public float BorderTopWidth { get; set; }
    public float BorderRightWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }

    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }

    public static BoxDimensions FromStyle(ComputedStyle style)
    {
        float fontSize = style.FontSize > 0 ? style.FontSize : 16f;
        return new BoxDimensions
        {
            MarginTop = GetPixelFromLength(style.MarginTop, fontSize, 16f, 0, 0),
            MarginRight = GetPixelFromLength(style.MarginRight, fontSize, 16f, 0, 0),
            MarginBottom = GetPixelFromLength(style.MarginBottom, fontSize, 16f, 0, 0),
            MarginLeft = GetPixelFromLength(style.MarginLeft, fontSize, 16f, 0, 0),
            BorderTopWidth = style.BorderTopWidth,
            BorderRightWidth = style.BorderRightWidth,
            BorderBottomWidth = style.BorderBottomWidth,
            BorderLeftWidth = style.BorderLeftWidth,
            PaddingTop = GetPixelFromLength(style.PaddingTop, fontSize, 16f, 0, 0),
            PaddingRight = GetPixelFromLength(style.PaddingRight, fontSize, 16f, 0, 0),
            PaddingBottom = GetPixelFromLength(style.PaddingBottom, fontSize, 16f, 0, 0),
            PaddingLeft = GetPixelFromLength(style.PaddingLeft, fontSize, 16f, 0, 0)
        };
    }

    private static float GetPixelFromLength(Length length, float reference, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (length == null) return 0;
        try
        {
            var px = length.ToPixels(reference, rootFontSize, viewportWidth, viewportHeight);
            if (!float.IsNaN(px)) return px;
        }
        catch
        {
            // ignore and fallback
        }
        return Length.ToPixelsOrDefault(length, reference > 0 ? reference : rootFontSize);
    }

    public float TotalWidth => MarginLeft + BorderLeftWidth + PaddingLeft + MarginRight + BorderRightWidth + PaddingRight;
    public float TotalHeight => MarginTop + BorderTopWidth + PaddingTop + MarginBottom + BorderBottomWidth + PaddingBottom;
}

public class LayoutBox
{
    public SKRect MarginBox { get; set; }
    public SKRect BorderBox { get; set; }
    public SKRect PaddingBox { get; set; }
    public SKRect ContentBox { get; set; }

    public float LineHeight { get; set; } = 16;
    public List<LayoutBox> Children { get; } = new();
    public LayoutBox? Parent { get; set; }
    public int? ZIndex { get; set; }
    public BoxDimensions? Dimensions { get; set; }

    public float Width => ContentBox.Width;
    public float Height => ContentBox.Height;

    public LayoutBox? ContainingBlock { get; set; }

    public List<LineBox>? Lines { get; set; }
    public List<InlineRun>? LineRuns { get; set; }
    public bool IsFloating { get; set; }
    public FloatType Float { get; set; }

    public SKRect AlignToDevice(SKCanvas canvas)
    {
        try
        {
            var m = canvas.TotalMatrix;
            float sx = MathF.Abs(m.ScaleX);
            float sy = MathF.Abs(m.ScaleY);
            if (sx <= 0) sx = 1f;
            if (sy <= 0) sy = 1f;

            float left = MathF.Round(MarginBox.Left * sx) / sx;
            float top = MathF.Round(MarginBox.Top * sy) / sy;
            float right = MathF.Round(MarginBox.Right * sx) / sx;
            float bottom = MathF.Round(MarginBox.Bottom * sy) / sy;
            return new SKRect(left, top, right, bottom);
        }
        catch
        {
            return MarginBox;
        }
    }
}

public class InlineRun
{
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Baseline { get; set; }
    public Node? Node { get; set; }
    public bool IsText { get; set; }
    public SKColor? Color { get; set; }
    public float? FontSize { get; set; }
    public string? FontFamily { get; set; }
}

public class LineBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Baseline { get; set; }
    public float TextAlignOffsetX { get; set; }
    public List<InlineRun> Runs { get; } = new();
}

public class PaintContext
{
    public float CurrentX { get; set; }
    public float CurrentY { get; set; }
    public float AvailableWidth { get; set; }
    public List<LineBox> Lines { get; } = new();
    public LineBox CurrentLine { get; set; } = new();
    public float MaxLineHeight { get; set; } = 16;
    public bool NeedsNewLine { get; set; }
}

// ===== 新增 CSS 枚举格式化器（修复问题5） =====
public static class CssEnumFormatter
{
    public static string ToCssString(this DisplayType display) => display switch
    {
        DisplayType.InlineBlock => "inline-block",
        DisplayType.InlineFlex => "inline-flex",
        DisplayType.ListItem => "list-item",
        DisplayType.TableRowGroup => "table-row-group",
        DisplayType.TableHeaderGroup => "table-header-group",
        DisplayType.TableFooterGroup => "table-footer-group",
        DisplayType.TableColumnGroup => "table-column-group",
        DisplayType.TableCaption => "table-caption",
        _ => display.ToString().ToLowerInvariant()
    };

    public static string ToCssString(this BoxSizingType boxSizing) => boxSizing switch
    {
        BoxSizingType.ContentBox => "content-box",
        BoxSizingType.BorderBox => "border-box",
        _ => boxSizing.ToString().ToLowerInvariant()
    };

    public static string ToCssString(this PositionType position) => position switch
    {
        _ => position.ToString().ToLowerInvariant()
    };

    public static string ToCssString(this BorderStyle style) => style switch
    {
        _ => style.ToString().ToLowerInvariant()
    };
}