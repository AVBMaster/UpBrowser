using SkiaSharp;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public class SkiaTextMeasurer : ITextMeasurer
{
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new();
    private readonly SKTypeface _defaultTypeface;

    public SkiaTextMeasurer()
    {
        _defaultTypeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
    }

    public float MeasureText(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        using var font = CreateFont(fontFamily, fontSize, weight);
        return font.MeasureText(text);
    }

    public float MeasureTextAdvanced(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        using var font = CreateFont(fontFamily, fontSize, weight, style);
        return font.MeasureText(text);
    }

    public (float width, float height, float baseline) MeasureTextDetail(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0, 0);
        
        using var font = CreateFont(fontFamily, fontSize, weight);
        var width = font.MeasureText(text);
        
        var metrics = font.Metrics;
        var height = metrics.Descent - metrics.Ascent;
        var baseline = -metrics.Ascent;
        
        return (width, height, baseline);
    }

    private SKFont CreateFont(string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        var typeface = GetTypeface(fontFamily, weight, style);
        return new SKFont(typeface, fontSize)
        {
            Subpixel = true,
            Hinting = SKFontHinting.Normal
        };
    }

    private SKTypeface GetTypeface(string family, FontWeight weight, FontStyleType style = FontStyleType.Normal)
    {
        var key = $"{family}:{weight}:{style}";
        if (_typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var skStyle = style == FontStyleType.Italic ? SKFontStyleSlant.Italic :
                     style == FontStyleType.Oblique ? SKFontStyleSlant.Oblique :
                     SKFontStyleSlant.Upright;
        
        var skWeight = weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        var fontManager = SKFontManager.Default;
        var typeface = fontManager.MatchFamily(family, new SKFontStyle(skWeight, SKFontStyleWidth.Normal, skStyle));
        
        if (typeface == null)
            typeface = _defaultTypeface ?? SKTypeface.Default;

        _typefaceCache[key] = typeface;
        return typeface;
    }
}
