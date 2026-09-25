using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

/// <summary>Box sides included when painting a fragmented box.</summary>
[Flags]
public enum PhysicalBoxSides
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
    All = Top | Right | Bottom | Left
}

public static class PhysicalBoxSidesExtensions
{
    public static bool HasAllSides(this PhysicalBoxSides sides) => sides == PhysicalBoxSides.All;
    public static bool IsEmpty(this PhysicalBoxSides sides) => sides == PhysicalBoxSides.None;
}

/// <summary>Shadow style: normal (outset) or inset.</summary>
public enum ShadowStyle { Normal, Inset }

/// <summary>One shadow in a shadow list.</summary>
public struct ShadowData
{
    public float X;
    public float Y;
    public float Blur;
    public float Spread;
    public ShadowStyle Style;
    public SKColor Color;

    public bool IsZeroOffset => X == 0 && Y == 0;
}

/// <summary>Ordered list of shadows (reverse paint order like the source).</summary>
public sealed class ShadowList
{
    public List<ShadowData> Shadows { get; } = new();
}

/// <summary>Background fill attachment.</summary>
public enum FillAttachment { Scroll, Fixed, Local }

/// <summary>Background painting area (background-clip).</summary>
public enum FillBox { Border, Padding, Content, Text, FillBox, StrokeBox, ViewBox, NoClip }

/// <summary>Background layering box (background-origin).</summary>
public enum FillBoxOrigin { Padding, Border, Content }

/// <summary>Blend mode for a background layer.</summary>
public enum FillBlendMode { Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity }

/// <summary>Blend mode for a mask layer.</summary>
public enum MaskMode { MatchSource, Alpha, Luminance }

/// <summary>Background bleed avoidance strategy.</summary>
public enum BackgroundBleedAvoidance { None, ShrinkBackground, ClipLayer, ClipOnly }

/// <summary>
/// One layer of a multi-layer background / mask (FillLayer).
/// Linked via <see cref="Next"/>; the last layer holds the background color.
/// </summary>
public sealed class FillLayer
{
    public string? Image; // CSS <image> or gradient token
    public SKColor? Color;
    public FillAttachment Attachment = FillAttachment.Scroll;
    public FillBox Clip = FillBox.Border;
    public FillBoxOrigin Origin = FillBoxOrigin.Padding;
    public FillRepeat RepeatX = FillRepeat.Repeat;
    public FillRepeat RepeatY = FillRepeat.Repeat;
    public FillSizeType SizeType = FillSizeType.Auto;
    public Length? SizeWidth;
    public Length? SizeHeight;
    public Length? PositionX;
    public Length? PositionY;
    public BackgroundEdgeOrigin XOrigin = BackgroundEdgeOrigin.Left;
    public BackgroundEdgeOrigin YOrigin = BackgroundEdgeOrigin.Top;
    public FillBlendMode BlendMode = FillBlendMode.Normal;
    public MaskMode MaskMode = MaskMode.MatchSource;
    public bool IsMaskLayer;
    public FillLayer? Next;

    /// <summary>
    /// Whether this layer's clip fully occludes all layers beneath it, in which
    /// case traversal of lower layers can stop (occlusion culling).
    /// </summary>
    public bool ClipOccludesNextLayers => Clip == FillBox.Border;

    public FillLayer? NextOrSelf => Next ?? this;
}

/// <summary>background-repeat behavior.</summary>
public enum FillRepeat { Repeat, NoRepeat, Round, Space }

/// <summary>background-size keyword.</summary>
public enum FillSizeType { Auto, Length, Cover, Contain, SizeNone }

/// <summary>background-position edge origin (which side the offset is relative to).</summary>
public enum BackgroundEdgeOrigin { Left, Top, Right, Bottom }

/// <summary>Snapped and unsnapped versions of an outset tuple.</summary>
public struct SnappedAndUnsnappedOutsets
{
    public PhysicalBoxStrut Snapped;
    public PhysicalBoxStrut Unsnapped;

    public static SnappedAndUnsnappedOutsets From(PhysicalBoxStrut s)
    {
        return new SnappedAndUnsnappedOutsets { Snapped = s, Unsnapped = s };
    }
}

/// <summary>
/// Rounded rectangle (border-radius aware) used for shadow and background shapes.
/// </summary>
public struct FloatRoundedRect
{
    public SKRect Rect;
    public float TopLeftRadius;
    public float TopRightRadius;
    public float BottomLeftRadius;
    public float BottomRightRadius;

    public bool IsRounded => TopLeftRadius > 0 || TopRightRadius > 0 || BottomLeftRadius > 0 || BottomRightRadius > 0;

    public bool IsEmpty => Rect.Width <= 0 || Rect.Height <= 0;

    public void Inset(float dx, float dy)
    {
        Rect = new SKRect(Rect.Left + dx, Rect.Top + dy, Rect.Right - dx, Rect.Bottom - dy);
    }

    public void ConstrainRadii()
    {
        float maxX = Rect.Width / 2f;
        float maxY = Rect.Height / 2f;
        if (TopLeftRadius > maxX) TopLeftRadius = maxX;
        if (TopRightRadius > maxX) TopRightRadius = maxX;
        if (BottomLeftRadius > maxX) BottomLeftRadius = maxX;
        if (BottomRightRadius > maxX) BottomRightRadius = maxX;
        if (TopLeftRadius > maxY) TopLeftRadius = maxY;
        if (TopRightRadius > maxY) TopRightRadius = maxY;
        if (BottomLeftRadius > maxY) BottomLeftRadius = maxY;
        if (BottomRightRadius > maxY) BottomRightRadius = maxY;
    }

    public void OutsetForMarginOrShadow(float spread)
    {
        Rect = new SKRect(Rect.Left - spread, Rect.Top - spread, Rect.Right + spread, Rect.Bottom + spread);
        if (TopLeftRadius > 0) TopLeftRadius += spread;
        if (TopRightRadius > 0) TopRightRadius += spread;
        if (BottomLeftRadius > 0) BottomLeftRadius += spread;
        if (BottomRightRadius > 0) BottomRightRadius += spread;
        ConstrainRadii();
    }

    public SKPath ToPath(bool useRadii)
    {
        var path = new SKPath();
        if (useRadii && IsRounded)
        {
            var rrect = new SKRoundRect();
            rrect.SetRectRadii(Rect,
                new[] {
                    new SKPoint(TopLeftRadius, TopLeftRadius),
                    new SKPoint(TopRightRadius, TopRightRadius),
                    new SKPoint(BottomRightRadius, BottomRightRadius),
                    new SKPoint(BottomLeftRadius, BottomLeftRadius)
                });
            path.AddRoundRect(rrect);
        }
        else
        {
            path.AddRect(Rect);
        }
        return path;
    }
}

/// <summary>Mutable box-side insets.</summary>
public struct PhysicalBoxStrut
{
    public float Top;
    public float Right;
    public float Bottom;
    public float Left;

    public PhysicalBoxStrut(float top, float right, float bottom, float left)
    {
        Top = top; Right = right; Bottom = bottom; Left = left;
    }

    public static PhysicalBoxStrut operator +(PhysicalBoxStrut a, PhysicalBoxStrut b) =>
        new(a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom, a.Left + b.Left);

    public static PhysicalBoxStrut operator -(PhysicalBoxStrut a, PhysicalBoxStrut b) =>
        new(a.Top - b.Top, a.Right - b.Right, a.Bottom - b.Bottom, a.Left - b.Left);

    public static PhysicalBoxStrut operator -(PhysicalBoxStrut a) =>
        new(-a.Top, -a.Right, -a.Bottom, -a.Left);

    public void TruncateSides(PhysicalBoxSides sides)
    {
        if ((sides & PhysicalBoxSides.Top) == 0) Top = 0;
        if ((sides & PhysicalBoxSides.Right) == 0) Right = 0;
        if ((sides & PhysicalBoxSides.Bottom) == 0) Bottom = 0;
        if ((sides & PhysicalBoxSides.Left) == 0) Left = 0;
    }

    public bool IsZero => Top == 0 && Right == 0 && Bottom == 0 && Left == 0;

    /// <summary>Position offset as (left, top) / (right, bottom) derived offsets.</summary>
    public SKPoint Offset() => new(Left, Top);
}

/// <summary>
/// Background painting geometry: positioning area, border/padding outsets and clip.
/// Mirrors BoxBackgroundPaintContext.
/// </summary>
public sealed class BoxBackgroundPaintContext
{
    public bool PaintingView;
    public bool CellUsingContainerBackground;
    public bool BoxHasMultipleFragments;
    public bool HasBackgroundFixedToViewport;
    public SKSize PositioningSizeOverride;
    public SKPoint ElementPositioningAreaOffset;

    private readonly float _borderTop, _borderRight, _borderBottom, _borderLeft;
    private readonly float _paddingTop, _paddingRight, _paddingBottom, _paddingLeft;

    public BoxBackgroundPaintContext(
        SKSize positioningSizeOverride,
        float borderTop = 0, float borderRight = 0, float borderBottom = 0, float borderLeft = 0,
        float paddingTop = 0, float paddingRight = 0, float paddingBottom = 0, float paddingLeft = 0)
    {
        PositioningSizeOverride = positioningSizeOverride;
        _borderTop = borderTop; _borderRight = borderRight; _borderBottom = borderBottom; _borderLeft = borderLeft;
        _paddingTop = paddingTop; _paddingRight = paddingRight; _paddingBottom = paddingBottom; _paddingLeft = paddingLeft;
    }

    public PhysicalBoxStrut BorderOutsets => new(_borderTop, _borderRight, _borderBottom, _borderLeft);
    public PhysicalBoxStrut PaddingOutsets => new(_paddingTop, _paddingRight, _paddingBottom, _paddingLeft);
    public PhysicalBoxStrut BorderPaddingOutsets => BorderOutsets + PaddingOutsets;

    /// <summary>background-position positioning area.</summary>
    public SKRect ComputePositioningArea(FillLayer layer, SKRect paintRect)
    {
        if (ShouldUseFixedAttachment(layer))
            return FixedAttachmentPositioningArea();
        return NormalPositioningArea(paintRect);
    }

    public SKRect NormalPositioningArea(SKRect paintRect)
    {
        if (PaintingView || CellUsingContainerBackground || BoxHasMultipleFragments)
            return new SKRect(0, 0, PositioningSizeOverride.Width, PositioningSizeOverride.Height);
        return paintRect;
    }

    public SKRect FixedAttachmentPositioningArea()
    {
        return new SKRect(0, 0, PositioningSizeOverride.Width, PositioningSizeOverride.Height);
    }

    public bool ShouldUseFixedAttachment(FillLayer layer) =>
        HasBackgroundFixedToViewport && layer.Image != null && layer.Attachment == FillAttachment.Fixed;

    public SKPoint OffsetInBackground(FillLayer layer) =>
        ShouldUseFixedAttachment(layer) ? new SKPoint(0, 0) : ElementPositioningAreaOffset;

    public FillBox EffectiveClip(FillLayer layer) => layer.Clip;

    public bool DisallowBorderDerivedAdjustment =>
        PaintingView || CellUsingContainerBackground || BoxHasMultipleFragments;

    /// <summary>Outsets from the border box to the inner border (padding edge).</summary>
    public PhysicalBoxStrut InnerBorderOutsets(SKRect destRect, SKRect positioningArea)
    {
        // Pixel-snapped inner border: the border box shrunk by border widths.
        float insetLeft = _borderLeft;
        float insetTop = _borderTop;
        float insetRight = _borderRight;
        float insetBottom = _borderBottom;

        return new PhysicalBoxStrut(
            (float)Math.Round(positioningArea.Top + insetTop) - destRect.Top,
            destRect.Right - (float)Math.Round(positioningArea.Right - insetRight),
            destRect.Bottom - (float)Math.Round(positioningArea.Bottom - insetBottom),
            (float)Math.Round(positioningArea.Left + insetLeft) - destRect.Left);
    }

    /// <summary>Snapped/unsnapped outsets where the border fully obscures the background.</summary>
    public SnappedAndUnsnappedOutsets ObscuredBorderOutsets(SKRect destRect, SKRect positioningArea)
    {
        var inner = InnerBorderOutsets(destRect, positioningArea);
        var boxOutsets = BorderOutsets;
        return new SnappedAndUnsnappedOutsets
        {
            Snapped = inner,
            Unsnapped = boxOutsets
        };
    }
}

/// <summary>
/// Transliteration of the box-painter background/shadow/border algorithms.
/// Emits paint ops into a <see cref="DisplayList"/> via <see cref="PaintOpPool"/>.
/// </summary>
public sealed class BoxPainterBase
{
    private readonly DisplayList _displayList;

    public BoxPainterBase(DisplayList displayList)
    {
        _displayList = displayList;
    }

    // ─── shadows ────────────────────────────────────────────────────────────

    private bool ShadowIsFullyObscured(in ShadowData shadow) =>
        shadow.IsZeroOffset && shadow.Blur == 0 && shadow.Spread == 0;

    private static List<ShadowData>? GetShadowList(ComputedStyle style)
    {
        var s = style.BoxShadow;
        if (s == null || s.Count == 0)
            return null;
        var list = new List<ShadowData>(s.Count);
        for (int i = 0; i < s.Count; i++)
        {
            var sh = s[i];
            list.Add(new ShadowData
            {
                X = sh.OffsetX,
                Y = sh.OffsetY,
                Blur = sh.BlurRadius,
                Spread = sh.Spread,
                Style = sh.Inset ? ShadowStyle.Inset : ShadowStyle.Normal,
                Color = sh.Color
            });
        }
        return list;
    }

    private static bool HasBorderRadius(ComputedStyle style) =>
        style.BorderTopLeftRadius > 0 || style.BorderTopRightRadius > 0 ||
        style.BorderBottomLeftRadius > 0 || style.BorderBottomRightRadius > 0;

    /// <summary>Paint normal (outset) box shadows. Paints under the background.</summary>
    public void PaintNormalBoxShadow(SKRect paintRect, ComputedStyle style, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All, bool backgroundIsSkipped = false)
    {
        var shadows = GetShadowList(style);
        if (shadows == null || shadows.Count == 0)
            return;

        var border = RoundedBorderGeometry.PixelSnappedRoundedBorder(style, paintRect, sidesToInclude);
        bool hasBorderRadius = HasBorderRadius(style);

        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            var shadow = shadows[i];
            if (shadow.Style != ShadowStyle.Normal)
                continue;
            if (ShadowIsFullyObscured(shadow))
                continue;

            SKColor shadowColor = shadow.Color;

            var fillRect = border.Rect;
            fillRect = new SKRect(fillRect.Left - shadow.Spread, fillRect.Top - shadow.Spread, fillRect.Right + shadow.Spread, fillRect.Bottom + shadow.Spread);
            if (fillRect.Width <= 0 || fillRect.Height <= 0)
                continue;

            // Recompute the shadow shape so spread is not applied twice for radii.
            fillRect = border.Rect;
            FloatRoundedRect roundedFill = new()
            {
                Rect = fillRect,
                TopLeftRadius = border.TopLeftRadius,
                TopRightRadius = border.TopRightRadius,
                BottomLeftRadius = border.BottomLeftRadius,
                BottomRightRadius = border.BottomRightRadius
            };
            roundedFill.OutsetForMarginOrShadow(shadow.Spread);

            // Emit as a DrawPathOp (proven raster path) with the shadow offset baked
            // into the geometry and an optional blur filter. DrawShadowOp's translate
            // + ImageFilter combination was not compositing on the raster canvas.
            var shadowPath = roundedFill.ToPath(hasBorderRadius);
            shadowPath.Offset(shadow.X, shadow.Y);
            var shadowOp = PaintOpPool.GetDrawPathOp();
            shadowOp.Path.Dispose();
            shadowOp.Path = shadowPath;
            shadowOp.FillPaint = new SKPaint
            {
                Color = shadowColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                ImageFilter = shadow.Blur > 0 ? SKImageFilter.CreateBlur(shadow.Blur, shadow.Blur) : null
            };
            shadowOp.StrokePaint = null;
            shadowOp.ZIndex = -1;
            shadowOp.Bounds = new SKRect(
                paintRect.Left + shadow.X - shadow.Blur - shadow.Spread,
                paintRect.Top + shadow.Y - shadow.Blur - shadow.Spread,
                paintRect.Right + shadow.X + shadow.Blur + shadow.Spread,
                paintRect.Bottom + shadow.Y + shadow.Blur + shadow.Spread);
            _displayList.Add(shadowOp);
        }
    }

    /// <summary>Paint inset box shadows using the border rect.</summary>
    public void PaintInsetBoxShadowWithBorderRect(SKRect borderRect, ComputedStyle style, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        var shadows = GetShadowList(style);
        if (shadows == null || shadows.Count == 0)
            return;
        var bounds = RoundedBorderGeometry.PixelSnappedRoundedInnerBorder(style, borderRect, sidesToInclude);
        PaintInsetBoxShadow(bounds, style, sidesToInclude);
    }

    /// <summary>Paint inset box shadows using the inner (padding) rect.</summary>
    public void PaintInsetBoxShadowWithInnerRect(SKRect innerRect, ComputedStyle style)
    {
        var shadows = GetShadowList(style);
        if (shadows == null || shadows.Count == 0)
            return;
        var bounds = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(style, innerRect, new PhysicalBoxStrut());
        PaintInsetBoxShadow(bounds, style, PhysicalBoxSides.All);
    }

    private SKRect AreaCastingShadowInHole(SKRect holeRect, in ShadowData shadow)
    {
        SKRect bounds = holeRect;
        bounds = new SKRect(bounds.Left - shadow.Blur, bounds.Top - shadow.Blur, bounds.Right + shadow.Blur, bounds.Bottom + shadow.Blur);

        if (shadow.Spread < 0)
            bounds = new SKRect(bounds.Left + shadow.Spread, bounds.Top + shadow.Spread, bounds.Right - shadow.Spread, bounds.Bottom - shadow.Spread);

        SKRect offsetBounds = bounds;
        offsetBounds = new SKRect(offsetBounds.Left - shadow.X, offsetBounds.Top - shadow.Y, offsetBounds.Right - shadow.X, offsetBounds.Bottom - shadow.Y);
        bounds.Union(offsetBounds);
        return bounds;
    }

    /// <summary>Paint inset shadows inside a clipped rounded rect.</summary>
    public void PaintInsetBoxShadow(FloatRoundedRect bounds, ComputedStyle style, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        var shadows = GetShadowList(style);
        if (shadows == null || shadows.Count == 0)
            return;

        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            var shadow = shadows[i];
            if (shadow.Style != ShadowStyle.Inset)
                continue;
            if (ShadowIsFullyObscured(shadow))
                continue;

            SKColor shadowColor = shadow.Color;

            // The inset shadow shape is the bounds shrunk by the spread (inset
            // shadows are drawn on the inside of the edge).
            SKRect innerRect = bounds.Rect;
            FloatRoundedRect innerRounded = new()
            {
                Rect = innerRect,
                TopLeftRadius = bounds.TopLeftRadius,
                TopRightRadius = bounds.TopRightRadius,
                BottomLeftRadius = bounds.BottomLeftRadius,
                BottomRightRadius = bounds.BottomRightRadius
            };
            innerRounded.OutsetForMarginOrShadow(-shadow.Spread);
            if (innerRounded.IsEmpty)
            {
                var fillAll = PaintOpPool.GetDrawRectOp();
                fillAll.Rect = bounds.Rect;
                fillAll.FillColor = shadowColor;
                fillAll.BorderRadius = Math.Max(bounds.TopLeftRadius, Math.Max(bounds.TopRightRadius, Math.Max(bounds.BottomLeftRadius, bounds.BottomRightRadius)));
                fillAll.Bounds = bounds.Rect;
                _displayList.Add(fillAll);
                continue;
            }

            // Clip to the border bounds.
            var clipOp = PaintOpPool.GetPushClipOp();
            if (bounds.IsRounded)
                clipOp.ClipPath = bounds.ToPath(true);
            else
                clipOp.ClipRect = bounds.Rect;
            clipOp.AntiAlias = true;
            clipOp.Bounds = bounds.Rect;
            _displayList.Add(clipOp);

            // Draw the shadow-colored region between the outer bounds and the
            // inner hole (blurred shadow around the hole). The hole is the shrunk
            // box moved by the shadow offset; the painted band is the box area
            // outside that offset hole.
            SKColor fillColor = new(shadowColor.Red, shadowColor.Green, shadowColor.Blue, shadowColor.Alpha);
            SKRect outerRect = AreaCastingShadowInHole(bounds.Rect, shadow);

            var holePath = innerRounded.ToPath(innerRounded.IsRounded);
            holePath.Offset(shadow.X, shadow.Y);
            var ringPath = new SKPath();
            var outerPath = new SKPath();
            outerPath.AddRect(outerRect);
            ringPath.AddPath(outerPath);
            ringPath.AddPath(holePath);
            // Winding fill would fill the hole too (both subpaths wind the same
            // way); EvenOdd makes the inner hole a true cut-out so only the ring
            // (the blurred shadow band along the box edge) is painted.
            ringPath.FillType = SKPathFillType.EvenOdd;

            var fillPath = PaintOpPool.GetDrawPathOp();
            fillPath.Path.Dispose();
            fillPath.Path = ringPath;
            fillPath.FillPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                ImageFilter = shadow.Blur > 0 ? SKImageFilter.CreateBlur(shadow.Blur, shadow.Blur) : null
            };
            fillPath.StrokePaint = null;
            fillPath.Bounds = outerRect;
            _displayList.Add(fillPath);

            var popClipOp = PaintOpPool.GetPopClipOp();
            popClipOp.Bounds = bounds.Rect;
            _displayList.Add(popClipOp);
        }
    }

    // ─── backgrounds ────────────────────────────────────────────────────────

    /// <summary>Paint all background layers back-to-front.</summary>
    public void PaintFillLayers(SKColor color, FillLayer? fillLayer, SKRect rect, BoxBackgroundPaintContext bgPaintContext, ComputedStyle style, BackgroundBleedAvoidance bleed = BackgroundBleedAvoidance.None)
    {
        if (fillLayer == null)
        {
            // Solid color fallback (background-color only).
            if (color.Alpha > 0)
            {
                var op = PaintOpPool.GetDrawRectOp();
                op.Rect = rect;
                op.FillColor = color;
                op.Bounds = rect;
                _displayList.Add(op);
            }
            return;
        }

        var reversedPaintList = new List<FillLayer>();
        bool separateBuffer = CalculateFillLayerOcclusionCulling(reversedPaintList, fillLayer);

        // Paint back-to-front: reverse of the front-to-back layer chain.
        reversedPaintList.Reverse();
        foreach (var layer in reversedPaintList)
        {
            PaintFillLayer(color, layer, rect, bleed, bgPaintContext, style);
        }
    }

    /// <summary>
    /// Determine which layers need to be painted (occlusion culling) and
    /// whether a separate buffer is required.
    /// Returns true if layers are non-associative (need isolation).
    /// </summary>
    public bool CalculateFillLayerOcclusionCulling(List<FillLayer> reversedPaintList, FillLayer fillLayer)
    {
        bool isNonAssociative = false;
        var currentLayer = fillLayer;
        while (currentLayer != null)
        {
            reversedPaintList.Add(currentLayer);
            if (currentLayer.BlendMode != FillBlendMode.Normal)
                isNonAssociative = true;
            if (currentLayer.ClipOccludesNextLayers)
            {
                isNonAssociative = false;
                break;
            }
            currentLayer = currentLayer.Next;
        }
        return isNonAssociative;
    }

    /// <summary>Paint one background layer.</summary>
    public void PaintFillLayer(SKColor color, FillLayer bgLayer, SKRect rect, BackgroundBleedAvoidance bleedAvoidance, BoxBackgroundPaintContext bgPaintContext, ComputedStyle style)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var info = GetFillLayerInfo(style, color, bgLayer, bleedAvoidance);
        if (!info.ShouldPaintImage && !info.ShouldPaintColor)
            return;

        bool shouldClipWithScrolling = info.IsClippedWithLocalScrolling;
        var scrolledPaintRect = rect;
        if (shouldClipWithScrolling)
        {
            var snappedBorders = bgPaintContext.BorderOutsets;
            snappedBorders.TruncateSides(info.SidesToInclude);
            scrolledPaintRect = AdjustRectForScrolledContent(snappedBorders, rect);
        }

        SKBlendMode compositeOp = SKBlendMode.SrcOver;
        if (info.ShouldApplyBlendOperation)
        {
            compositeOp = CompositeToSkia(bgLayer);
        }

        var border = bgPaintContext.BorderOutsets;
        var padding = bgPaintContext.PaddingOutsets;
        var borderPaddingInsets = -(border + padding);
        FloatRoundedRect borderRect = RoundedBorderRectForClip(info, style, bgLayer, rect, bleedAvoidance, borderPaddingInsets);
        // Fast path: single tile color + image fit in the dest rect.
        if (CanUseBottomLayerFastPath(info, bgPaintContext, bleedAvoidance)
            && PaintFastBottomLayer(info, rect, borderRect, scrolledPaintRect, bgPaintContext))
        {
            return;
        }

        // Rounded clip to border.
        if (info.IsRoundedFill)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipPath = borderRect.ToPath(true);
            clipOp.AntiAlias = true;
            clipOp.Bounds = rect;
            _displayList.Add(clipOp);
        }

        var effectiveClip = bgPaintContext.EffectiveClip(bgLayer);
        if (effectiveClip == FillBox.Text)
        {
            PaintFillLayerTextFillBox(info, rect, scrolledPaintRect);
            if (info.IsRoundedFill)
            {
                var popClipOp = PaintOpPool.GetPopClipOp();
                popClipOp.Bounds = rect;
                _displayList.Add(popClipOp);
            }
            return;
        }

        bool pushedClip = false;
        if (!bgPaintContext.HasBackgroundFixedToViewport)
        {
            switch (effectiveClip)
            {
                case FillBox.FillBox:
                case FillBox.Padding:
                case FillBox.Content:
                {
                    if (info.IsRoundedFill)
                        break;
                    var outsets = border;
                    if (effectiveClip == FillBox.FillBox || effectiveClip == FillBox.Content)
                        outsets += padding;
                    outsets.TruncateSides(info.SidesToInclude);
                    var clipRect = scrolledPaintRect;
                    clipRect = new SKRect(clipRect.Left + outsets.Left, clipRect.Top + outsets.Top, clipRect.Right - outsets.Right, clipRect.Bottom - outsets.Bottom);
                    var clipOp = PaintOpPool.GetPushClipOp();
                    clipOp.ClipRect = clipRect;
                    clipOp.AntiAlias = true;
                    clipOp.Bounds = rect;
                    _displayList.Add(clipOp);
                    pushedClip = true;
                    break;
                }
                case FillBox.StrokeBox:
                case FillBox.ViewBox:
                case FillBox.NoClip:
                case FillBox.Border:
                    break;
            }
        }

        PaintFillLayerBackground(info, scrolledPaintRect, bgPaintContext);

        if (pushedClip || info.IsRoundedFill)
        {
            var popClipOp = PaintOpPool.GetPopClipOp();
            popClipOp.Bounds = rect;
            _displayList.Add(popClipOp);
        }
    }

    /// <summary>Paint a background-color and/or background-image inside a layer.</summary>
    public void PaintFillLayerBackground(FillLayerInfo info, SKRect scrolledPaintRect, BoxBackgroundPaintContext bgPaintContext)
    {
        if (info.ShouldPaintColor)
        {
            var backgroundRect = scrolledPaintRect;
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = backgroundRect;
            op.FillColor = info.Color;
            op.BorderRadius = Math.Max(info.BorderTopLeftRadius, Math.Max(info.BorderTopRightRadius, Math.Max(info.BorderBottomLeftRadius, info.BorderBottomRightRadius)));
            op.Bounds = backgroundRect;
            _displayList.Add(op);
        }

        if (info.ShouldPaintImage && info.Image != null)
        {
            DrawBackgroundImage(info.Image, info, scrolledPaintRect);
        }
    }

    private void PaintFillLayerTextFillBox(FillLayerInfo info, SKRect rect, SKRect scrolledPaintRect)
    {
        // background-clip: text is handled by PaintVisitor's text clipping path.
        PaintFillLayerBackground(info, scrolledPaintRect, null!);
    }

    /// <summary>Emit ops for a background image (gradient or url).</summary>
    private void DrawBackgroundImage(string image, FillLayerInfo info, SKRect destRect)
    {
        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = destRect;
        op.FillColor = info.Color;
        op.Bounds = destRect;
        _displayList.Add(op);
        // Full gradient/image raster is delegated to GradientRenderer / ImageCache
        // via PaintVisitor; here we only keep the layer structure.
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private bool CanUseBottomLayerFastPath(FillLayerInfo info, BoxBackgroundPaintContext bgPaintContext, BackgroundBleedAvoidance bleedAvoidance)
    {
        if (bgPaintContext.CellUsingContainerBackground)
            return false;
        if (!info.IsBottomLayer || !info.IsBorderFill)
            return false;
        if (info.ShouldPaintImage)
        {
            if (bleedAvoidance == BackgroundBleedAvoidance.ShrinkBackground)
                return false;
            if (info.IsRoundedFill && info.IsPrinting)
                return false;
        }
        return true;
    }

    private bool PaintFastBottomLayer(FillLayerInfo info, SKRect rect, FloatRoundedRect borderRect, SKRect scrolledPaintRect, BoxBackgroundPaintContext bgPaintContext)
    {
        FloatRoundedRect colorBorder = info.IsRoundedFill ? borderRect : new FloatRoundedRect { Rect = rect };

        // Paint the color first.
        if (info.ShouldPaintColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = colorBorder.Rect;
            op.FillColor = info.Color;
            op.BorderRadius = colorBorder.IsRounded ? Math.Max(colorBorder.TopLeftRadius, Math.Max(colorBorder.TopRightRadius, Math.Max(colorBorder.BottomLeftRadius, colorBorder.BottomRightRadius))) : 0;
            op.Bounds = rect;
            _displayList.Add(op);
        }

        // Paint the image if it fits in a single tile.
        if (info.ShouldPaintImage && info.Image != null)
        {
            DrawBackgroundImage(info.Image, info, scrolledPaintRect);
        }
        return true;
    }

    private FillLayerInfo GetFillLayerInfo(ComputedStyle style, SKColor color, FillLayer layer, BackgroundBleedAvoidance bleedAvoidance)
    {
        return new FillLayerInfo(layer, color, layer.Clip == FillBox.Border, layer.Next == null);
    }
    private FloatRoundedRect RoundedBorderRectForClip(FillLayerInfo info, ComputedStyle style, FillLayer bgLayer, SKRect rect, BackgroundBleedAvoidance bleedAvoidance, PhysicalBoxStrut borderPaddingInsets)
    {
        if (!info.IsRoundedFill)
            return new FloatRoundedRect();

        FloatRoundedRect border = RoundedBorderGeometry.PixelSnappedRoundedBorder(style, rect, info.SidesToInclude);

        if (info.IsBorderFill && bleedAvoidance == BackgroundBleedAvoidance.ShrinkBackground && !info.IsClippedWithLocalScrolling)
        {
            border = BackgroundRoundedRectAdjustedForBleedAvoidance(border);
        }

        SKRect borderRect = border.Rect;
        if (bgLayer.Clip == FillBox.FillBox || bgLayer.Clip == FillBox.Content)
        {
            border = RoundedBorderGeometry.PixelSnappedRoundedBorderWithOutsets(style, borderRect, borderPaddingInsets, info.SidesToInclude);
        }
        else if (bgLayer.Clip == FillBox.Padding || info.IsClippedWithLocalScrolling)
        {
            border = RoundedBorderGeometry.PixelSnappedRoundedInnerBorder(style, borderRect, info.SidesToInclude);
        }
        return border;
    }

    /// <summary>Inset the rounded rect for bleed avoidance: 1/2 border width, or 1/6 for double borders.</summary>
    private FloatRoundedRect BackgroundRoundedRectAdjustedForBleedAvoidance(FloatRoundedRect backgroundRoundedRect)
    {
        float fractionalInset = 1.0f / 2;
        // (double-border detection omitted for single-style borders)
        float inset = fractionalInset;
        backgroundRoundedRect.Inset(inset, inset);
        backgroundRoundedRect.ConstrainRadii();
        return backgroundRoundedRect;
    }

    private SKRect AdjustRectForScrolledContent(PhysicalBoxStrut snappedBorders, SKRect rect)
    {
        // background-attachment: local — paint in content space; borders clip.
        return new SKRect(rect.Left + snappedBorders.Left, rect.Top + snappedBorders.Top, rect.Right - snappedBorders.Right, rect.Bottom - snappedBorders.Bottom);
    }

    private SKBlendMode CompositeToSkia(FillLayer layer)
    {
        switch (layer.BlendMode)
        {
            case FillBlendMode.Multiply: return SKBlendMode.Multiply;
            case FillBlendMode.Screen: return SKBlendMode.Screen;
            case FillBlendMode.Overlay: return SKBlendMode.Overlay;
            case FillBlendMode.Darken: return SKBlendMode.Darken;
            case FillBlendMode.Lighten: return SKBlendMode.Lighten;
            case FillBlendMode.ColorDodge: return SKBlendMode.ColorDodge;
            case FillBlendMode.ColorBurn: return SKBlendMode.ColorBurn;
            case FillBlendMode.HardLight: return SKBlendMode.HardLight;
            case FillBlendMode.SoftLight: return SKBlendMode.SoftLight;
            case FillBlendMode.Difference: return SKBlendMode.Difference;
            case FillBlendMode.Exclusion: return SKBlendMode.Exclusion;
            case FillBlendMode.Hue: return SKBlendMode.Hue;
            case FillBlendMode.Saturation: return SKBlendMode.Saturation;
            case FillBlendMode.Color: return SKBlendMode.Color;
            case FillBlendMode.Luminosity: return SKBlendMode.Luminosity;
            default: return SKBlendMode.SrcOver;
        }
    }

    /// <summary>Clip out a rect from a rounded shape using the difference operation.</summary>
    private static SKPath ClipOutOf(SKPath basePath, SKRect toClipOut, bool rounded)
    {
        var result = new SKPath();
        result.AddPath(basePath);
        var inner = new SKPath();
        if (rounded)
        {
            var rrect = new SKRoundRect();
            rrect.SetRectRadii(toClipOut,
                new[] { new SKPoint(1, 1), new SKPoint(1, 1), new SKPoint(1, 1), new SKPoint(1, 1) });
            inner.AddRoundRect(rrect);
        }
        else
        {
            inner.AddRect(toClipOut);
        }
        var outPath = new SKPath();
        if (result.Op(inner, SKPathOp.Difference, outPath))
        {
            result.Dispose();
            return outPath;
        }
        inner.Dispose();
        return result;
    }
}

/// <summary>Computed per-layer paint info.</summary>
public sealed class FillLayerInfo
{
    public string? Image;
    public SKColor Color;
    public bool IsBottomLayer;
    public bool IsBorderFill;
    public bool IsRoundedFill;
    public bool IsClippedWithLocalScrolling;
    public bool IsPrinting;
    public bool ShouldPaintColor;
    public bool ShouldPaintImage;
    public bool ShouldApplyBlendOperation;
    public bool IsMaskLayer;
    public PhysicalBoxSides SidesToInclude;
    public float BorderTopLeftRadius;
    public float BorderTopRightRadius;
    public float BorderBottomLeftRadius;
    public float BorderBottomRightRadius;

    public FillLayerInfo(FillLayer layer, SKColor bgColor, bool isBorderFill, bool isBottomLayer)
    {
        Image = layer.Image;
        Color = layer.Color ?? bgColor;
        IsBorderFill = isBorderFill;
        IsBottomLayer = isBottomLayer;
        IsClippedWithLocalScrolling = layer.Attachment == FillAttachment.Local;
        SidesToInclude = PhysicalBoxSides.All;
        ShouldPaintImage = Image != null && !string.IsNullOrEmpty(Image);
        ShouldPaintColor = isBottomLayer && Color.Alpha > 0 && (!ShouldPaintImage || !IsBottomLayer || layer.Color != null);
        if (isBottomLayer && ShouldPaintImage && layer.Color == null)
            ShouldPaintColor = Color.Alpha > 0 && !IsOpaqueImage;
        ShouldApplyBlendOperation = !isBottomLayer || !layer.IsMaskLayer;
        IsMaskLayer = layer.IsMaskLayer;
    }

    private bool IsOpaqueImage => Image != null && Image.Contains("gradient", StringComparison.OrdinalIgnoreCase) == false;
}

/// <summary>Border-radius geometry helpers.</summary>
public static class RoundedBorderGeometry
{
    public static FloatRoundedRect PixelSnappedRoundedBorder(ComputedStyle style, SKRect rect, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        return new FloatRoundedRect
        {
            Rect = rect,
            TopLeftRadius = (sidesToInclude & PhysicalBoxSides.Left) != 0 && (sidesToInclude & PhysicalBoxSides.Top) != 0 ? style.BorderTopLeftRadius : 0,
            TopRightRadius = (sidesToInclude & PhysicalBoxSides.Right) != 0 && (sidesToInclude & PhysicalBoxSides.Top) != 0 ? style.BorderTopRightRadius : 0,
            BottomRightRadius = (sidesToInclude & PhysicalBoxSides.Right) != 0 && (sidesToInclude & PhysicalBoxSides.Bottom) != 0 ? style.BorderBottomRightRadius : 0,
            BottomLeftRadius = (sidesToInclude & PhysicalBoxSides.Left) != 0 && (sidesToInclude & PhysicalBoxSides.Bottom) != 0 ? style.BorderBottomLeftRadius : 0
        };
    }

    public static FloatRoundedRect PixelSnappedRoundedInnerBorder(ComputedStyle style, SKRect rect, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        float insetL = style.BorderLeftWidth, insetT = style.BorderTopWidth, insetR = style.BorderRightWidth, insetB = style.BorderBottomWidth;
        return PixelSnappedRoundedBorderWithOutsets(style, rect, new PhysicalBoxStrut(insetT, insetR, insetB, insetL), sidesToInclude);
    }

    public static FloatRoundedRect PixelSnappedRoundedBorderWithOutsets(ComputedStyle style, SKRect rect, PhysicalBoxStrut outsets, PhysicalBoxSides sidesToInclude = PhysicalBoxSides.All)
    {
        var border = PixelSnappedRoundedBorder(style, rect, sidesToInclude);
        border.Inset(outsets.Left, outsets.Top);
        border.Rect = new SKRect(rect.Left + outsets.Left, rect.Top + outsets.Top, rect.Right - outsets.Right, rect.Bottom - outsets.Bottom);
        float scaleX = border.Rect.Width <= 0 ? 0 : (border.Rect.Width + 2 * outsets.Left) <= 0 ? 0 : border.Rect.Width / (border.Rect.Width + 2 * outsets.Left);
        float scaleY = border.Rect.Height <= 0 ? 0 : (border.Rect.Height + 2 * outsets.Top) <= 0 ? 0 : border.Rect.Height / (border.Rect.Height + 2 * outsets.Top);
        border.TopLeftRadius = ReduceRadius(style.BorderTopLeftRadius, outsets.Left, outsets.Top, scaleX, scaleY);
        border.TopRightRadius = ReduceRadius(style.BorderTopRightRadius, outsets.Right, outsets.Top, scaleX, scaleY);
        border.BottomRightRadius = ReduceRadius(style.BorderBottomRightRadius, outsets.Right, outsets.Bottom, scaleX, scaleY);
        border.BottomLeftRadius = ReduceRadius(style.BorderBottomLeftRadius, outsets.Left, outsets.Bottom, scaleX, scaleY);
        return border;
    }

    private static float ReduceRadius(float radius, float insetX, float insetY, float scaleX, float scaleY)
    {
        if (radius <= 0) return 0;
        float r = radius;
        if (insetX > 0)
        {
            r = Math.Max(0, r - insetX);
            r = r * scaleX;
        }
        if (insetY > 0)
        {
            r = Math.Max(0, r - insetY);
            r = r * scaleY;
        }
        return r;
    }
}
