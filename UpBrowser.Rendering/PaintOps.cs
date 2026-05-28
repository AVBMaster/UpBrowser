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
    public float BorderRadius { get; set; }

    public override void Reset()
    {
        base.Reset();
        Rect = default;
        FillColor = default;
        BorderTopWidth = BorderRightWidth = BorderBottomWidth = BorderLeftWidth = 0;
        BorderTopColor = BorderRightColor = BorderBottomColor = BorderLeftColor = default;
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
            {
                using var paint = new SKPaint
                {
                    Color = BorderTopColor,
                    Style = SKPaintStyle.Fill
                };
                var r = new SKRect(alignedRect.Left, alignedRect.Top, alignedRect.Right, alignedRect.Bottom);
                canvas.DrawRect(r.Left, r.Top, r.Width, BorderTopWidth, paint);
            }

            if (BorderBottomWidth > 0)
            {
                using var paint = new SKPaint
                {
                    Color = BorderBottomColor,
                    Style = SKPaintStyle.Fill
                };
                var r2 = new SKRect(alignedRect.Left, alignedRect.Top, alignedRect.Right, alignedRect.Bottom);
                canvas.DrawRect(r2.Left, r2.Bottom - BorderBottomWidth, r2.Width, BorderBottomWidth, paint);
            }

            if (BorderLeftWidth > 0)
            {
                using var paint = new SKPaint
                {
                    Color = BorderLeftColor,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(alignedRect.Left, alignedRect.Top, BorderLeftWidth, alignedRect.Height, paint);
            }

            if (BorderRightWidth > 0)
            {
                using var paint = new SKPaint
                {
                    Color = BorderRightColor,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(alignedRect.Right - BorderRightWidth, alignedRect.Top, BorderRightWidth, alignedRect.Height, paint);
            }
        }
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
        Underline = LineThrough = false;
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
            using var underlinePaint = new SKPaint
            {
                Color = Color,
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawLine(drawX, underlineY, drawX + actualWidth, underlineY, underlinePaint);
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
    }

    private float MeasureTextWithFallback(SKCanvas canvas, SKPaint paint, float x, float y, bool dryRun)
    {
        if (string.IsNullOrEmpty(Text)) return 0;

        var text = Text;
        int len = text.Length;
        float currentX = x;

        // Initialize with first character's typeface so the first run starts at index 0
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
                    // Flush current run: draw text[runStart..i] with currentTypeface
                    string run = text[runStart..i];
                    using var font = new SKFont(currentTypeface, FontSize)
                    {
                        Edging = SKFontEdging.SubpixelAntialias,
                        Subpixel = true,
                        Hinting = SKFontHinting.Normal
                    };
                    float runWidth = font.MeasureText(run);
                    if (!dryRun)
                        canvas.DrawText(run, currentX, y, font, paint);
                    currentX += runWidth;

                    currentTypeface = neededTypeface;
                    runStart = i;
                }
            }
            else
            {
                // End of string: flush remaining run
                string run = text[runStart..i];
                using var font = new SKFont(currentTypeface, FontSize)
                {
                    Edging = SKFontEdging.SubpixelAntialias,
                    Subpixel = true,
                    Hinting = SKFontHinting.Normal
                };
                float runWidth = font.MeasureText(run);
                if (!dryRun)
                    canvas.DrawText(run, currentX, y, font, paint);
                currentX += runWidth;
            }
        }

        return currentX - x;
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
                var style = SKFontManager.Default.GetFontStyles(index);
                var tf = style.CreateTypeface(0);
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

        var dest = Fit switch
        {
            ImageFit.Cover => CalculateCoverRect(Image.Width, Image.Height, DestRect),
            ImageFit.Contain => CalculateContainRect(Image.Width, Image.Height, DestRect),
            _ => DestRect
        };

        canvas.DrawImage(Image, SourceRect, dest);
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
        canvas.SetMatrix(Matrix);
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

    public override void Reset()
    {
        base.Reset();
        Path.Dispose();
        Path = new();
        Color = default;
        BlurRadius = OffsetX = OffsetY = 0;
    }

    public override void Execute(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = Color.WithAlpha(80),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(BlurRadius, BlurRadius)
        };

        canvas.Save();
        canvas.Translate(OffsetX, OffsetY);
        canvas.DrawPath(Path, paint);
        canvas.Restore();
    }
}

public enum ImageFit { Fill, Contain, Cover, None }

public static class PaintOpPool
{
    private const int MaxPoolSize = 500;
    private static readonly Stack<DrawRectOp> _rectOps = new();
    private static readonly Stack<DrawTextOp> _textOps = new();
    private static readonly Stack<DrawImageOp> _imageOps = new();
    private static readonly Stack<DrawLineOp> _lineOps = new();
    private static readonly Stack<DrawPathOp> _pathOps = new();
    private static readonly Stack<PushClipOp> _clipOps = new();
    private static readonly Stack<PopClipOp> _popClipOps = new();
    private static readonly Stack<PushTransformOp> _transformOps = new();
    private static readonly Stack<PopTransformOp> _popTransformOps = new();
    private static readonly Stack<DrawShadowOp> _shadowOps = new();

    public static DrawRectOp GetDrawRectOp() => _rectOps.Count > 0 ? _rectOps.Pop() : new DrawRectOp();
    public static DrawTextOp GetDrawTextOp() => _textOps.Count > 0 ? _textOps.Pop() : new DrawTextOp();
    public static DrawImageOp GetDrawImageOp() => _imageOps.Count > 0 ? _imageOps.Pop() : new DrawImageOp();
    public static DrawLineOp GetDrawLineOp() => _lineOps.Count > 0 ? _lineOps.Pop() : new DrawLineOp();
    public static DrawPathOp GetDrawPathOp() => _pathOps.Count > 0 ? _pathOps.Pop() : new DrawPathOp();
    public static PushClipOp GetPushClipOp() => _clipOps.Count > 0 ? _clipOps.Pop() : new PushClipOp();
    public static PopClipOp GetPopClipOp() => _popClipOps.Count > 0 ? _popClipOps.Pop() : new PopClipOp();
    public static PushTransformOp GetPushTransformOp() => _transformOps.Count > 0 ? _transformOps.Pop() : new PushTransformOp();
    public static PopTransformOp GetPopTransformOp() => _popTransformOps.Count > 0 ? _popTransformOps.Pop() : new PopTransformOp();
    public static DrawShadowOp GetDrawShadowOp() => _shadowOps.Count > 0 ? _shadowOps.Pop() : new DrawShadowOp();

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
    private List<PaintOp> _ops = new();
    private bool _isSorted;
    private SpatialGrid? _spatialGrid;

    public SpatialGrid? SpatialGrid => _spatialGrid;

    public void Add(PaintOp op) => _ops.Add(op);

    public void AddRange(IEnumerable<PaintOp> ops) => _ops.AddRange(ops);

    public void Clear()
    {
        for (int i = 0; i < _ops.Count; i++)
        {
            PaintOpPool.ReturnOp(_ops[i]);
        }
        _ops.Clear();
        _isSorted = false;
        _spatialGrid?.Clear();
    }

    public void SortByZIndex()
    {
        if (_isSorted) return;
        // Use stable sort (OrderBy) so equal-ZIndex ops keep their original order.
        // List<T>.Sort is unstable and can reorder background/foreground ops.
        _ops = _ops.OrderBy(op => op.ZIndex).ToList();
        _isSorted = true;
    }

    public void Execute(SKCanvas canvas)
    {
        for (int i = 0; i < _ops.Count; i++)
        {
            try
            {
                _ops[i].AlignBounds(canvas);
            }
            catch
            {
                // ignore alignment errors
            }
            _ops[i].Execute(canvas);
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
        for (int i = 0; i < _ops.Count; i++)
        {
            if (_ops[i].Bounds.IntersectsWith(rect))
                yield return _ops[i];
        }
    }

    public void BuildSpatialGrid()
    {
        if (_ops.Count <= 100) return;
        _spatialGrid ??= new SpatialGrid();
        _spatialGrid.Build(_ops);
    }

    public int Count => _ops.Count;

    public PaintOp? this[int index] => index >= 0 && index < _ops.Count ? _ops[index] : null;
}
