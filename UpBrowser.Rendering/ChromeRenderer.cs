using SkiaSharp;

namespace UpBrowser.Rendering;

public class ChromeRenderer
{
    private const float UrlBarHeight = 30;
    private const float TabBarHeight = 35;
    private const float ToolbarHeight = 40;
    private const float StatusBarHeight = 20;
    private const float BorderRadius = 4;

    private SKPaint _backgroundPaint = null!;
    private SKPaint _borderPaint = null!;
    private SKPaint _textPaint = null!;
    private SKPaint _buttonPaint = null!;
    private SKPaint _urlBarPaint = null!;
    private SKTypeface? _chineseTypeface;

    private SKTypeface? GetChineseTypeface()
    {
        var fontFamilies = SKFontManager.Default.FontFamilies.ToArray();
        
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "FangSong", "KaiTi", "Segoe UI" };
        
        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(fontFamilies, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                {
                    return tf;
                }
            }
        }
        
        return null;
    }

    public void Initialize()
    {
        _chineseTypeface = GetChineseTypeface();

        _backgroundPaint = new SKPaint
        {
            Color = SKColor.Parse("#f0f0f0"),
            Style = SKPaintStyle.Fill
        };

        _borderPaint = new SKPaint
        {
            Color = SKColor.Parse("#ccc"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };

        _textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 13,
            IsAntialias = true,
            Typeface = _chineseTypeface
        };

        _buttonPaint = new SKPaint
        {
            Color = SKColor.Parse("#e0e0e0"),
            Style = SKPaintStyle.Fill
        };

        _urlBarPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill
        };
    }

    public void RenderChrome(SKCanvas canvas, float width, float height, string url, string title)
    {
        RenderTabBar(canvas, width, title);
        RenderToolbar(canvas, width, url);
        RenderStatusBar(canvas, width, height);
    }

    private void RenderTabBar(SKCanvas canvas, float width, string title)
    {
        canvas.DrawRect(0, 0, width, TabBarHeight, _backgroundPaint);

        using var tabPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(5, 5, 150, TabBarHeight - 5, BorderRadius, BorderRadius, tabPaint);

        canvas.DrawText(title.Length > 15 ? title[..15] + "..." : title, 15, 25, _textPaint);

        using var closePaint = new SKPaint 
        { 
            Color = SKColor.Parse("#666"), 
            TextSize = 14,
            Typeface = _chineseTypeface
        };
        canvas.DrawText("x", 135, 26, closePaint);
    }

    private void RenderToolbar(SKCanvas canvas, float width, string url)
    {
        float toolbarTop = TabBarHeight;

        canvas.DrawRect(0, toolbarTop, width, ToolbarHeight, _backgroundPaint);

        using var backPaint = new SKPaint 
        { 
            Color = SKColor.Parse("#888"), 
            TextSize = 18,
            Typeface = _chineseTypeface
        };
        canvas.DrawText("<", 10, toolbarTop + 25, backPaint);
        canvas.DrawText(">", 30, toolbarTop + 25, backPaint);
        canvas.DrawText("R", 50, toolbarTop + 25, backPaint);

        float urlBarLeft = 80;
        float urlBarWidth = width - urlBarLeft - 10;
        canvas.DrawRoundRect(urlBarLeft, toolbarTop + 5, urlBarWidth, ToolbarHeight - 10, BorderRadius, BorderRadius, _urlBarPaint);
        canvas.DrawRoundRect(urlBarLeft, toolbarTop + 5, urlBarWidth, ToolbarHeight - 10, BorderRadius, BorderRadius, _borderPaint);

        canvas.DrawText(url, urlBarLeft + 5, toolbarTop + 22, _textPaint);
    }

    private void RenderStatusBar(SKCanvas canvas, float width, float height)
    {
        float statusTop = height - StatusBarHeight;

        canvas.DrawRect(0, statusTop, width, StatusBarHeight, _backgroundPaint);
        canvas.DrawLine(0, statusTop, width, statusTop, _borderPaint);

        canvas.DrawText("¼ÓÔØ³É¹¦", 5, statusTop + 14, _textPaint);
    }

    public float GetContentOffset() => TabBarHeight + ToolbarHeight;
    public float GetStatusBarHeight() => StatusBarHeight;

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
        _buttonPaint.Dispose();
        _urlBarPaint.Dispose();
    }
}