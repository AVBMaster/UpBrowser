using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Rendering;

internal sealed class ImagePainter
{
    private readonly DisplayList _displayList;
    private readonly ImageCache _imageCache;
    private readonly string? _baseUrl;
    private readonly HashSet<string> _lazyLoading = new();

    public ImagePainter(DisplayList displayList, ImageCache imageCache, string? baseUrl)
    {
        _displayList = displayList;
        _imageCache = imageCache;
        _baseUrl = baseUrl;
    }

    public void PaintImage(Element element, ComputedStyle style, LayoutBox box)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var loading = element.GetAttribute("loading");
        if (loading != null && loading.Equals("lazy", StringComparison.OrdinalIgnoreCase))
        {
            if (_lazyLoading.Add(src))
            {
                _ = LoadImageAsync(element, src, style, box);
                return;
            }
        }

        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null)
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var task = _imageCache.GetImageAsync(resolvedSrc);
        task.Wait();
        var image = task.Result;
        if (image == null)
        {
            PaintAltText(element, style, box);
            PaintMissingImagePlaceholder(element, style, box);
            return;
        }

        var rect = BuildDestRect(element, style, box);
        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = new SKRect(0, 0, image.Width, image.Height);
        op.DestRect = rect;
        op.Fit = MapObjectFitToImageFit(style.ObjectFit);
        op.ZIndex = style.ZIndex ?? 0;
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private async Task LoadImageAsync(Element element, string src, ComputedStyle style, LayoutBox box)
    {
        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null) return;
        await _imageCache.GetImageAsync(resolvedSrc);
    }

    private static SKRect BuildDestRect(Element element, ComputedStyle style, LayoutBox box)
    {
        // Honour object-fit and object-position.
        float left = box.ContentBox.Left;
        float top = box.ContentBox.Top;
        float right = box.ContentBox.Right;
        float bottom = box.ContentBox.Bottom;

        // Use the content-box size as the base (the layout has already sized
        // the box to the CSS 'width'/'height' or the default 300×150).
        float conW = right - left;
        float conH = bottom - top;
        if (conW <= 0) conW = 300;
        if (conH <= 0) conH = 150;

        // Get the intrinsic image size from the box's storage.  LayoutImage
        // stores the intrinsic size through LayoutReplaced.IntrinsicSize,
        // but at paint time we only have the LayoutBox.  Fall back to the
        // content-box size (which already reflects the layout sizing).
        float intW = conW;
        float intH = conH;
        var attrWidth = element.GetAttribute("width");
        var attrHeight = element.GetAttribute("height");
        if (!string.IsNullOrEmpty(attrWidth) && float.TryParse(attrWidth, out var aw))
            intW = aw;
        if (!string.IsNullOrEmpty(attrHeight) && float.TryParse(attrHeight, out var ah))
            intH = ah;

        if (intW <= 0 || intH <= 0)
            return new SKRect(left, top, right, bottom);

        // object-fit scaling (mirrors LayoutReplaced.ComputeReplacedContentRect).
        float scaleX = conW / intW;
        float scaleY = conH / intH;
        float scale = style.ObjectFit switch
        {
            ObjectFitType.Contain => Math.Min(scaleX, scaleY),
            ObjectFitType.Cover => Math.Max(scaleX, scaleY),
            ObjectFitType.Fill => 1f,
            _ => 1f,
        };

        float fitW = style.ObjectFit == ObjectFitType.Fill
            ? conW
            : Math.Max(1, intW * scale);
        float fitH = style.ObjectFit == ObjectFitType.Fill
            ? conH
            : Math.Max(1, intH * scale);

        if (style.ObjectFit == ObjectFitType.None)
        {
            fitW = Math.Min(intW, conW);
            fitH = Math.Min(intH, conH);
        }

        // object-position (default 50% 50%).
        float posX = 0.5f, posY = 0.5f;
        if (style.ObjectPositionX is Length lx)
            posX = ResolveObjectPosition(lx, conW - fitW);
        if (style.ObjectPositionY is Length ly)
            posY = ResolveObjectPosition(ly, conH - fitH);

        float destLeft = left + posX * (conW - fitW);
        float destTop = top + posY * (conH - fitH);
        return new SKRect(destLeft, destTop, destLeft + fitW, destTop + fitH);
    }

    private static float ResolveObjectPosition(Length length, float availableDiff)
    {
        if (length is PercentLength pct)
            return pct.Value;
        float px = length.ToPixels(16f, 16f, 0f, 0f);
        return availableDiff > 0 && px != float.NaN ? px / availableDiff : 0;
    }

    private void PaintAltText(Element element, ComputedStyle style, LayoutBox box)
    {
        var rect = BuildDestRect(element, style, box);
        if (rect.Width <= 2 || rect.Height <= 2) return;

        var alt = element.GetAttribute("alt");
        if (!string.IsNullOrEmpty(alt))
        {
            var op = PaintOpPool.GetDrawTextOp();
            op.Text = alt;
            op.X = rect.Left + 4;
            op.Y = rect.Top + style.FontSize;
            op.Color = style.Color;
            op.FontSize = style.FontSize;
            op.FontFamily = style.FontFamily ?? "Arial";
            op.Bounds = rect;
            _displayList.Add(op);
        }
    }

    private void PaintMissingImagePlaceholder(Element element, ComputedStyle style, LayoutBox box)
    {
        var rect = BuildDestRect(element, style, box);
        if (rect.Width <= 2 || rect.Height <= 2) return;

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = rect;
        op.FillColor = SKColors.Transparent;
        op.BorderTopWidth = 1;
        op.BorderRightWidth = 1;
        op.BorderBottomWidth = 1;
        op.BorderLeftWidth = 1;
        op.BorderTopColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderRightColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderBottomColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.BorderLeftColor = new SKColor(0xCC, 0xCC, 0xCC, 0xFF);
        op.Bounds = rect;
        _displayList.Add(op);
    }

    private string? ResolveImageUrl(string url) => UrlResolver.Resolve(url, _baseUrl);

    private static ImageFit MapObjectFitToImageFit(ObjectFitType objectFit) => objectFit switch
    {
        ObjectFitType.Fill => ImageFit.Fill,
        ObjectFitType.Contain => ImageFit.Contain,
        ObjectFitType.Cover => ImageFit.Cover,
        ObjectFitType.None => ImageFit.None,
        ObjectFitType.ScaleDown => ImageFit.ScaleDown,
        _ => ImageFit.Fill
    };
}