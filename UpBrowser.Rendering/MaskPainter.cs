using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Rendering;

/// <summary>
/// Handles CSS mask-image rendering. Mirrors css_mask_painter.cc.
/// Loads the mask image and applies it as a luminance-to-alpha mask layer.
/// </summary>
internal sealed class MaskPainter
{
    private readonly ImageCache _imageCache;
    private readonly string? _baseUrl;

    public MaskPainter(ImageCache imageCache, string? baseUrl)
    {
        _imageCache = imageCache;
        _baseUrl = baseUrl;
    }

    public bool HasMask(ComputedStyle style) =>
        !string.IsNullOrEmpty(style.MaskImage) && style.MaskImage != "none";

    public SKImage? TryLoadMaskImage(ComputedStyle style)
    {
        var maskUrl = style.MaskImage;
        if (string.IsNullOrEmpty(maskUrl) || maskUrl == "none")
            return null;

        var resolved = ResolveMaskUrl(maskUrl);
        if (resolved == null) return null;

        var task = _imageCache.GetImageAsync(resolved);
        task.Wait();
        return task.Result;
    }

    private string? ResolveMaskUrl(string url) => UrlResolver.Resolve(url, _baseUrl);
}