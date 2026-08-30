using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

/// <summary>
/// Implementation of the decoration-line painter and the geometry portions of
/// the text-decoration info / offset helpers. Computes text-decoration line
/// geometry (thickness, underline/overline/line-through offsets, double
/// offsets, wavy bezier pattern) and paints the decoration lines into a canvas.
/// </summary>
public static class TextDecorationPainter
{
    /// <summary>
    /// Corresponds to ComputeDecorationThickness(): auto thickness is
    /// font_size / 10, floored at the minimum thickness (1 CSS px).
    /// </summary>
    public static float ComputeDecorationThickness(float computedFontSize, float minimumThickness = 1f)
    {
        float autoThickness = MathF.Max(minimumThickness, computedFontSize / 10f);
        return autoThickness;
    }

    /// <summary>WavyControlPointDistance(): distance from the wave axis to the bezier control points.</summary>
    public static float WavyControlPointDistance(float resolvedThickness)
    {
        return 0.5f + MathF.Round(3 * MathF.Max(1f, resolvedThickness) + 0.5f);
    }

    /// <summary>WavyStep(): horizontal step between consecutive wave crests.</summary>
    public static float WavyStep(float resolvedThickness)
    {
        return 0.5f + MathF.Round(2 * MathF.Max(1f, resolvedThickness) + 0.5f);
    }

    /// <summary>
    /// PrepareWavyStrokePath(): three consecutive cubic beziers forming a wavy
    /// pattern whose midpoints sit at y = 0.5 (to reduce vertical aliasing).
    /// The pattern starts at phase_shift (negative) so it can be clipped to the
    /// line length on both ends.
    /// </summary>
    public static SKPath PrepareWavyStrokePath(float resolvedThickness)
    {
        float controlPointDistance = WavyControlPointDistance(resolvedThickness);
        float step = WavyStep(resolvedThickness);
        float phaseShift = -2f * step;

        var path = new SKPath();
        float startX = phaseShift;
        const float midY = 0.5f;
        path.MoveTo(startX, midY);

        float endX = startX + 2f * step;
        var cp1 = new SKPoint(startX + step, midY + controlPointDistance);
        var cp2 = new SKPoint(startX + step, midY - controlPointDistance);
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        cp1.X += 2f * step;
        cp2.X += 2f * step;
        endX += 2f * step;
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        cp1.X += 2f * step;
        cp2.X += 2f * step;
        endX += 2f * step;
        path.CubicTo(cp1, cp2, new SKPoint(endX, midY));

        return path;
    }

    /// <summary>
    /// Paints all text-decoration lines in one call (under/over first, then
    /// line-through), without text-shadow phases. Kept for compatibility;
    /// <see cref="PaintUnderOrOverLines"/> and <see cref="PaintLineThrough"/>
    /// are the phase-aware entry points used by DrawTextOp.
    /// </summary>
    public static void PaintDecorationLines(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool underline, bool overline, bool lineThrough,
        TextDecorationStyleType style, SKColor color, SKColor underlineColor)
    {
        if (width <= 0)
            return;

        float thickness = ComputeDecorationThickness(fontSize);
        PaintUnderOrOverLines(canvas, startX, width, baselineY, ascent, fontSize,
            underline, overline, style, color, underlineColor, null);
        PaintLineThrough(canvas, startX, width, baselineY, ascent, fontSize,
            lineThrough, style, color, null);
    }

    /// <summary>
    /// Paints underline/overline decorations, mirroring the engine's
    /// paint-under/over-line-decorations path: a shadow pass
    /// first (each text shadow rendered as the decoration in the shadow color,
    /// offset and blurred), then the line in its actual color. These lines are
    /// painted before the text so glyphs sit on top of them.
    /// </summary>
    public static void PaintUnderOrOverLines(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool underline, bool overline,
        TextDecorationStyleType style, SKColor color, SKColor underlineColor,
        List<TextShadowValue>? shadows,
        Func<float, float, List<SKRect>?>? skipInkProvider = null)
    {
        if (width <= 0)
            return;

        float thickness = ComputeDecorationThickness(fontSize);
        var lineColor = underlineColor.Alpha > 0 ? underlineColor : color;

        // text-decoration-skip-ink: auto — punch holes in the decoration line
        // where the glyph ink crosses it. Mirrors the engine's
        // PaintDecorationLine / ClipDecorationsStripe: the stripe
        // band is the decoration bounds inset by 0.5 (to ignore intersects
        // smaller than half a pixel); the provider returns the clip rects for a
        // given band (upper = band top relative to the baseline, stripe = band
        // height) in canvas coordinates.
        float dilation = MathF.Min(thickness, 13f);
        List<SKRect>? underlineClips = null;
        List<SKRect>? overlineClips = null;
        if (skipInkProvider != null)
        {
            if (underline)
            {
                int gap = Math.Max(1, (int)MathF.Ceiling(thickness / 2f));
                float lineY = baselineY + gap;
                underlineClips = skipInkProvider(lineY - baselineY + 0.5f, StripeHeight(style, thickness));
            }
            if (overline)
            {
                float lineY = baselineY - ascent - thickness;
                overlineClips = skipInkProvider(lineY - baselineY + 0.5f, StripeHeight(style, thickness));
            }
        }

        PaintWithShadowPhases(canvas, shadows, lineColor, (dx, dy, shadowColor) =>
        {
            // Underline: gap below the baseline grows with thickness.
            // (ComputeUnderlineOffsetAuto with is_fixed=false.)
            if (underline)
            {
                int gap = Math.Max(1, (int)MathF.Ceiling(thickness / 2f));
                float lineY = baselineY + gap;
                DrawLineWithSkipInk(canvas, underlineClips, dx, dy, () =>
                {
                    PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
                    if (style == TextDecorationStyleType.Double)
                        PaintSingleLine(canvas, startX + dx, width, lineY + thickness + 1f + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
                });
            }

            // Overline: sits just above the ascent line (TextTop position).
            if (overline)
            {
                float lineY = baselineY - ascent - thickness;
                DrawLineWithSkipInk(canvas, overlineClips, dx, dy, () =>
                {
                    PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
                    if (style == TextDecorationStyleType.Double)
                        PaintSingleLine(canvas, startX + dx, width, lineY - (thickness + 1f) + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
                });
            }
        });
    }

    /// <summary>
    /// Height of the decoration stripe used for ink skipping, after the 0.5px
    /// inset on each side. Mirrors the engine's decoration-info Bounds() for
    /// each decoration style (double spans both stripes: DoubleOffset +
    /// thickness), with the height reduced by the inset.
    /// </summary>
    private static float StripeHeight(TextDecorationStyleType style, float thickness)
    {
        if (style == TextDecorationStyleType.Double)
            return 2 * thickness + 1 - 1f;
        return thickness - 1f;
    }

    /// <summary>
    /// Draws <paramref name="draw"/> with the skip-ink clip rects punched out
    /// (SKClipOperation.Difference), then restores. The rects are shifted by
    /// <paramref name="dx"/>/<paramref name="dy"/> so the holes follow the line
    /// for each text-shadow phase (mirroring the engine, where the clip is
    /// re-applied per phase). When there are no clips the line is drawn as-is.
    /// </summary>
    private static void DrawLineWithSkipInk(SKCanvas canvas, List<SKRect>? clips, float dx, float dy, Action draw)
    {
        if (clips == null || clips.Count == 0)
        {
            draw();
            return;
        }

        canvas.Save();
        foreach (var clip in clips)
            canvas.ClipRect(new SKRect(clip.Left + dx, clip.Top + dy, clip.Right + dx, clip.Bottom + dy), SKClipOperation.Difference);
        draw();
        canvas.Restore();
    }

    /// <summary>
    /// Paints line-through decorations, mirroring the engine's
    /// paint-line-through path: shadow pass first,
    /// then the line in its actual color. Painted after the text so it sits on
    /// top of the glyphs. No skip: ink for line-through.
    /// </summary>
    public static void PaintLineThrough(
        SKCanvas canvas, float startX, float width, float baselineY, float ascent, float fontSize,
        bool lineThrough,
        TextDecorationStyleType style, SKColor color, List<TextShadowValue>? shadows)
    {
        if (width <= 0)
            return;

        float thickness = ComputeDecorationThickness(fontSize);

        PaintWithShadowPhases(canvas, shadows, color, (dx, dy, shadowColor) =>
        {
            if (!lineThrough)
                return;
            // Line-through: centered at 2/3 of the ascent (SetLineThroughLineData).
            float lineY = baselineY - ascent / 3f - thickness / 2f;
            PaintSingleLine(canvas, startX + dx, width, lineY + dy, thickness, style, shadowColor);
            if (style == TextDecorationStyleType.Double)
                PaintSingleLine(canvas, startX + dx, width, lineY + MathF.Floor(thickness + 1f) + dy, thickness, TextDecorationStyleType.Solid, shadowColor);
        });
    }

    /// <summary>
    /// Renders the shadow pass (one draw per text shadow, offset + blur in the
    /// shadow color) followed by the fill pass in <paramref name="fillColor"/>.
    /// Mirrors TextPainter::PaintWithTextShadow / text_shadow_painter.cc.
    /// </summary>
    private static void PaintWithShadowPhases(SKCanvas canvas, List<TextShadowValue>? shadows, SKColor fillColor,
        Action<float, float, SKColor> draw)
    {
        if (shadows != null)
        {
            foreach (var shadow in shadows)
            {
                if (shadow.BlurRadius > 0)
                {
                    using var layerPaint = new SKPaint
                    {
                        ImageFilter = SKImageFilter.CreateBlur(shadow.BlurRadius, shadow.BlurRadius)
                    };
                    canvas.SaveLayer(layerPaint);
                    draw(shadow.OffsetX, shadow.OffsetY, shadow.Color);
                    canvas.Restore();
                }
                else
                {
                    draw(shadow.OffsetX, shadow.OffsetY, shadow.Color);
                }
            }
        }
        draw(0, 0, fillColor);
    }

    private static void PaintSingleLine(SKCanvas canvas, float startX, float width, float lineY, float thickness,
        TextDecorationStyleType style, SKColor color)
    {
        switch (style)
        {
            case TextDecorationStyleType.Wavy:
                PaintWavy(canvas, startX, width, lineY, thickness, color);
                break;
            case TextDecorationStyleType.Dotted:
            case TextDecorationStyleType.Dashed:
                PaintDottedOrDashed(canvas, startX, width, lineY, thickness, color, style);
                break;
            default: // Solid / Double (the second stripe is painted by the caller)
            {
                // SnapYAxis(): round to the nearest pixel and never below 1px thick.
                float snappedY = MathF.Floor(lineY + 0.5f);
                float snappedHeight = MathF.Max(MathF.Floor(thickness), 1f);
                using var paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = false
                };
                canvas.DrawRect(new SKRect(startX, snappedY, startX + width, snappedY + snappedHeight), paint);
                break;
            }
        }
    }

    private static void PaintDottedOrDashed(SKCanvas canvas, float startX, float width, float lineY, float thickness,
        SKColor color, TextDecorationStyleType style)
    {
        // GetSnappedPointsForTextLine(): mid-point snapped to a device pixel.
        int midY = (int)MathF.Floor(lineY + MathF.Max(thickness / 2f, 0.5f));
        float strokeWidth = MathF.Max(MathF.Floor(thickness), 1f);
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true,
            StrokeCap = style == TextDecorationStyleType.Dotted ? SKStrokeCap.Round : SKStrokeCap.Butt
        };
        float dash = MathF.Max(1f, thickness);
        float gap = style == TextDecorationStyleType.Dotted ? dash * 2f : dash * 2f;
        paint.PathEffect = SKPathEffect.CreateDash(new[] { dash, gap }, 0);
        canvas.DrawLine(startX, midY, startX + width, midY, paint);
    }

    private static void PaintWavy(SKCanvas canvas, float startX, float width, float lineY, float thickness, SKColor color)
    {
        float step = WavyStep(thickness);
        float controlPointDistance = WavyControlPointDistance(thickness);

        // The wavy path midpoints sit at y=0.5; its stroked bounds span
        // 0.5 +/- (control_point_distance + thickness/2). The engine floors the
        // top and paints the tile so nothing lands at y<0, centering the wave a
        // little below the decoration line.
        float strokeTop = 0.5f - controlPointDistance - thickness / 2f;
        float patternTop = MathF.Floor(strokeTop);
        float ty = lineY - patternTop;

        using var pattern = PrepareWavyStrokePath(thickness);

        // Tile the three-bezier pattern (span 6*step) across the line, starting
        // one wave before startX so clipping produces identical phase at both ends.
        float left = startX - 2f * step;
        float right = startX + width + 2f * step;
        using var tiled = new SKPath();
        for (float x = left; x < right; x += 2f * step)
        {
            using var copy = new SKPath(pattern);
            copy.Transform(SKMatrix.CreateTranslation(x + 2f * step, 0));
            tiled.AddPath(copy);
        }

        canvas.Save();
        canvas.Translate(0, ty);
        canvas.ClipRect(new SKRect(startX, lineY - 40, startX + width, lineY + 40));
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = true
        };
        canvas.DrawPath(tiled, paint);
        canvas.Restore();
    }
}
