using SkiaSharp;

namespace UpBrowser.Core.Dom;

public abstract class Length
{
    public abstract float ToPixels(float reference, float rootFontSize, float viewportWidth, float viewportHeight);

    public static Length Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "auto" || value == "inherit")
            return AutoLength.Instance;

        if (value.EndsWith("px"))
            return new PixelLength(float.Parse(value[..^2]));
        if (value.EndsWith("em"))
            return new EmLength(float.Parse(value[..^2]));
        if (value.EndsWith("rem"))
            return new RemLength(float.Parse(value[..^2]));
        if (value.EndsWith("%"))
            return new PercentLength(float.Parse(value[..^2]) / 100f);
        if (value.EndsWith("cqw"))
            return new VwLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqh"))
            return new VhLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqi"))
            return new VwLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqb"))
            return new VhLength(float.Parse(value[..^3]));
        if (value.EndsWith("cqmin"))
            return new VwLength(float.Parse(value[..^5]));
        if (value.EndsWith("cqmax"))
            return new VwLength(float.Parse(value[..^5]));
        if (value.EndsWith("dvw"))
            return new VwLength(float.Parse(value[..^3]));
        if (value.EndsWith("dvh"))
            return new VhLength(float.Parse(value[..^3]));
        if (value.EndsWith("svw"))
            return new VwLength(float.Parse(value[..^3]));
        if (value.EndsWith("svh"))
            return new VhLength(float.Parse(value[..^3]));
        if (value.EndsWith("lvw"))
            return new VwLength(float.Parse(value[..^3]));
        if (value.EndsWith("lvh"))
            return new VhLength(float.Parse(value[..^3]));
        if (value.EndsWith("vmin"))
            return new VwLength(float.Parse(value[..^4]));
        if (value.EndsWith("vmax"))
            return new VwLength(float.Parse(value[..^4]));
        if (value.EndsWith("vw"))
            return new VwLength(float.Parse(value[..^2]));
        if (value.EndsWith("vh"))
            return new VhLength(float.Parse(value[..^2]));
        if (value.EndsWith("vi"))
            return new VwLength(float.Parse(value[..^2]));
        if (value.EndsWith("vb"))
            return new VhLength(float.Parse(value[..^2]));
        if (value == "0")
            return new PixelLength(0);

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
            _ => 16
        };
    }

    public static float ToPixelsOrDefault(Length length, float defaultValue = 0)
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

public class ComputedStyle
{
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
    public string? Transition { get; set; }
    public string? Animation { get; set; }
    public string? PointerEvents { get; set; } = "auto";
    public string? UserSelect { get; set; } = "auto";
    public string Direction { get; set; } = "ltr";
    public float LetterSpacing { get; set; }
    public float WordSpacing { get; set; }
    public float TextIndent { get; set; }
    public string TextTransform { get; set; } = "none";
    public Length RowGap { get; set; } = new PixelLength(0);
    public Length ColumnGap { get; set; } = new PixelLength(0);

    public float OutlineWidth { get; set; }
    public SKColor OutlineColor { get; set; } = SKColors.Black;
    public BorderStyle OutlineStyle { get; set; } = BorderStyle.None;
    public string TableLayout { get; set; } = "auto";
    public string CaptionSide { get; set; } = "top";
    public string EmptyCells { get; set; } = "show";
    public string? Content { get; set; }

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
            Width = Width,
            Height = Height,
            Top = Top,
            Left = Left,
            Right = Right,
            Bottom = Bottom,
            MarginTop = MarginTop,
            MarginRight = MarginRight,
            MarginBottom = MarginBottom,
            MarginLeft = MarginLeft,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            PaddingLeft = PaddingLeft,
            BorderTopWidth = BorderTopWidth,
            BorderRightWidth = BorderRightWidth,
            BorderBottomWidth = BorderBottomWidth,
            BorderLeftWidth = BorderLeftWidth,
            BorderTopColor = BorderTopColor,
            BorderRightColor = BorderRightColor,
            BorderBottomColor = BorderBottomColor,
            BorderLeftColor = BorderLeftColor,
            BorderTopStyle = BorderTopStyle,
            BorderRightStyle = BorderRightStyle,
            BorderBottomStyle = BorderBottomStyle,
            BorderLeftStyle = BorderLeftStyle,
            BorderTopLeftRadius = BorderTopLeftRadius,
            BorderTopRightRadius = BorderTopRightRadius,
            BorderBottomRightRadius = BorderBottomRightRadius,
            BorderBottomLeftRadius = BorderBottomLeftRadius,
            Display = Display,
            Position = Position,
            Float = Float,
            Clear = Clear,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontStyle = FontStyle,
            LineHeight = LineHeight,
            Color = Color,
            BackgroundColor = BackgroundColor,
            BackgroundImage = BackgroundImage,
            BackgroundPositionX = BackgroundPositionX,
            BackgroundPositionY = BackgroundPositionY,
            BackgroundRepeat = BackgroundRepeat,
            BackgroundAttachment = BackgroundAttachment,
            TextAlign = TextAlign,
            TextDecoration = TextDecoration,
            VerticalAlign = VerticalAlign,
            WhiteSpace = WhiteSpace,
            WordBreak = WordBreak,
            OverflowWrap = OverflowWrap,
            Overflow = Overflow,
            OverflowX = OverflowX,
            OverflowY = OverflowY,
            Visibility = Visibility,
            ZIndex = ZIndex,
            Cursor = Cursor,
            Opacity = Opacity,
            BoxShadow = BoxShadow,
            BackgroundSize = BackgroundSize,
            BackgroundSizeWidth = BackgroundSizeWidth,
            BackgroundSizeHeight = BackgroundSizeHeight,
            FlexDirection = FlexDirection,
            FlexWrap = FlexWrap,
            FlexGrow = FlexGrow,
            FlexShrink = FlexShrink,
            FlexBasis = FlexBasis,
            JustifyContent = JustifyContent,
            AlignItems = AlignItems,
            AlignSelf = AlignSelf,
            MinWidth = MinWidth,
            MaxWidth = MaxWidth,
            MinHeight = MinHeight,
            MaxHeight = MaxHeight,
            BoxSizing = BoxSizing,
            BorderCollapse = BorderCollapse,
            ListStyleType = ListStyleType,
            ListStyleImage = ListStyleImage,
            ListStylePosition = ListStylePosition,
            Transform = Transform,
            Transition = Transition,
            Animation = Animation,
            PointerEvents = PointerEvents,
            UserSelect = UserSelect,
            Direction = Direction,
            LetterSpacing = LetterSpacing,
            WordSpacing = WordSpacing,
            TextIndent = TextIndent,
            TextTransform = TextTransform,
            RowGap = RowGap,
            ColumnGap = ColumnGap,
            OutlineWidth = OutlineWidth,
            OutlineColor = OutlineColor,
            OutlineStyle = OutlineStyle,
            TableLayout = TableLayout,
            CaptionSide = CaptionSide,
            EmptyCells = EmptyCells,
            Content = Content
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
public record BoxShadowValue(SKColor Color, float OffsetX, float OffsetY, float BlurRadius, float Spread);

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
        return new BoxDimensions
        {
            MarginTop = GetPixelFromLength(style.MarginTop, 16f, 16f, 0, 0),
            MarginRight = GetPixelFromLength(style.MarginRight, 16f, 16f, 0, 0),
            MarginBottom = GetPixelFromLength(style.MarginBottom, 16f, 16f, 0, 0),
            MarginLeft = GetPixelFromLength(style.MarginLeft, 16f, 16f, 0, 0),
            BorderTopWidth = style.BorderTopWidth,
            BorderRightWidth = style.BorderRightWidth,
            BorderBottomWidth = style.BorderBottomWidth,
            BorderLeftWidth = style.BorderLeftWidth,
            PaddingTop = GetPixelFromLength(style.PaddingTop, 16f, 16f, 0, 0),
            PaddingRight = GetPixelFromLength(style.PaddingRight, 16f, 16f, 0, 0),
            PaddingBottom = GetPixelFromLength(style.PaddingBottom, 16f, 16f, 0, 0),
            PaddingLeft = GetPixelFromLength(style.PaddingLeft, 16f, 16f, 0, 0)
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