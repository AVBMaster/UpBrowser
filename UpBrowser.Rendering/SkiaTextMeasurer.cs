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
        
        var paint = CreatePaint(fontFamily, fontSize, weight);
        return paint.MeasureText(text);
    }

    public float MeasureTextAdvanced(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        var paint = CreatePaint(fontFamily, fontSize, weight, style);
        return paint.MeasureText(text);
    }

    public (float width, float height, float baseline) MeasureTextDetail(string text, string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0, 0);
        
        var paint = CreatePaint(fontFamily, fontSize, weight);
        var width = paint.MeasureText(text);
        
        var metrics = paint.FontMetrics;
        var height = metrics.Descent - metrics.Ascent;
        var baseline = -metrics.Ascent;
        
        return (width, height, baseline);
    }

    private SKPaint CreatePaint(string fontFamily, float fontSize, FontWeight weight = FontWeight.Normal, FontStyleType style = FontStyleType.Normal)
    {
        var typeface = GetTypeface(fontFamily, weight, style);
        return new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            IsAntialias = true,
            SubpixelText = true
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

        // Try to find the font family
        var fontManager = SKFontManager.Default;
        var typeface = fontManager.MatchFamily(family, new SKFontStyle(skWeight, SKFontStyleWidth.Normal, skStyle));
        
        if (typeface == null)
            typeface = _defaultTypeface ?? SKTypeface.Default;

        _typefaceCache[key] = typeface;
        return typeface;
    }
}
