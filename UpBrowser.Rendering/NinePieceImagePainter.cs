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

    public static void Paint(DisplayList displayList, ImageCache imageCache, ComputedStyle style, SKRect borderRect)
    {
        var source = style.BorderImageSource;
        if (string.IsNullOrEmpty(source) || source == "none") return;

        var url = ParseUrl(source);
        if (url == null) return;

        var task = imageCache.GetImageAsync(url);
        task.Wait();
        var image = task.Result;
        if (image == null) return;

        float imgW = image.Width;
        float imgH = image.Height;
        if (imgW <= 0 || imgH <= 0) return;

        // Parse border-image-slice
        var slice = ParseBoxValues(style.BorderImageSlice, 100f, 100f);
        float sliceTop = slice.top * imgH / 100f;
        float sliceRight = slice.right * imgW / 100f;
        float sliceBottom = slice.bottom * imgH / 100f;
        float sliceLeft = slice.left * imgW / 100f;

        // Clamp slices to image dimensions
        sliceTop = Math.Clamp(sliceTop, 0, imgH);
        sliceRight = Math.Clamp(sliceRight, 0, imgW);
        sliceBottom = Math.Clamp(sliceBottom, 0, imgH);
        sliceLeft = Math.Clamp(sliceLeft, 0, imgW);

        // Parse border-image-width (default to border widths)
        var borderWidth = ParseBoxValues(style.BorderImageWidth, 1f, 1f);
        float bwTop = ResolveWidth(borderWidth.top, style.BorderTopWidth, sliceTop, borderRect.Height);
        float bwRight = ResolveWidth(borderWidth.right, style.BorderRightWidth, sliceRight, borderRect.Width);
        float bwBottom = ResolveWidth(borderWidth.bottom, style.BorderBottomWidth, sliceBottom, borderRect.Height);
        float bwLeft = ResolveWidth(borderWidth.left, style.BorderLeftWidth, sliceLeft, borderRect.Width);

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

    private static float ParseValue(string? s, float defaultValue)
    {
        if (string.IsNullOrEmpty(s)) return defaultValue;
        if (s.EndsWith("%") && float.TryParse(s[..^1], out var pct)) return pct;
        if (float.TryParse(s.Replace("px", ""), out var v)) return v;
        return defaultValue;
    }

    private static float ResolveWidth(float value, float borderWidth, float slice, float extent)
    {
        if (value == 0) return borderWidth;
        return value;
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