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

    private string? ResolveMaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("url("))
            url = url[4..^1].Trim('\'', '"');

        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:") || url.StartsWith("blob:"))
            return url;
        if (url.StartsWith("//"))
        {
            return (!string.IsNullOrEmpty(_baseUrl) && _baseUrl.StartsWith("https://"))
                ? "https:" + url : "http:" + url;
        }
        if (string.IsNullOrEmpty(_baseUrl)) return url;
        try
        {
            var baseUri = new Uri(_baseUrl.EndsWith('/') ? _baseUrl : _baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return url; }
    }
}