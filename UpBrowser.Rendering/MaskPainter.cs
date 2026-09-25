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

    public SKImage? TryLoadMaskImage(ComputedStyle style) => TryBuildMaskImage(style, SKRect.Empty);

    /// <summary>
    /// Build the mask paint source for mask-image: a decoded url() image, or a
    /// rasterized gradient sized to the element's border box. The layer blends
    /// with SrcIn, so the gradient's alpha channel is what masks (CSS Masking 1 §11).
    /// </summary>
    public SKImage? TryBuildMaskImage(ComputedStyle style, SKRect borderBox)
    {
        var maskValue = style.MaskImage;
        if (string.IsNullOrEmpty(maskValue) || maskValue == "none")
            return null;

        if (maskValue.Contains("gradient", StringComparison.OrdinalIgnoreCase))
        {
            float width = MathF.Max(1, MathF.Round(borderBox.Width));
            float height = MathF.Max(1, MathF.Round(borderBox.Height));
            var shader = GradientRenderer.CreateGradient(maskValue, new SKRect(0, 0, width, height));
            if (shader == null)
                return null;
            using var paint = new SKPaint { Shader = shader };
            var info = new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null)
                return null;
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawRect(0, 0, width, height, paint);
            return surface.Snapshot();
        }

        var resolved = ResolveMaskUrl(maskValue);
        if (resolved == null) return null;

        var task = _imageCache.GetImageAsync(resolved);
        task.Wait();
        return task.Result;
    }

    private string? ResolveMaskUrl(string url) => UrlResolver.Resolve(url, _baseUrl);
}