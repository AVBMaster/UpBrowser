using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

/// <summary>
/// Transliteration of the outline painting algorithms: right-angle outline
/// paths, shrink/round-corner transforms and per-edge strokes for the various
/// outline styles.
/// </summary>
public sealed class OutlinePainter
{
    private readonly DisplayList _displayList;

    public OutlinePainter(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>One segment of a right-angle outline path.</summary>
    public struct Line
    {
        public SKPoint Start;
        public SKPoint End;
    }

    /// <summary>Corner radii (top-left, top-right, bottom-right, bottom-left).</summary>
    public struct Radii
    {
        public SKSize TopLeft;
        public SKSize TopRight;
        public SKSize BottomRight;
        public SKSize BottomLeft;

        public bool IsEmpty => TopLeft.IsEmpty && TopRight.IsEmpty && BottomRight.IsEmpty && BottomLeft.IsEmpty;

        public Radii(float uniform)
        {
            TopLeft = new SKSize(uniform, uniform);
            TopRight = new SKSize(uniform, uniform);
            BottomRight = new SKSize(uniform, uniform);
            BottomLeft = new SKSize(uniform, uniform);
        }
    }

    private const float kCornerConicWeight = 0.707106781187f; // 1/sqrt(2)

    private static SKPoint[] AdjustedOutlineOffset(SKRect rect, int offset)
    {
        int top = Math.Max(offset, -(int)(rect.Height / 2));
        int bottom = top;
        int left = Math.Max(offset, -(int)(rect.Width / 2));
        int right = left;
        return new[] { new SKPoint(left, top), new SKPoint(right, bottom) };
    }

    /// <summary>
    /// Construct a clockwise path along the outer edge of the region covered by
    /// <paramref name="rects"/> expanded by outline offset and additional outset.
    /// </summary>
    private static bool ComputeRightAnglePath(SKPath path, List<SKRect> rects, int outlineOffset, int additionalOutset)
    {
        var region = new SKRegion();
        foreach (var r in rects)
        {
            var rect = r;
            var offs = AdjustedOutlineOffset(rect, outlineOffset);
            rect = new SKRect(
                rect.Left + offs[0].X - additionalOutset,
                rect.Top + offs[0].Y - additionalOutset,
                rect.Right + offs[1].X + additionalOutset,
                rect.Bottom + offs[1].Y + additionalOutset);
            var irect = new SKRectI((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
            region.Op(irect, SKRegionOperation.Union);
        }
        var boundary = region.GetBoundaryPath();
        if (boundary == null)
            return false;
        path.AddPath(boundary);
        boundary.Dispose();
        return true;
    }

    /// <summary>Merge line2 into line1 if they are in the same straight line.</summary>
    private static bool MergeLineIfPossible(ref Line line1, in Line line2)
    {
        if ((line1.Start.X == line1.End.X && line1.Start.X == line2.End.X) ||
            (line1.Start.Y == line1.End.Y && line1.Start.Y == line2.End.Y))
        {
            line1.End = line2.End;
            return true;
        }
        return false;
    }

    /// <summary>Iterate a right-angle path, running contourAction on each closed contour.</summary>
    private static void IterateRightAnglePath(SKPath path, Action<List<Line>> contourAction)
    {
        var iter = path.CreateIterator(true);
        var lines = new List<Line>();
        var points = new SKPoint[4];
        bool done = false;
        while (!done)
        {
            var verb = iter.Next(points);
            switch (verb)
            {
                case SKPathVerb.Move:
                    lines.Clear();
                    break;
                case SKPathVerb.Line:
                {
                    var newLine = new Line { Start = points[0], End = points[1] };
                    if (lines.Count == 0)
                    {
                        lines.Add(newLine);
                    }
                    else
                    {
                        var last = lines[lines.Count - 1];
                        if (MergeLineIfPossible(ref last, newLine))
                            lines[lines.Count - 1] = last;
                        else
                            lines.Add(newLine);
                    }
                    break;
                }
                case SKPathVerb.Close:
                {
                    if (lines.Count >= 4)
                    {
                        var last = lines[lines.Count - 1];
                        if (MergeLineIfPossible(ref last, lines[0]))
                        {
                            lines[0] = last;
                            lines.RemoveAt(lines.Count - 1);
                        }
                        if (lines.Count >= 4)
                            contourAction(lines);
                    }
                    lines.Clear();
                    break;
                }
                case SKPathVerb.Done:
                    done = true;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Given 3 points defining a right angle corner, returns p2 shifted to shrink by inset.</summary>
    private static SKPoint ShrinkCorner(SKPoint p1, SKPoint p2, SKPoint p3, int inset)
    {
        if (p1.X == p2.X)
        {
            if (p1.Y < p2.Y)
            {
                return p2.X < p3.X ? new SKPoint(p2.X - inset, p2.Y + inset) : new SKPoint(p2.X - inset, p2.Y - inset);
            }
            return p2.X < p3.X ? new SKPoint(p2.X + inset, p2.Y + inset) : new SKPoint(p2.X + inset, p2.Y - inset);
        }
        if (p1.X < p3.X)
        {
            return p2.Y < p3.Y ? new SKPoint(p2.X - inset, p2.Y + inset) : new SKPoint(p2.X + inset, p2.Y + inset);
        }
        return p2.Y < p3.Y ? new SKPoint(p2.X - inset, p2.Y - inset) : new SKPoint(p2.X + inset, p2.Y - inset);
    }

    /// <summary>Shrink a right-angle path by inset on all sides.</summary>
    private static void ShrinkRightAnglePath(SKPath path, int inset)
    {
        var input = new SKPath(path);
        path.Reset();
        IterateRightAnglePath(input, lines =>
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var prevPoint = lines[i == 0 ? lines.Count - 1 : i - 1].Start;
                var newPoint = ShrinkCorner(prevPoint, lines[i].Start, lines[i].End, inset);
                if (i == 0)
                    path.MoveTo(newPoint);
                else
                    path.LineTo(newPoint);
            }
            path.Close();
        });
        input.Dispose();
    }

    /// <summary>Get the corner radii corresponding to a corner defined by 3 points.</summary>
    private static SKSize GetRadiiCorner(in Radii convexRadii, in Radii concaveRadii, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        if (p1.X == p2.X)
        {
            if (p1.Y == p2.Y || p2.X == p3.X)
                return SKSize.Empty;
            if (p1.Y < p2.Y)
            {
                return p2.X < p3.X ? concaveRadii.BottomLeft : convexRadii.BottomRight;
            }
            return p2.X < p3.X ? convexRadii.TopLeft : concaveRadii.TopRight;
        }
        if (p2.X != p3.X || p2.Y == p3.Y)
            return SKSize.Empty;
        if (p1.X < p2.X)
        {
            return p2.Y < p3.Y ? convexRadii.TopRight : concaveRadii.BottomRight;
        }
        return p2.Y < p3.Y ? concaveRadii.TopLeft : convexRadii.BottomLeft;
    }

    /// <summary>Shorten a line between rounded corners.</summary>
    private static void AdjustLineBetweenCorners(ref Line line, in Radii convexRadii, in Radii concaveRadii, SKPoint prevPoint, SKPoint nextPoint)
    {
        var corner1 = GetRadiiCorner(convexRadii, concaveRadii, prevPoint, line.Start, line.End);
        var corner2 = GetRadiiCorner(convexRadii, concaveRadii, line.Start, line.End, nextPoint);
        if (line.Start.X == line.End.X)
        {
            // vertical line, adjacent lines horizontal
            float height = Math.Abs(line.End.Y - line.Start.Y);
            float corner1Height = corner1.Height;
            float corner2Height = corner2.Height;
            if (corner1Height + corner2Height > height)
            {
                float scale = height / (corner1Height + corner2Height);
                corner1Height = (float)Math.Floor(corner1Height * scale);
                corner2Height = (float)Math.Floor(corner2Height * scale);
            }
            if (line.Start.Y < line.End.Y)
            {
                line.Start = new SKPoint(line.Start.X, line.Start.Y + corner1Height);
                line.End = new SKPoint(line.End.X, line.End.Y - corner2Height);
            }
            else
            {
                line.Start = new SKPoint(line.Start.X, line.Start.Y - corner1Height);
                line.End = new SKPoint(line.End.X, line.End.Y + corner2Height);
            }
        }
        else
        {
            // horizontal line, adjacent lines vertical
            float width = Math.Abs(line.End.X - line.Start.X);
            float corner1Width = corner1.Width;
            float corner2Width = corner2.Width;
            if (corner1Width + corner2Width > width)
            {
                float scale = width / (corner1Width + corner2Width);
                corner1Width = (float)Math.Floor(corner1Width * scale);
                corner2Width = (float)Math.Floor(corner2Width * scale);
            }
            if (line.Start.X < line.End.X)
            {
                line.Start = new SKPoint(line.Start.X + corner1Width, line.Start.Y);
                line.End = new SKPoint(line.End.X - corner2Width, line.End.Y);
            }
            else
            {
                line.Start = new SKPoint(line.Start.X - corner1Width, line.Start.Y);
                line.End = new SKPoint(line.End.X + corner2Width, line.End.Y);
            }
        }
    }

    /// <summary>Create a rounded path from a right-angle path by inserting arcs for corners.</summary>
    private static void AddCornerRadiiToPath(SKPath path, in Radii convexRadii, in Radii concaveRadii)
    {
        var input = new SKPath(path);
        path.Reset();
        var convex = convexRadii;
        var concave = concaveRadii;
        IterateRightAnglePath(input, lines =>
        {
            var newLines = new List<Line>(lines);
            for (int i = 0; i < lines.Count; i++)
            {
                var prevPoint = lines[i == 0 ? lines.Count - 1 : i - 1].Start;
                var nextPoint = lines[i == lines.Count - 1 ? 0 : i + 1].End;
                var tmp = newLines[i];
                AdjustLineBetweenCorners(ref tmp, convex, concave, prevPoint, nextPoint);
                newLines[i] = tmp;
            }
            path.MoveTo(newLines[newLines.Count - 1].End);
            for (int i = 0; i < newLines.Count; i++)
            {
                path.ConicTo(lines[i].Start, newLines[i].Start, kCornerConicWeight);
                path.LineTo(newLines[i].End);
            }
            path.Close();
        });
        input.Dispose();
    }

    private static void ExtendLineAtEndpoint(ref SKPoint point, SKPoint other, int offset)
    {
        if (point.X == other.X)
        {
            point = new SKPoint(point.X, point.Y + (point.Y < other.Y ? -offset : offset));
        }
        else
        {
            point = new SKPoint(point.X + (point.X < other.X ? -offset : offset), point.Y);
        }
    }

    private static int MiterSlope(SKPoint p1, SKPoint p2, SKPoint p3)
    {
        if (p1.X == p2.X)
            return (p3.X > p2.X) == (p2.Y > p1.Y) ? 1 : -1;
        return (p3.Y > p2.Y) == (p2.X > p1.X) ? 1 : -1;
    }

    private static SKPath MiterClipPath(SKRect bounds, SKPoint prevPoint, in Line line, SKPoint nextPoint)
    {
        int startMiterSlope = MiterSlope(prevPoint, line.Start, line.End);
        int endMiterSlope = MiterSlope(line.Start, line.End, nextPoint);
        var p1 = new SKPoint(line.Start.X + startMiterSlope * (line.Start.Y - bounds.Top), bounds.Top);
        var p2 = new SKPoint(line.End.X + endMiterSlope * (line.End.Y - bounds.Top), bounds.Top);
        var p3 = new SKPoint(line.End.X - endMiterSlope * (bounds.Bottom - line.End.Y), bounds.Bottom);
        var p4 = new SKPoint(line.Start.X - startMiterSlope * (bounds.Bottom - line.Start.Y), bounds.Bottom);

        var path = new SKPath();
        path.MoveTo(p1);
        path.LineTo(p2);
        path.LineTo(p3);
        path.LineTo(p4);
        path.Close();
        if (startMiterSlope != endMiterSlope && line.Start.X == line.End.X)
            path.FillType = SKPathFillType.InverseWinding;
        return path;
    }

    private static void PaintStraightEdge(DisplayList displayList, in Line line, SKColor color, float width)
    {
        var adjusted = line;
        if (adjusted.Start.X > adjusted.End.X || adjusted.Start.Y > adjusted.End.Y)
        {
            (adjusted.Start, adjusted.End) = (adjusted.End, adjusted.Start);
        }
        int jointOffset = (int)((width + 1) / 2);
        ExtendLineAtEndpoint(ref adjusted.Start, adjusted.End, jointOffset);
        ExtendLineAtEndpoint(ref adjusted.End, adjusted.Start, jointOffset);

        var op = PaintOpPool.GetDrawLineOp();
        op.X1 = adjusted.Start.X;
        op.Y1 = adjusted.Start.Y;
        op.X2 = adjusted.End.X;
        op.Y2 = adjusted.End.Y;
        op.Color = color;
        op.StrokeWidth = width;
        op.Bounds = new SKRect(
            Math.Min(adjusted.Start.X, adjusted.End.X) - width,
            Math.Min(adjusted.Start.Y, adjusted.End.Y) - width,
            Math.Max(adjusted.Start.X, adjusted.End.X) + width,
            Math.Max(adjusted.Start.Y, adjusted.End.Y) + width);
        displayList.Add(op);
    }

    /// <summary>Paint a solid outline by stroking the outline rect ring.</summary>
    public void PaintOutline(SKRect borderRect, ComputedStyle style, float outlineOffset, bool isFocusRing = false)
    {
        float width = style.OutlineWidth;
        if (width <= 0)
            return;

        var outlineRect = new SKRect(
            borderRect.Left - outlineOffset - width,
            borderRect.Top - outlineOffset - width,
            borderRect.Right + outlineOffset + width,
            borderRect.Bottom + outlineOffset + width);

        float maxRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
        float radius = maxRadius > 0 ? maxRadius + outlineOffset : 0;

        var path = new SKPath();
        if (radius > 0)
        {
            var rrect = new SKRoundRect();
            rrect.SetRectRadii(outlineRect,
                new[] {
                    new SKPoint(radius, radius), new SKPoint(radius, radius),
                    new SKPoint(radius, radius), new SKPoint(radius, radius)
                });
            path.AddRoundRect(rrect);
        }
        else
        {
            path.AddRect(outlineRect);
        }

        if (isFocusRing)
        {
            var ringOp = PaintOpPool.GetDrawPathOp();
            ringOp.Path.Dispose();
            ringOp.Path = path;
            ringOp.StrokePaint = new SKPaint { Color = style.OutlineColor, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
            ringOp.FillPaint = null;
            ringOp.Bounds = outlineRect;
            _displayList.Add(ringOp);
            return;
        }

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = outlineRect;
        op.BorderTopWidth = op.BorderRightWidth = op.BorderBottomWidth = op.BorderLeftWidth = width;
        op.BorderTopColor = op.BorderRightColor = op.BorderBottomColor = op.BorderLeftColor = style.OutlineColor;
        op.BorderRadius = radius;
        op.Bounds = outlineRect;
        _displayList.Add(op);
    }

    /// <summary>Paint a complex outline over multiple rects using the full ring algorithm.</summary>
    public void PaintOutlineRects(List<SKRect> outlineRects, ComputedStyle style, int offset, int width)
    {
        if (outlineRects.Count == 0)
            return;
        SKColor color = style.OutlineColor;

        var pixelRects = new List<SKRect>();
        foreach (var r in outlineRects)
        {
            pixelRects.Add(new SKRect((float)Math.Round(r.Left), (float)Math.Round(r.Top), (float)Math.Round(r.Right), (float)Math.Round(r.Bottom)));
        }

        // Compute the outer right-angle path (union of offset+width rects).
        var outerPath = new SKPath();
        if (!ComputeRightAnglePath(outerPath, pixelRects, offset, width))
            return;

        var innerPath = new SKPath(outerPath);
        ShrinkRightAnglePath(innerPath, width);

        // Push clip to outer path, clip out inner path (winding fill difference).
        var clipOp = PaintOpPool.GetPushClipOp();
        clipOp.ClipPath = outerPath;
        clipOp.AntiAlias = true;
        clipOp.Bounds = outerPath.Bounds;
        _displayList.Add(clipOp);

        var clipOutOp = PaintOpPool.GetPushClipOp();
        clipOutOp.ClipPath = MakeClipOutPath(outerPath, innerPath);
        clipOutOp.AntiAlias = true;
        clipOutOp.Bounds = outerPath.Bounds;
        _displayList.Add(clipOutOp);

        // Fill the ring area.
        var fillOp = PaintOpPool.GetDrawRectOp();
        fillOp.Rect = outerPath.Bounds;
        fillOp.FillColor = color;
        fillOp.Bounds = outerPath.Bounds;
        _displayList.Add(fillOp);

        var popClipOp = PaintOpPool.GetPopClipOp();
        popClipOp.Bounds = outerPath.Bounds;
        _displayList.Add(popClipOp);
        var popClipOp2 = PaintOpPool.GetPopClipOp();
        popClipOp2.Bounds = outerPath.Bounds;
        _displayList.Add(popClipOp2);

        innerPath.Dispose();
        outerPath.Dispose();
    }

    /// <summary>
    /// Add a counter-clockwise rect around the outer bounds so a winding clip of
    /// the inner path removes the ring area but keeps the area outside the path.
    /// </summary>
    private static SKPath MakeClipOutPath(SKPath outerPath, SKPath innerPath)
    {
        var result = new SKPath();
        result.FillType = SKPathFillType.Winding;
        var bounds = outerPath.Bounds;
        var outer = new SKPath();
        outer.AddRect(bounds, SKPathDirection.CounterClockwise);
        result.AddPath(outer);
        result.AddPath(innerPath);
        return result;
    }
}
