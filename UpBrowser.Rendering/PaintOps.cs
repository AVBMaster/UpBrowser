using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public abstract class PaintOp
{
    public SKRect Bounds { get; set; }
    public int ZIndex { get; set; }
    public abstract void Execute(SKCanvas canvas);
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

    public override void Execute(SKCanvas canvas)
    {
        if (FillColor.Alpha > 0)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = BorderRadius > 0
            };

            if (BorderRadius > 0)
            {
                var path = new SKPath();
                path.AddRoundRect(Rect, BorderRadius, BorderRadius);
                canvas.DrawPath(path, paint);
            }
            else
            {
                canvas.DrawRect(Rect, paint);
            }
        }

        if (BorderTopWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = BorderTopColor,
                Style = SKPaintStyle.Fill,
                StrokeWidth = BorderTopWidth
            };
            canvas.DrawRect(Rect.Left, Rect.Top, Rect.Width, BorderTopWidth, paint);
        }

        if (BorderBottomWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = BorderBottomColor,
                Style = SKPaintStyle.Fill,
                StrokeWidth = BorderBottomWidth
            };
            canvas.DrawRect(Rect.Left, Rect.Bottom - BorderBottomWidth, Rect.Width, BorderBottomWidth, paint);
        }

        if (BorderLeftWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = BorderLeftColor,
                Style = SKPaintStyle.Fill,
                StrokeWidth = BorderLeftWidth
            };
            canvas.DrawRect(Rect.Left, Rect.Top, BorderLeftWidth, Rect.Height, paint);
        }

        if (BorderRightWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = BorderRightColor,
                Style = SKPaintStyle.Fill,
                StrokeWidth = BorderRightWidth
            };
            canvas.DrawRect(Rect.Right - BorderRightWidth, Rect.Top, BorderRightWidth, Rect.Height, paint);
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

    public override void Execute(SKCanvas canvas)
    {
        if (string.IsNullOrEmpty(Text)) return;

        using var typeface = GetTypeface();
        using var font = new SKFont(typeface, FontSize);
        using var paint = new SKPaint
        {
            Color = Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        float x = X;
        if (TextAlign == TextAlignType.Center)
        {
            float width = paint.MeasureText(Text);
            x -= width / 2;
        }
        else if (TextAlign == TextAlignType.End || TextAlign == TextAlignType.Right)
        {
            float width = paint.MeasureText(Text);
            x -= width;
        }

        canvas.DrawText(Text, x, Y, font, paint);

        float textWidth = paint.MeasureText(Text);

        if (Underline)
        {
            float underlineY = Y + 2;
            using var underlinePaint = new SKPaint
            {
                Color = Color,
                StrokeWidth = 1,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawLine(x, underlineY, x + textWidth, underlineY, underlinePaint);
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
            canvas.DrawLine(x, strikeY, x + textWidth, strikeY, strikePaint);
        }
    }

    private static SKTypeface? _cachedChineseTypeface;
    private static bool _isChineseTypefaceDisposed = false;
    
    private static SKTypeface GetCachedChineseTypeface()
    {
        if (_cachedChineseTypeface != null && !_isChineseTypefaceDisposed)
        {
            try
            {
                // Try to use the cached typeface
                return _cachedChineseTypeface;
            }
            catch (ObjectDisposedException)
            {
                _isChineseTypefaceDisposed = true;
                _cachedChineseTypeface = null;
            }
        }
        
        var families = SKFontManager.Default.FontFamilies.ToArray();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong", "YouYuan", "STSong" };
        
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
        if (!string.IsNullOrEmpty(FontFamily))
        {
            var fontName = FontFamily.Split(',')[0].Trim();
            
            if (IsChineseString(fontName))
            {
                var chineseTf = TryGetChineseFont(fontName);
                if (chineseTf != null)
                    return chineseTf;
            }
            
            var families = SKFontManager.Default.FontFamilies.ToArray();
            var index = Array.IndexOf(families, fontName);
            if (index >= 0)
            {
                var style = SKFontManager.Default.GetFontStyles(index);
                var tf = style.CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    bool isSystemFont = tf.FamilyName == "Segoe UI" || tf.FamilyName == "Arial";
                    if (!isSystemFont)
                        return tf;
                }
            }
        }
        
        return GetCachedChineseTypeface();
    }
    
    private static bool IsChineseString(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                return true;
        }
        return false;
    }
    
    private static SKTypeface? TryGetChineseFont(string fontName)
    {
        var families = SKFontManager.Default.FontFamilies.ToArray();
        var index = Array.IndexOf(families, fontName);
        if (index >= 0)
        {
            return SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
        }
        
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "FangSong" };
        foreach (var cnFont in chineseFonts)
        {
            if (fontName.Contains(cnFont, StringComparison.OrdinalIgnoreCase) ||
                cnFont.Contains(fontName, StringComparison.OrdinalIgnoreCase))
            {
                index = Array.IndexOf(families, cnFont);
                if (index >= 0)
                {
                    return SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                }
            }
        }
        
        return null;
    }
}

public class DrawImageOp : PaintOp
{
    public SKImage? Image { get; set; }
    public SKRect SourceRect { get; set; }
    public SKRect DestRect { get; set; }
    public ImageFit Fit { get; set; } = ImageFit.Fill;

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
    public override void Execute(SKCanvas canvas)
    {
        canvas.Restore();
    }
}

public class PushTransformOp : PaintOp
{
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;

    public override void Execute(SKCanvas canvas)
    {
        canvas.Save();
        canvas.SetMatrix(Matrix);
    }
}

public class PopTransformOp : PaintOp
{
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

public class DisplayList
{
    private List<PaintOp> _ops = new();
    private bool _isSorted;

    public void Add(PaintOp op) => _ops.Add(op);

    public void AddRange(IEnumerable<PaintOp> ops) => _ops.AddRange(ops);

    public void Clear()
    {
        _ops.Clear();
        _isSorted = false;
    }

    public void SortByZIndex()
    {
        if (_isSorted) return;
        _ops = _ops.OrderBy(op => op.ZIndex).ToList();
        _isSorted = true;
    }

    public void Execute(SKCanvas canvas)
    {
        foreach (var op in _ops)
        {
            op.Execute(canvas);
        }
    }

    public IEnumerable<PaintOp> GetOpsInRect(SKRect rect)
    {
        return _ops.Where(op => op.Bounds.IntersectsWith(rect));
    }

    public int Count => _ops.Count;
}