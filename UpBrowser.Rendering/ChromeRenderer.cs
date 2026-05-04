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

        canvas.DrawText("Ready", 5, statusTop + 14, _textPaint);
    }

    public float GetContentOffset() => TabBarHeight + ToolbarHeight;
    public float GetStatusBarHeight() => StatusBarHeight;

    public void RenderScrollbars(SKCanvas canvas, float width, float height, ScrollManager scrollManager)
    {
        float contentTop = GetContentOffset();
        float viewportHeight = scrollManager.ViewportHeight;
        float viewportWidth = scrollManager.ViewportWidth;

        // 垂直滚动条 - 参考 ScrollViewer.ConfigureScrollBar 的逻辑
        if (scrollManager.CanScrollY && viewportHeight > 0)
        {
            float scrollbarLeft = width - ScrollManager.ScrollbarWidth;
            float trackTop = contentTop;
            float trackHeight = viewportHeight;
            float contentHeight = scrollManager.ContentHeight;
            
            if (contentHeight <= viewportHeight)
                return; // 不需要滚动条

            // 轨道背景
            using var trackPaint = new SKPaint { Color = new SKColor(240, 240, 240), Style = SKPaintStyle.Fill };
            canvas.DrawRect(scrollbarLeft, trackTop, ScrollManager.ScrollbarWidth, trackHeight, trackPaint);

            // 滑块大小：轨道高度 * (视口高度 / 内容高度)
            // 参考 ScrollViewer: thumbSize = (ViewportSize / Maximum) * trackSize
            float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize, 
                trackHeight * viewportHeight / contentHeight);
            
            // 滑块位置：当前滚动偏移 / 最大滚动偏移 * (轨道高度 - 滑块高度)
            float maxScrollY = contentHeight - viewportHeight;
            float thumbTop = trackTop;
            if (maxScrollY > 0)
            {
                thumbTop += (scrollManager.ScrollY / maxScrollY) * (trackHeight - thumbHeight);
            }

            // 绘制滑块
            var thumbRect = new SKRect(scrollbarLeft + 2, thumbTop, 
                scrollbarLeft + ScrollManager.ScrollbarWidth - 2, thumbTop + thumbHeight);
            using var thumbPaint = new SKPaint 
            { 
                Color = SKColors.Gray, 
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(thumbRect, 4, 4, thumbPaint);
        }

        // 水平滚动条
        if (scrollManager.CanScrollX && viewportWidth > 0)
        {
            float scrollbarTop = height - StatusBarHeight - ScrollManager.ScrollbarWidth;
            float trackLeft = 0;
            float trackWidth = viewportWidth;
            float contentWidth = scrollManager.ContentWidth;
            
            if (contentWidth <= viewportWidth)
                return;

            using var trackPaint = new SKPaint { Color = new SKColor(240, 240, 240), Style = SKPaintStyle.Fill };
            canvas.DrawRect(trackLeft, scrollbarTop, trackWidth, ScrollManager.ScrollbarWidth, trackPaint);

            float thumbWidth = Math.Max(ScrollManager.ScrollbarMinThumbSize, 
                trackWidth * viewportWidth / contentWidth);
            
            float maxScrollX = contentWidth - viewportWidth;
            float thumbLeft = trackLeft;
            if (maxScrollX > 0)
            {
                thumbLeft += (scrollManager.ScrollX / maxScrollX) * (trackWidth - thumbWidth);
            }

            var thumbRect = new SKRect(thumbLeft, scrollbarTop + 2, 
                thumbLeft + thumbWidth, scrollbarTop + ScrollManager.ScrollbarWidth - 2);
            using var thumbPaint = new SKPaint 
            { 
                Color = SKColors.Gray, 
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(thumbRect, 4, 4, thumbPaint);
        }
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
        _buttonPaint.Dispose();
        _urlBarPaint.Dispose();
    }
}