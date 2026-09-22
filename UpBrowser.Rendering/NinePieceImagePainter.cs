using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance;

namespace UpBrowser.Rendering;

/// <summary>
/// Renders border-image by splitting the source image into 9 pieces and
/// mapping them onto the border area. Mirrors nine_piece_image_painter.cc
/// and nine_piece_image_grid.cc.
/// </summary>
internal static class NinePieceImagePainter
{
    public static bool HasBorderImage(ComputedStyle style) =>
        !string.IsNullOrEmpty(style.BorderImageSource) && style.BorderImageSource != "none";

    public static void Paint(DisplayList displayList, ImageCache imageCache, ComputedStyle style, SKRect borderRect, string? baseUrl = null)
    {
        var source = style.BorderImageSource;
        if (string.IsNullOrEmpty(source) || source == "none") return;

        var url = ParseUrl(source);
        if (url == null) return;

        url = UrlResolver.Resolve(url, baseUrl);
        if (url == null) return;

        var task = imageCache.GetImageAsync(url);
        task.Wait();
        var image = task.Result;
        if (image == null) return;

        float imgW = image.Width;
        float imgH = image.Height;
        if (imgW <= 0 || imgH <= 0) return;

        // Parse border-image-slice (numbers are px, % of image edge-to-edge)
        var sliceSides = ParseBoxStrings(style.BorderImageSlice, "100%");
        float sliceTop = ResolveSlice(sliceSides.top, imgH);
        float sliceRight = ResolveSlice(sliceSides.right, imgW);
        float sliceBottom = ResolveSlice(sliceSides.bottom, imgH);
        float sliceLeft = ResolveSlice(sliceSides.left, imgW);

        // Clamp slices to image dimensions
        sliceTop = Math.Clamp(sliceTop, 0, imgH);
        sliceRight = Math.Clamp(sliceRight, 0, imgW);
        sliceBottom = Math.Clamp(sliceBottom, 0, imgH);
        sliceLeft = Math.Clamp(sliceLeft, 0, imgW);

        // Parse border-image-width: auto -> border width, number -> x border
        // width, % -> of the border-box extent, px -> pixels.
        var widthSides = ParseBoxStrings(style.BorderImageWidth, "auto");
        float bwTop = ResolveWidthSide(widthSides.top, style.BorderTopWidth, borderRect.Height);
        float bwRight = ResolveWidthSide(widthSides.right, style.BorderRightWidth, borderRect.Width);
        float bwBottom = ResolveWidthSide(widthSides.bottom, style.BorderBottomWidth, borderRect.Height);
        float bwLeft = ResolveWidthSide(widthSides.left, style.BorderLeftWidth, borderRect.Width);

        // Parse border-image-outset
        var outset = ParseBoxValues(style.BorderImageOutset, 0f, 0f);
        float oTop = outset.top;
        float oRight = outset.right;
        float oBottom = outset.bottom;
        float oLeft = outset.left;

        // Expand border rect by outset
        var destRect = new SKRect(
            borderRect.Left - oLeft,
            borderRect.Top - oTop,
            borderRect.Right + oRight,
            borderRect.Bottom + oBottom);

        // Parse border-image-repeat
        var repeat = ParseRepeat(style.BorderImageRepeat);
        bool fill = style.BorderImageSlice.Contains("fill");

        // Compute source rects for the 9 pieces
        // Corners (source)
        var srcTL = new SKRect(0, 0, sliceLeft, sliceTop);
        var srcTR = new SKRect(imgW - sliceRight, 0, imgW, sliceTop);
        var srcBL = new SKRect(0, imgH - sliceBottom, sliceLeft, imgH);
        var srcBR = new SKRect(imgW - sliceRight, imgH - sliceBottom, imgW, imgH);
        // Edges (source)
        var srcTop = new SKRect(sliceLeft, 0, imgW - sliceRight, sliceTop);
        var srcBottom = new SKRect(sliceLeft, imgH - sliceBottom, imgW - sliceRight, imgH);
        var srcLeft = new SKRect(0, sliceTop, sliceLeft, imgH - sliceBottom);
        var srcRight = new SKRect(imgW - sliceRight, sliceTop, imgW, imgH - sliceBottom);
        // Middle (source)
        var srcMiddle = new SKRect(sliceLeft, sliceTop, imgW - sliceRight, imgH - sliceBottom);

        // Compute destination rects for the 9 pieces
        // Corners (destination)
        var dstTL = new SKRect(destRect.Left, destRect.Top, destRect.Left + bwLeft, destRect.Top + bwTop);
        var dstTR = new SKRect(destRect.Right - bwRight, destRect.Top, destRect.Right, destRect.Top + bwTop);
        var dstBL = new SKRect(destRect.Left, destRect.Bottom - bwBottom, destRect.Left + bwLeft, destRect.Bottom);
        var dstBR = new SKRect(destRect.Right - bwRight, destRect.Bottom - bwBottom, destRect.Right, destRect.Bottom);
        // Edges (destination)
        var dstTop = new SKRect(destRect.Left + bwLeft, destRect.Top, destRect.Right - bwRight, destRect.Top + bwTop);
        var dstBottom = new SKRect(destRect.Left + bwLeft, destRect.Bottom - bwBottom, destRect.Right - bwRight, destRect.Bottom);
        var dstLeft = new SKRect(destRect.Left, destRect.Top + bwTop, destRect.Left + bwLeft, destRect.Bottom - bwBottom);
        var dstRight = new SKRect(destRect.Right - bwRight, destRect.Top + bwTop, destRect.Right, destRect.Bottom - bwBottom);
        // Middle (destination)
        var dstMiddle = new SKRect(destRect.Left + bwLeft, destRect.Top + bwTop, destRect.Right - bwRight, destRect.Bottom - bwBottom);

        // Paint corners (always stretched, no tiling)
        PaintImagePiece(displayList, image, srcTL, dstTL, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcTR, dstTR, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcBL, dstBL, ImageFit.Fill);
        PaintImagePiece(displayList, image, srcBR, dstBR, ImageFit.Fill);

        // Paint edges
        PaintImagePiece(displayList, image, srcTop, dstTop, repeat.horizontal == "stretch" ? ImageFit.Fill : ImageFit.None);
        PaintImagePiece(displayList, image, srcBottom, dstBottom, repeat.horizontal == "stretch" ? ImageFit.Fill : ImageFit.None);
        PaintImagePiece(displayList, image, srcLeft, dstLeft, repeat.vertical == "stretch" ? ImageFit.Fill : ImageFit.None);
        PaintImagePiece(displayList, image, srcRight, dstRight, repeat.vertical == "stretch" ? ImageFit.Fill : ImageFit.None);

        // Paint middle (only if fill is specified)
        if (fill && srcMiddle.Width > 0 && srcMiddle.Height > 0 && dstMiddle.Width > 0 && dstMiddle.Height > 0)
        {
            PaintImagePiece(displayList, image, srcMiddle, dstMiddle, ImageFit.Fill);
        }
    }

    private static void PaintImagePiece(DisplayList displayList, SKImage image, SKRect src, SKRect dst, ImageFit fit)
    {
        if (src.Width <= 0 || src.Height <= 0 || dst.Width <= 0 || dst.Height <= 0) return;

        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = src;
        op.DestRect = dst;
        op.Fit = fit;
        op.Bounds = dst;
        displayList.Add(op);
    }

    private static string? ParseUrl(string source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        if (source.StartsWith("url("))
            return source[4..^1].Trim('\'', '"');
        return source;
    }

    private static (float top, float right, float bottom, float left) ParseBoxValues(string value, float defaultX, float defaultY)
    {
        if (string.IsNullOrEmpty(value)) return (defaultY, defaultX, defaultY, defaultX);

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float v1 = ParseValue(parts.Length > 0 ? parts[0] : null, defaultY);
        float v2 = ParseValue(parts.Length > 1 ? parts[1] : null, defaultX);
        float v3 = ParseValue(parts.Length > 2 ? parts[2] : null, defaultY);
        float v4 = ParseValue(parts.Length > 3 ? parts[3] : null, defaultX);

        if (parts.Length == 1) return (v1, v1, v1, v1);
        if (parts.Length == 2) return (v1, v2, v1, v2);
        if (parts.Length == 3) return (v1, v2, v3, v2);
        return (v1, v2, v3, v4);
    }

    /// <summary>
    /// Expands a 1-to-4 side value list into top/right/bottom/left strings
    /// without resolving units, so each side keeps its own unit semantics.
    /// </summary>
    private static (string top, string right, string bottom, string left) ParseBoxStrings(string value, string defaultSide)
    {
        if (string.IsNullOrEmpty(value)) return (defaultSide, defaultSide, defaultSide, defaultSide);
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string p1 = parts.Length > 0 ? parts[0] : defaultSide;
        string p2 = parts.Length > 1 ? parts[1] : p1;
        string p3 = parts.Length > 2 ? parts[2] : p1;
        string p4 = parts.Length > 3 ? parts[3] : p2;
        return (p1, p2, p3, p4);
    }

    private static float ParseValue(string? s, float defaultValue)
    {
        if (string.IsNullOrEmpty(s)) return defaultValue;
        if (s.EndsWith("%") && float.TryParse(s[..^1], out var pct)) return pct;
        if (float.TryParse(s.Replace("px", ""), out var v)) return v;
        return defaultValue;
    }

    private enum MeasureKind { Auto, Number, Percent, Length }

    private static (MeasureKind Kind, float Value) ParseMeasure(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return (MeasureKind.Auto, 0);
        if (s.EndsWith("%") && float.TryParse(s[..^1], out var pct))
            return (MeasureKind.Percent, pct);
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase) && s.Length > 2 && float.TryParse(s[..^2], out var px))
            return (MeasureKind.Length, px);
        if (float.TryParse(s, out var num))
            return (MeasureKind.Number, num);
        return (MeasureKind.Auto, 0);
    }

    /// <summary>border-image-slice: % of the image extent, bare numbers/px are pixels.</summary>
    private static float ResolveSlice(string side, float imageExtent)
    {
        var (kind, value) = ParseMeasure(side);
        return kind switch
        {
            MeasureKind.Percent => value / 100f * imageExtent,
            MeasureKind.Auto => imageExtent,
            _ => value,
        };
    }

    /// <summary>border-image-width: auto/border width, number x border width, % of border-box extent, px pixels.</summary>
    private static float ResolveWidthSide(string side, float borderWidth, float extent)
    {
        var (kind, value) = ParseMeasure(side);
        return kind switch
        {
            MeasureKind.Auto => borderWidth,
            MeasureKind.Number => value * borderWidth,
            MeasureKind.Percent => value / 100f * extent,
            _ => value,
        };
    }

    private static (string horizontal, string vertical) ParseRepeat(string repeat)
    {
        if (string.IsNullOrEmpty(repeat)) return ("stretch", "stretch");
        var parts = repeat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var h = parts.Length > 0 ? parts[0].ToLowerInvariant() : "stretch";
        var v = parts.Length > 1 ? parts[1].ToLowerInvariant() : h;
        return (h, v);
    }
}