using System.Collections.Concurrent;
using System.Linq;
using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public abstract class PaintOp
{
    public SKRect Bounds { get; set; }
    public int ZIndex { get; set; }
    public abstract void Execute(SKCanvas canvas);
    public virtual void Reset() { Bounds = default; ZIndex = 0; }

    protected static (float scaleX, float scaleY) GetCanvasScale(SKCanvas canvas)
    {
        try
        {
            var m = canvas.TotalMatrix;
            // SKMatrix has ScaleX/ScaleY fields
            float sx = MathF.Abs(m.ScaleX);
            float sy = MathF.Abs(m.ScaleY);
            if (sx <= 0) sx = 1f;
            if (sy <= 0) sy = 1f;
            return (sx, sy);
        }
        catch
        {
            return (1f, 1f);
        }
    }

    protected static float SnapToDevice(float v, float scale)
    {
        if (scale <= 0) return v;
        return MathF.Round(v * scale) / scale;
    }

    protected static SKRect AlignRectToDevice(SKRect r, SKCanvas canvas)
    {
        var (sx, sy) = GetCanvasScale(canvas);
        var left = SnapToDevice(r.Left, sx);
        var top = SnapToDevice(r.Top, sy);
        var right = SnapToDevice(r.Right, sx);
        var bottom = SnapToDevice(r.Bottom, sy);
        // Ensure non-negative width/height
        if (right < left) right = left;
        if (bottom < top) bottom = top;
        return new SKRect(left, top, right, bottom);
    }

    public void AlignBounds(SKCanvas canvas)
    {
        Bounds = AlignRectToDevice(Bounds, canvas);
    }
}

public class DrawRectOp : PaintOp
{
    public SKRect Rect { get; set; }
    public SKColor FillColor { get; set; }
    public float BorderTopWidth { get; set; }
    public float BorderRightWidth { get; set; }
    public float BorderBottomWidth { get; set; }
    public float BorderLeftWidth { get; set; }
    public SKColor BorderTopColor { get; set; }
    public SKColor BorderRightColor { get; set; }
    public SKColor BorderBottomColor { get; set; }
    public SKColor BorderLeftColor { get; set; }
    public BorderStyle BorderTopStyle { get; set; }
    public BorderStyle BorderRightStyle { get; set; }
    public BorderStyle BorderBottomStyle { get; set; }
    public BorderStyle BorderLeftStyle { get; set; }
    public float BorderRadius { get; set; }

    public override void Reset()
    {
        base.Reset();
        Rect = default;
        FillColor = default;
        BorderTopWidth = BorderRightWidth = BorderBottomWidth = BorderLeftWidth = 0;
        BorderTopColor = BorderRightColor = BorderBottomColor = BorderLeftColor = default;
        BorderTopStyle = BorderRightStyle = BorderBottomStyle = BorderLeftStyle = BorderStyle.None;
        BorderRadius = 0;
    }

    public override void Execute(SKCanvas canvas)
    {
        bool hasBorder = BorderTopWidth > 0 || BorderRightWidth > 0 ||
                         BorderBottomWidth > 0 || BorderLeftWidth > 0;
        bool hasFill = FillColor.Alpha > 0;

        if (BorderRadius > 0 && (hasFill || hasBorder))
        {
            ExecuteWithRoundRect(canvas, hasFill, hasBorder);
        }
        else
        {
            ExecuteWithFlatRect(canvas, hasFill, hasBorder);
        }
    }

    private void ExecuteWithRoundRect(SKCanvas canvas, bool hasFill, bool hasBorder)
    {
        using var borderPath = new SKPath();
        var aligned = AlignRectToDevice(Rect, canvas);
        borderPath.AddRoundRect(aligned, BorderRadius, BorderRadius);

        if (hasBorder)
        {
            var borderWidth = Math.Max(BorderTopWidth, Math.Max(BorderBottomWidth,
                Math.Max(BorderLeftWidth, BorderRightWidth)));

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = borderWidth,
                IsAntialias = true
            };

            float inset = borderWidth / 2;
            var innerRect = new SKRect(
                Rect.Left + inset,
                Rect.Top + inset,
                Rect.Right - inset,
                Rect.Bottom - inset);
            using var strokePath = new SKPath();
            strokePath.AddRoundRect(innerRect, Math.Max(0, BorderRadius - inset), Math.Max(0, BorderRadius - inset));
            borderPaint.Color = BorderTopColor;
            canvas.DrawPath(strokePath, borderPaint);
        }

        if (hasFill)
        {
            using var fillPaint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(borderPath, fillPaint);
        }
    }

    private void ExecuteWithFlatRect(SKCanvas canvas, bool hasFill, bool hasBorder)
    {
        var alignedRect = AlignRectToDevice(Rect, canvas);
        if (hasFill)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(alignedRect, paint);
        }

        if (hasBorder)
        {
            if (BorderTopWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Top, alignedRect.Right, alignedRect.Top, BorderTopWidth, BorderTopColor, BorderTopStyle);
            if (BorderBottomWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Bottom, alignedRect.Right, alignedRect.Bottom, BorderBottomWidth, BorderBottomColor, BorderBottomStyle);
            if (BorderLeftWidth > 0)
                DrawBorderSide(canvas, alignedRect.Left, alignedRect.Top, alignedRect.Left, alignedRect.Bottom, BorderLeftWidth, BorderLeftColor, BorderLeftStyle);
            if (BorderRightWidth > 0)
                DrawBorderSide(canvas, alignedRect.Right, alignedRect.Top, alignedRect.Right, alignedRect.Bottom, BorderRightWidth, BorderRightColor, BorderRightStyle);
        }
    }

    private static void DrawBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color, BorderStyle style)
    {
        if (style == BorderStyle.Groove || style == BorderStyle.Ridge ||
            style == BorderStyle.Inset || style == BorderStyle.Outset)
        {
            DrawSpecialBorderSide(canvas, x1, y1, x2, y2, width, color, style);
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            IsAntialias = true
        };
        if (style == BorderStyle.Dashed)
            paint.PathEffect = SKPathEffect.CreateDash(new[] { width * 4, width * 4 }, 0);
        else if (style == BorderStyle.Dotted)
            paint.PathEffect = SKPathEffect.CreateDash(new[] { width, width }, 0);
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawSpecialBorderSide(SKCanvas canvas, float x1, float y1, float x2, float y2, float width, SKColor color, BorderStyle style)
    {
        bool isHorizontal = MathF.Abs(y1 - y2) < 0.5f;
        SKColor light = AdjustBrightness(color, 1.4f);
        SKColor dark = AdjustBrightness(color, 0.6f);
        SKColor half = AdjustBrightness(color, 0.8f);

        float halfW = width / 2f;
        if (isHorizontal)
        {
            float left = MathF.Min(x1, x2);
            float right = MathF.Max(x1, x2);
            SKColor topColor, bottomColor;
            switch (style)
            {
                case BorderStyle.Groove:
                    topColor = dark; bottomColor = light; break;
                case BorderStyle.Ridge:
                    topColor = light; bottomColor = dark; break;
                case BorderStyle.Inset:
                    topColor = dark; bottomColor = light; break;
                default: // Outset
                    topColor = light; bottomColor = dark; break;
            }
            using var p1 = new SKPaint { Color = topColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(left, y1 - halfW, right - left, halfW, p1);
            using var p2 = new SKPaint { Color = bottomColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(left, y1, right - left, halfW, p2);
        }
        else
        {
            float top = MathF.Min(y1, y2);
            float bottom = MathF.Max(y1, y2);
            SKColor leftColor, rightColor;
            switch (style)
            {
                case BorderStyle.Groove:
                    leftColor = dark; rightColor = light; break;
                case BorderStyle.Ridge:
                    leftColor = light; rightColor = dark; break;
                case BorderStyle.Inset:
                    leftColor = dark; rightColor = light; break;
                default: // Outset
                    leftColor = light; rightColor = dark; break;
            }
            using var p1 = new SKPaint { Color = leftColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(x1 - halfW, top, halfW, bottom - top, p1);
            using var p2 = new SKPaint { Color = rightColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(x1, top, halfW, bottom - top, p2);
        }
    }

    private static SKColor AdjustBrightness(SKColor color, float factor)
    {
        byte r = (byte)MathF.Min(255, color.Red * factor);
        byte g = (byte)MathF.Min(255, color.Green * factor);
        byte b = (byte)MathF.Min(255, color.Blue * factor);
        return new SKColor(r, g, b, color.Alpha);
    }
}

public class DrawTextOp : PaintOp
{
    public string Text { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public SKColor Color { get; set; }
    public float FontSize { get; set; } = 16;
    public string FontFamily { get; set; } = "Arial";
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;
    public TextAlignType TextAlign { get; set; } = TextAlignType.Start;
    public float? MaxWidth { get; set; }
    public bool Underline { get; set; }
    public bool LineThrough { get; set; }
    public bool Overline { get; set; }
    public SKColor UnderlineColor { get; set; }
    public TextDecorationStyleType DecorationStyle { get; set; } = TextDecorationStyleType.Solid;
    public List<TextShadowValue>? TextShadows { get; set; }
    public float LetterSpacing { get; set; }
    public bool Italic { get; set; }

    public override void Reset()
    {
        base.Reset();
        Text = string.Empty;
        X = Y = 0;
        Color = default;
        FontSize = 16;
        FontFamily = "Arial";
        FontWeight = FontWeight.Normal;
        TextAlign = TextAlignType.Start;
        MaxWidth = null;
        Underline = LineThrough = Overline = false;
        UnderlineColor = default;
        DecorationStyle = TextDecorationStyleType.Solid;
        TextShadows = null;
        LetterSpacing = 0;
        Italic = false;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (string.IsNullOrEmpty(Text)) return;

        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        float x = X;

        // Draw text shadows before main text
        if (TextShadows != null && TextShadows.Count > 0)
        {
            var (scaleX, scaleY) = GetCanvasScale(canvas);
            foreach (var shadow in TextShadows)
            {
                using var shadowPaint = new SKPaint
                {
                    Color = shadow.Color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                    ImageFilter = shadow.BlurRadius > 0 ? SKImageFilter.CreateBlur(shadow.BlurRadius, shadow.BlurRadius) : null
                };
                float shadowX = SnapToDevice(x + shadow.OffsetX, scaleX);
                float shadowY = SnapToDevice(Y + shadow.OffsetY, scaleY);
                canvas.DrawText(Text, shadowX, shadowY, shadowPaint);
            }
        }
        bool needsAlignment = TextAlign == TextAlignType.Center || TextAlign == TextAlignType.End || TextAlign == TextAlignType.Right;

        float totalWidth;
        if (needsAlignment)
        {
            totalWidth = MeasureTextWithFallback(canvas, paint, x, Y, dryRun: true);
        }
        else
        {
            totalWidth = 0;
        }

        if (TextAlign == TextAlignType.Center)
            x -= totalWidth / 2;
        else if (TextAlign == TextAlignType.End || TextAlign == TextAlignType.Right)
            x -= totalWidth;

        // Snap coordinates to device pixels to reduce subpixel differences
        var (sx, sy) = GetCanvasScale(canvas);
        float drawX = SnapToDevice(x, sx);
        float drawY = SnapToDevice(Y, sy);
        float actualWidth = MeasureTextWithFallback(canvas, paint, drawX, drawY, dryRun: false);

        if (Underline)
        {
            float underlineY = drawY + 2;
            var underlineColor = UnderlineColor.Alpha > 0 ? UnderlineColor : Color;
            if (DecorationStyle == TextDecorationStyleType.Wavy)
            {
                using var wavyPath = new SKPath();
                float waveLength = MathF.Max(4, FontSize * 0.15f);
                float amplitude = MathF.Max(1.5f, FontSize * 0.05f);
                float endX = drawX + actualWidth;
                float x0 = drawX;
                wavyPath.MoveTo(x0, underlineY);
                int segments = Math.Max(1, (int)((endX - x0) / waveLength));
                for (int i = 0; i < segments; i++)
                {
                    float t0 = (float)i / segments;
                    float t1 = (float)(i + 0.5f) / segments;
                    float t2 = (float)(i + 1) / segments;
                    float cx1 = x0 + (endX - x0) * t1;
                    float cy1 = underlineY - amplitude;
                    float cx2 = x0 + (endX - x0) * t2;
                    float cy2 = underlineY;
                    wavyPath.QuadTo(cx1, cy1, cx2, cy2);
                }
                using var wavyPaint = new SKPaint
                {
                    Color = underlineColor,
                    StrokeWidth = 1,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawPath(wavyPath, wavyPaint);
            }
            else if (DecorationStyle == TextDecorationStyleType.Double)
            {
                using var doublePaint = new SKPaint
                {
                    Color = underlineColor,
                    StrokeWidth = 1,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawLine(drawX, underlineY - 1, drawX + actualWidth, underlineY - 1, doublePaint);
                canvas.DrawLine(drawX, underlineY + 1, drawX + actualWidth, underlineY + 1, doublePaint);
            }
            else if (DecorationStyle == TextDecorationStyleType.Dotted || DecorationStyle == TextDecorationStyleType.Dashed)
            {
                float[] intervals = DecorationStyle == TextDecorationStyleType.Dotted ? new[] { 1f, 3f } : new[] { 5f, 3f };
                using var dashPaint = new SKPaint
                {
                    Color = underlineColor,
                    StrokeWidth = 1,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true,
                    PathEffect = SKPathEffect.CreateDash(intervals, 0)
                };
                canvas.DrawLine(drawX, underlineY, drawX + actualWidth, underlineY, dashPaint);
            }
            else
            {
                using var underlinePaint = new SKPaint
                {
                    Color = underlineColor,
                    StrokeWidth = 1,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawLine(drawX, underlineY, drawX + actualWidth, underlineY, underlinePaint);
            }
        }

        if (LineThrough)
        {
            float strikeY = Y - FontSize * 0.3f;
            using var strikePaint = new SKPaint
            {
                Color = Color,
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawLine(x, strikeY, x + actualWidth, strikeY, strikePaint);
        }

        if (Overline)
        {
            float overlineY = Y - FontSize * 1.15f;
            var overlineColor = UnderlineColor.Alpha > 0 ? UnderlineColor : Color;
            using var overlinePaint = new SKPaint
            {
                Color = overlineColor,
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawLine(drawX, overlineY, drawX + actualWidth, overlineY, overlinePaint);
        }
    }

    private float MeasureTextWithFallback(SKCanvas canvas, SKPaint paint, float x, float y, bool dryRun)
    {
        if (string.IsNullOrEmpty(Text)) return 0;

        var text = Text;
        int len = text.Length;
        float currentX = x;

        int runStart = 0;
        SKTypeface currentTypeface = GetTypefaceForChar(text[0]);

        for (int i = 1; i <= len; i++)
        {
            if (i < len)
            {
                char c = text[i];
                SKTypeface neededTypeface = GetTypefaceForChar(c);

                if (neededTypeface != currentTypeface)
                {
                    string run = text[runStart..i];
                    using var font = CreateFont(currentTypeface);
                    float runWidth = font.MeasureText(run) + (run.Length - 1) * LetterSpacing;
                    if (!dryRun)
                        canvas.DrawText(run, currentX, y, font, paint);
                    currentX += runWidth;

                    currentTypeface = neededTypeface;
                    runStart = i;
                }
            }
            else
            {
                string run = text[runStart..i];
                using var font = CreateFont(currentTypeface);
                float runWidth = font.MeasureText(run) + (run.Length - 1) * LetterSpacing;
                if (!dryRun)
                    canvas.DrawText(run, currentX, y, font, paint);
                currentX += runWidth;
            }
        }

        return currentX - x;
    }

    private SKFont CreateFont(SKTypeface typeface)
    {
        var actualTypeface = typeface;
        if (typeface != null && (Italic || FontWeight == FontWeight.Bold))
        {
            var families = GetFontFamilies();
            var familyName = typeface.FamilyName;
            var index = Array.IndexOf(families, familyName);
            if (index >= 0)
            {
                var styles = SKFontManager.Default.GetFontStyles(index);
                SKFontStyleSlant slant = Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                SKFontStyleWeight weight = FontWeight == FontWeight.Bold
                    ? SKFontStyleWeight.Bold
                    : SKFontStyleWeight.Normal;
                var targetStyle = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
                var tf = styles.CreateTypeface(targetStyle);
                if (tf != null) actualTypeface = tf;
            }
        }
        return new SKFont(actualTypeface, FontSize)
        {
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
            Hinting = SKFontHinting.Normal
        };
    }

    private SKTypeface GetTypefaceForChar(char c)
    {
        int codePoint = c;
        bool isCjk = (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) ||
                     (c >= 0x20000 && c <= 0x2A6DF) || (c >= 0x2B740 && c <= 0x2B81F) ||
                     (c >= 0x2B820 && c <= 0x2CEAF) || (c >= 0x3000 && c <= 0x303F) ||
                     (c >= 0xFF00 && c <= 0xFFEF);
        bool isEmoji = c >= 0x2600;
        bool isSpecialSymbol = (c >= 0x2000 && c <= 0x206F) || (c >= 0x2100 && c <= 0x27BF) ||
                               (c >= 0x2800 && c <= 0x28FF) || c == 0x00A0 || c == 0x00A9 ||
                               c == 0x00AE || (c >= 0x2190 && c <= 0x21FF) ||
                               (c >= 0x2200 && c <= 0x22FF) || (c >= 0x2300 && c <= 0x23FF);

        // First try matching from requested font family
        if (!string.IsNullOrEmpty(FontFamily))
        {
            var fontName = FontFamily.Split(',')[0].Trim().Trim('"', '\'');
            var families = GetFontFamilies();
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var styles = SKFontManager.Default.GetFontStyles(index);
                int styleIdx = FontWeight == FontWeight.Bold ? 1 : 0;
                if (styleIdx >= styles.Count) styleIdx = 0;
                var tf = styles.CreateTypeface(styleIdx);
                if (tf != null && tf.ContainsGlyph(codePoint))
                    return tf;
            }
        }

        if (isCjk)
            return GetCachedChineseTypeface();
        if (isEmoji)
            return GetCachedEmojiTypeface() ?? GetCachedChineseTypeface();
        if (isSpecialSymbol)
            return GetCachedDefaultTypeface();

        var defaultTf = GetCachedDefaultTypeface();
        if (defaultTf.ContainsGlyph(codePoint))
            return defaultTf;

        return GetCachedChineseTypeface();
    }

    private static SKTypeface? _cachedChineseTypeface;
    private static bool _isChineseTypefaceDisposed = false;

    private static SKTypeface? _cachedDefaultTypeface;
    private static bool _isDefaultTypefaceDisposed = false;

    private static SKTypeface? _cachedEmojiTypeface;
    private static bool _isEmojiTypefaceDisposed = false;

    private static string[] _cachedFontFamilies = null!;
    private static readonly object _fontFamiliesLock = new();
    private static readonly Dictionary<string, SKTypeface> _globalTypefaceCache = new();
    private static readonly LinkedList<string> _typefaceCacheOrder = new();
    private const int MaxTypefaceCacheSize = 64;

    private static void CacheTypeface(string key, SKTypeface tf)
    {
        if (_globalTypefaceCache.Count >= MaxTypefaceCacheSize)
        {
            var last = _typefaceCacheOrder.Last;
            if (last != null)
            {
                _globalTypefaceCache.Remove(last.Value);
                _typefaceCacheOrder.RemoveLast();
            }
        }
        _globalTypefaceCache[key] = tf;
        _typefaceCacheOrder.Remove(key);
        _typefaceCacheOrder.AddFirst(key);
    }

    private static string[] GetFontFamilies()
    {
        if (_cachedFontFamilies == null)
        {
            lock (_fontFamiliesLock)
            {
                if (_cachedFontFamilies == null)
                    _cachedFontFamilies = SKFontManager.Default.FontFamilies.ToArray();
            }
        }
        return _cachedFontFamilies;
    }

    private static SKTypeface? GetCachedEmojiTypeface()
    {
        if (_cachedEmojiTypeface != null && !_isEmojiTypefaceDisposed)
        {
            try
            {
                return _cachedEmojiTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isEmojiTypefaceDisposed = true;
                _cachedEmojiTypeface = null;
            }
        }

        var families = GetFontFamilies();
        string[] emojiFonts = { "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", "Segoe UI Symbol" };

        foreach (var fontName in emojiFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedEmojiTypeface = tf;
                    _isEmojiTypefaceDisposed = false;
                    return tf;
                }
            }
        }

        return null;
    }

    private static SKTypeface GetCachedChineseTypeface()
    {
        if (_cachedChineseTypeface != null && !_isChineseTypefaceDisposed)
        {
            try
            {
                return _cachedChineseTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isChineseTypefaceDisposed = true;
                _cachedChineseTypeface = null;
            }
        }
        
        var families = GetFontFamilies();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong", "YouYuan", "STSong", "PingFang SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "Droid Sans Fallback" };
        
        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedChineseTypeface = tf;
                    _isChineseTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedChineseTypeface = SKTypeface.Default;
        _isChineseTypefaceDisposed = false;
        return _cachedChineseTypeface!;
    }
    
    private SKTypeface GetTypeface()
    {
        if (string.IsNullOrEmpty(FontFamily))
        {
            return ContainsChinese(Text) ? GetCachedChineseTypeface() : GetCachedDefaultTypeface();
        }

        var fontName = FontFamily.Split(',')[0].Trim().Trim('"', '\'');
        bool hasChinese = ContainsChinese(Text);
        var cacheKey = hasChinese ? $"tf:{fontName}:zh" : $"tf:{fontName}";

        lock (_globalTypefaceCache)
        {
            if (_globalTypefaceCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var families = GetFontFamilies();
        var index = Array.IndexOf(families, fontName);
        if (index >= 0)
        {
            var style = SKFontManager.Default.GetFontStyles(index);
            var tf = style.CreateTypeface(0);
            if (tf != null && tf.FamilyName != null)
            {
                lock (_globalTypefaceCache)
                    CacheTypeface(cacheKey, tf);
                return tf;
            }
        }

        if (hasChinese)
        {
            var chineseTf = GetCachedChineseTypeface();
            lock (_globalTypefaceCache)
                CacheTypeface(cacheKey, chineseTf);
            return chineseTf;
        }

        var defaultTf = GetCachedDefaultTypeface();
        lock (_globalTypefaceCache)
            CacheTypeface(cacheKey, defaultTf);
        return defaultTf;
    }
    
    private static SKTypeface GetCachedDefaultTypeface()
    {
        if (_cachedDefaultTypeface != null && !_isDefaultTypefaceDisposed)
        {
            try { return _cachedDefaultTypeface; }
            catch (ObjectDisposedException) { _isDefaultTypefaceDisposed = true; _cachedDefaultTypeface = null; }
        }
        
        var families = GetFontFamilies();
        string[] defaultFonts = { "Segoe UI", "Arial", "Tahoma", "Verdana", "Helvetica", "DejaVu Sans", "Liberation Sans" };
        
        foreach (var fontName in defaultFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedDefaultTypeface = tf;
                    _isDefaultTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedDefaultTypeface = SKTypeface.Default;
        _isDefaultTypefaceDisposed = false;
        return _cachedDefaultTypeface!;
    }
    
    private static bool ContainsChinese(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
            if (c >= 0x3400 && c <= 0x4DBF)
                return true;
            if (c >= 0x20000 && c <= 0x2A6DF)
                return true;
        }
        return false;
    }

    private static bool HasEmoji(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x2600 && c <= 0x27BF) || c == 0x200D || c == 0xFE0F ||
                (c >= 0x1F000 && c <= 0x1FFFF) || (c >= 0x2300 && c <= 0x23FF) ||
                c == 0x2934 || c == 0x2935 || (c >= 0x2B00 && c <= 0x2BFF) ||
                c == 0x3030 || c == 0x303D || c == 0x3297 || c == 0x3299)
                return true;
        }
        return false;
    }

    private static SKTypeface? GetChineseFallbackTypeface()
    {
        if (_cachedChineseTypeface != null && !_isChineseTypefaceDisposed)
        {
            try
            {
                return _cachedChineseTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isChineseTypefaceDisposed = true;
                _cachedChineseTypeface = null;
            }
        }
        
        var families = GetFontFamilies();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong", "YouYuan", "STSong", "PingFang SC", "Noto Sans SC", "Source Han Sans SC" };
        
        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    _cachedChineseTypeface = tf;
                    _isChineseTypefaceDisposed = false;
                    return tf;
                }
            }
        }
        
        _cachedChineseTypeface = SKTypeface.Default;
        _isChineseTypefaceDisposed = false;
        return _cachedChineseTypeface!;
    }
}

public class DrawImageOp : PaintOp
{
    public SKImage? Image { get; set; }
    public SKRect SourceRect { get; set; }
    public SKRect DestRect { get; set; }
    public ImageFit Fit { get; set; } = ImageFit.Fill;

    public override void Reset()
    {
        base.Reset();
        Image = null;
        SourceRect = DestRect = default;
        Fit = ImageFit.Fill;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (Image == null) return;

        SKRect dest;
        switch (Fit)
        {
            case ImageFit.Fill:
                dest = DestRect;
                break;
            case ImageFit.Cover:
                dest = CalculateCoverRect(Image.Width, Image.Height, DestRect);
                break;
            case ImageFit.Contain:
                dest = CalculateContainRect(Image.Width, Image.Height, DestRect);
                break;
            case ImageFit.ScaleDown:
                var containDest = CalculateContainRect(Image.Width, Image.Height, DestRect);
                var noneDest = CalculateNoneRect(Image.Width, Image.Height, DestRect);
                dest = containDest.Width * containDest.Height < noneDest.Width * noneDest.Height ? containDest : noneDest;
                break;
            default:
                dest = CalculateNoneRect(Image.Width, Image.Height, DestRect);
                break;
        }

        canvas.DrawImage(Image, SourceRect, dest);
    }

    private SKRect CalculateNoneRect(float srcW, float srcH, SKRect dest)
    {
        float w = Math.Min(srcW, dest.Width);
        float h = Math.Min(srcH, dest.Height);
        float x = dest.Left + (dest.Width - w) / 2;
        float y = dest.Top + (dest.Height - h) / 2;
        return new SKRect(x, y, x + w, y + h);
    }

    private SKRect CalculateCoverRect(float srcW, float srcH, SKRect dest)
    {
        float srcRatio = srcW / srcH;
        float destRatio = dest.Width / dest.Height;

        if (srcRatio > destRatio)
        {
            float newWidth = srcH * destRatio;
            float offset = (srcW - newWidth) / 2;
            return new SKRect(offset, 0, offset + newWidth, srcH);
        }
        else
        {
            float newHeight = srcW / destRatio;
            float offset = (srcH - newHeight) / 2;
            return new SKRect(0, offset, srcW, offset + newHeight);
        }
    }

    private SKRect CalculateContainRect(float srcW, float srcH, SKRect dest)
    {
        float srcRatio = srcW / srcH;
        float destRatio = dest.Width / dest.Height;

        if (srcRatio > destRatio)
        {
            float height = dest.Width / srcRatio;
            float y = dest.MidY - height / 2;
            return new SKRect(dest.Left, y, dest.Right, y + height);
        }
        else
        {
            float width = dest.Height * srcRatio;
            float x = dest.MidX - width / 2;
            return new SKRect(x, dest.Top, x + width, dest.Bottom);
        }
    }
}

public class DrawLineOp : PaintOp
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float StrokeWidth { get; set; } = 1;
    public SKColor Color { get; set; }

    public override void Reset()
    {
        base.Reset();
        X1 = Y1 = X2 = Y2 = 0;
        StrokeWidth = 1;
        Color = default;
    }

    public override void Execute(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokeWidth,
            IsAntialias = true
        };
        canvas.DrawLine(X1, Y1, X2, Y2, paint);
    }
}

public class DrawPathOp : PaintOp
{
    public SKPath Path { get; set; } = new();
    public SKPaint? FillPaint { get; set; }
    public SKPaint? StrokePaint { get; set; }

    public override void Reset()
    {
        base.Reset();
        Path.Dispose();
        Path = new();
        FillPaint?.Dispose();
        FillPaint = null;
        StrokePaint?.Dispose();
        StrokePaint = null;
    }

    public override void Execute(SKCanvas canvas)
    {
        if (FillPaint != null)
            canvas.DrawPath(Path, FillPaint);
        if (StrokePaint != null)
            canvas.DrawPath(Path, StrokePaint);
    }
}

public class PushLayerOp : PaintOp
{
    public float Opacity { get; set; } = 1.0f;
    public SKRect ClipRect { get; set; }
    public bool HasClipRect { get; set; }
    public SKPath? ClipPath { get; set; }
    public SKImageFilter? ImageFilter { get; set; }

    public override void Reset()
    {
        base.Reset();
        Opacity = 1.0f;
        ClipRect = default;
        HasClipRect = false;
        ClipPath = null;
        ImageFilter = null;
    }

    public override void Execute(SKCanvas canvas)
    {
        var paint = new SKPaint();
        paint.Color = SKColors.Black;
        if (Opacity < 1.0f)
            paint.Color = paint.Color.WithAlpha((byte)(Opacity * 255));
        if (ImageFilter != null)
            paint.ImageFilter = ImageFilter;

        canvas.SaveLayer(paint);

        if (ClipPath != null)
            canvas.ClipPath(ClipPath, SKClipOperation.Intersect, true);
        else if (HasClipRect && ClipRect.Width > 0 && ClipRect.Height > 0)
            canvas.ClipRect(ClipRect, SKClipOperation.Intersect, true);
    }
}

public class PopLayerOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas) { canvas.Restore(); }
}

public class PushClipOp : PaintOp
{
    public SKRect ClipRect { get; set; }
    public SKPath? ClipPath { get; set; }
    public bool AntiAlias { get; set; } = true;

    public override void Reset()
    {
        base.Reset();
        ClipRect = default;
        ClipPath = null;
        AntiAlias = true;
    }

    public override void Execute(SKCanvas canvas)
    {
        canvas.Save();
        if (ClipPath != null)
            canvas.ClipPath(ClipPath, SKClipOperation.Intersect, AntiAlias);
        else
            canvas.ClipRect(ClipRect, SKClipOperation.Intersect, AntiAlias);
    }
}

public class PopClipOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas)
    {
        canvas.Restore();
    }
}

public class PushTransformOp : PaintOp
{
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;

    public override void Reset()
    {
        base.Reset();
        Matrix = SKMatrix.Identity;
    }

    public override void Execute(SKCanvas canvas)
    {
        canvas.Save();
        var m = Matrix;
        canvas.Concat(ref m);
    }
}

public class PopTransformOp : PaintOp
{
    public override void Reset() { base.Reset(); }
    public override void Execute(SKCanvas canvas)
    {
        canvas.Restore();
    }
}

public class DrawShadowOp : PaintOp
{
    public SKPath Path { get; set; } = new();
    public SKColor Color { get; set; }
    public float BlurRadius { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool Inset { get; set; }

    public override void Reset()
    {
        base.Reset();
        Path.Dispose();
        Path = new();
        Color = default;
        BlurRadius = OffsetX = OffsetY = 0;
        Inset = false;
    }

    private static readonly Dictionary<float, SKImageFilter> _blurCache = new();

    private static SKImageFilter GetOrCreateBlur(float radius)
    {
        if (radius <= 0) return null!;
        lock (_blurCache)
        {
            if (!_blurCache.TryGetValue(radius, out var filter))
            {
                filter = SKImageFilter.CreateBlur(radius, radius);
                _blurCache[radius] = filter;
            }
            return filter;
        }
    }

    public override void Execute(SKCanvas canvas)
    {
        var blurFilter = GetOrCreateBlur(BlurRadius);
        using var paint = new SKPaint
        {
            Color = Color.WithAlpha(80),
            IsAntialias = true,
            ImageFilter = blurFilter
        };

        if (Inset)
        {
            canvas.Save();
            canvas.Translate(OffsetX, OffsetY);
            canvas.DrawPath(Path, paint);
            canvas.Restore();
            return;
        }

        canvas.Save();
        canvas.Translate(OffsetX, OffsetY);
        canvas.DrawPath(Path, paint);
        canvas.Restore();
    }
}

public enum ImageFit { Fill, Contain, Cover, None, ScaleDown }

public static class PaintOpPool
{
    private const int MaxPoolSize = 500;
    private static readonly ConcurrentStack<DrawRectOp> _rectOps = new();
    private static readonly ConcurrentStack<DrawTextOp> _textOps = new();
    private static readonly ConcurrentStack<DrawImageOp> _imageOps = new();
    private static readonly ConcurrentStack<DrawLineOp> _lineOps = new();
    private static readonly ConcurrentStack<DrawPathOp> _pathOps = new();
    private static readonly ConcurrentStack<PushClipOp> _clipOps = new();
    private static readonly ConcurrentStack<PopClipOp> _popClipOps = new();
    private static readonly ConcurrentStack<PushTransformOp> _transformOps = new();
    private static readonly ConcurrentStack<PopTransformOp> _popTransformOps = new();
    private static readonly ConcurrentStack<DrawShadowOp> _shadowOps = new();
    private static readonly ConcurrentStack<PushLayerOp> _layerOps = new();
    private static readonly ConcurrentStack<PopLayerOp> _popLayerOps = new();

    public static DrawRectOp GetDrawRectOp() => _rectOps.TryPop(out var op) ? op : new DrawRectOp();
    public static DrawTextOp GetDrawTextOp() => _textOps.TryPop(out var op) ? op : new DrawTextOp();
    public static DrawImageOp GetDrawImageOp() => _imageOps.TryPop(out var op) ? op : new DrawImageOp();
    public static DrawLineOp GetDrawLineOp() => _lineOps.TryPop(out var op) ? op : new DrawLineOp();
    public static DrawPathOp GetDrawPathOp() => _pathOps.TryPop(out var op) ? op : new DrawPathOp();
    public static PushClipOp GetPushClipOp() => _clipOps.TryPop(out var op) ? op : new PushClipOp();
    public static PopClipOp GetPopClipOp() => _popClipOps.TryPop(out var op) ? op : new PopClipOp();
    public static PushTransformOp GetPushTransformOp() => _transformOps.TryPop(out var op) ? op : new PushTransformOp();
    public static PopTransformOp GetPopTransformOp() => _popTransformOps.TryPop(out var op) ? op : new PopTransformOp();
    public static DrawShadowOp GetDrawShadowOp() => _shadowOps.TryPop(out var op) ? op : new DrawShadowOp();
    public static PushLayerOp GetPushLayerOp() => _layerOps.TryPop(out var op) ? op : new PushLayerOp();
    public static PopLayerOp GetPopLayerOp() => _popLayerOps.TryPop(out var op) ? op : new PopLayerOp();

    public static void Return(DrawRectOp op) { op.Reset(); if (_rectOps.Count < MaxPoolSize) _rectOps.Push(op); }
    public static void Return(DrawTextOp op) { op.Reset(); if (_textOps.Count < MaxPoolSize) _textOps.Push(op); }
    public static void Return(DrawImageOp op) { op.Reset(); if (_imageOps.Count < MaxPoolSize) _imageOps.Push(op); }
    public static void Return(DrawLineOp op) { op.Reset(); if (_lineOps.Count < MaxPoolSize) _lineOps.Push(op); }
    public static void Return(DrawPathOp op) { op.Reset(); if (_pathOps.Count < MaxPoolSize) _pathOps.Push(op); }
    public static void Return(PushClipOp op) { op.Reset(); if (_clipOps.Count < MaxPoolSize) _clipOps.Push(op); }
    public static void Return(PopClipOp op) { op.Reset(); if (_popClipOps.Count < MaxPoolSize) _popClipOps.Push(op); }
    public static void Return(PushTransformOp op) { op.Reset(); if (_transformOps.Count < MaxPoolSize) _transformOps.Push(op); }
    public static void Return(PopTransformOp op) { op.Reset(); if (_popTransformOps.Count < MaxPoolSize) _popTransformOps.Push(op); }
    public static void Return(DrawShadowOp op) { op.Reset(); if (_shadowOps.Count < MaxPoolSize) _shadowOps.Push(op); }
    public static void Return(PushLayerOp op) { op.Reset(); if (_layerOps.Count < MaxPoolSize) _layerOps.Push(op); }
    public static void Return(PopLayerOp op) { op.Reset(); if (_popLayerOps.Count < MaxPoolSize) _popLayerOps.Push(op); }

    public static void ReturnOp(PaintOp op)
    {
        switch (op)
        {
            case DrawRectOp o: Return(o); break;
            case DrawTextOp o: Return(o); break;
            case DrawImageOp o: Return(o); break;
            case DrawLineOp o: Return(o); break;
            case DrawPathOp o: Return(o); break;
            case PushClipOp o: Return(o); break;
            case PopClipOp o: Return(o); break;
            case PushTransformOp o: Return(o); break;
            case PopTransformOp o: Return(o); break;
            case DrawShadowOp o: Return(o); break;
            case PushLayerOp o: Return(o); break;
            case PopLayerOp o: Return(o); break;
        }
    }

    public static void Clear()
    {
        _rectOps.Clear();
        _textOps.Clear();
        _imageOps.Clear();
        _lineOps.Clear();
        _pathOps.Clear();
        _clipOps.Clear();
        _popClipOps.Clear();
        _transformOps.Clear();
        _popTransformOps.Clear();
        _shadowOps.Clear();
        _layerOps.Clear();
        _popLayerOps.Clear();
    }
}

/// <summary>
/// Spatial grid index for fast region-based paint op lookups.
/// </summary>
public class SpatialGrid
{
    private readonly float _cellSize;
    private readonly Dictionary<(int, int), List<PaintOp>> _grid = new();
    private int _opCount;

    public SpatialGrid(float cellSize = 100)
    {
        _cellSize = cellSize;
    }

    public void Build(IReadOnlyList<PaintOp> ops)
    {
        _grid.Clear();
        _opCount = ops.Count;

        foreach (var op in ops)
        {
            var cells = GetCellsForRect(op.Bounds);
            foreach (var cell in cells)
            {
                if (!_grid.TryGetValue(cell, out var list))
                {
                    list = new List<PaintOp>();
                    _grid[cell] = list;
                }
                list.Add(op);
            }
        }
    }

    public IEnumerable<PaintOp> GetOpsInRect(SKRect rect)
    {
        if (_opCount == 0) yield break;

        var seen = new HashSet<PaintOp>();
        var cells = GetCellsForRect(rect);
        foreach (var cell in cells)
        {
            if (_grid.TryGetValue(cell, out var ops))
            {
                foreach (var op in ops)
                {
                    if (seen.Add(op) && op.Bounds.IntersectsWith(rect))
                        yield return op;
                }
            }
        }
    }

    public void Clear()
    {
        _grid.Clear();
        _opCount = 0;
    }

    private List<(int, int)> GetCellsForRect(SKRect rect)
    {
        var cells = new List<(int, int)>(4);
        int minX = (int)(rect.Left / _cellSize);
        int maxX = (int)(rect.Right / _cellSize);
        int minY = (int)(rect.Top / _cellSize);
        int maxY = (int)(rect.Bottom / _cellSize);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                cells.Add((x, y));
        return cells;
    }
}

public class DisplayList
{
    private readonly object _lock = new();
    private List<PaintOp> _ops = new();
    private bool _isSorted;
    private SpatialGrid? _spatialGrid;

    public SpatialGrid? SpatialGrid => _spatialGrid;

    public void Add(PaintOp op) { lock (_lock) _ops.Add(op); }

    public void AddRange(IEnumerable<PaintOp> ops) { lock (_lock) _ops.AddRange(ops); }

    public void Clear()
    {
        List<PaintOp> oldOps;
        lock (_lock)
        {
            oldOps = _ops;
            _ops = new List<PaintOp>();
            _isSorted = false;
        }
        for (int i = 0; i < oldOps.Count; i++)
        {
            PaintOpPool.ReturnOp(oldOps[i]);
        }
        _spatialGrid?.Clear();
    }

    public void SortByZIndex()
    {
        lock (_lock)
        {
            if (_isSorted) return;
            // Use stable sort (OrderBy) so equal-ZIndex ops keep their original order.
            // List<T>.Sort is unstable and can reorder background/foreground ops.
            _ops = _ops.OrderBy(op => op.ZIndex).ToList();
            _isSorted = true;
        }
    }

    public void Execute(SKCanvas canvas)
    {
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        for (int i = 0; i < snapshot.Count; i++)
        {
            try
            {
                snapshot[i].AlignBounds(canvas);
            }
            catch
            {
                // ignore alignment errors
            }
            snapshot[i].Execute(canvas);
        }
    }

    public IEnumerable<PaintOp> GetOpsInRect(SKRect rect)
    {
        if (_spatialGrid != null && _ops.Count > 100)
        {
            foreach (var op in _spatialGrid.GetOpsInRect(rect))
                yield return op;
            yield break;
        }
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Bounds.IntersectsWith(rect))
                yield return snapshot[i];
        }
    }

    public void BuildSpatialGrid()
    {
        List<PaintOp> snapshot;
        lock (_lock) snapshot = new List<PaintOp>(_ops);
        if (snapshot.Count <= 100) return;
        _spatialGrid ??= new SpatialGrid();
        _spatialGrid.Build(snapshot);
    }

    public int Count { get { lock (_lock) return _ops.Count; } }

    public PaintOp? this[int index] { get { lock (_lock) return index >= 0 && index < _ops.Count ? _ops[index] : null; } }
}
