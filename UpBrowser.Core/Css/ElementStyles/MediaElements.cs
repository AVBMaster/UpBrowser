using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class MediaElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 图像
            case "img":
                style.Display = DisplayType.InlineBlock;
                style.VerticalAlign = VerticalAlignType.Bottom;
                break;

            case "picture":
                style.Display = DisplayType.InlineBlock;
                break;

            // 图形和绘图
            case "canvas":
                style.Display = DisplayType.InlineBlock;
                style.VerticalAlign = VerticalAlignType.Bottom;
                break;

            case "svg":
                style.Display = DisplayType.InlineBlock;
                style.VerticalAlign = VerticalAlignType.Bottom;
                break;

            // 视频和音频
            case "video":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.BackgroundColor = SKColors.Black;
                style.Width = new PixelLength(300);
                style.Height = new PixelLength(150);
                break;

            case "audio":
                style.Display = DisplayType.InlineBlock;
                style.Width = new PixelLength(300);
                style.Height = new PixelLength(30);
                break;

            // 媒体源
            case "source":
                style.Display = DisplayType.None;
                break;

            case "track":
                style.Display = DisplayType.None;
                break;

            // 嵌入内容
            case "iframe":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.Width = new PixelLength(300);
                style.Height = new PixelLength(150);
                break;

            case "embed":
                style.Display = DisplayType.InlineBlock;
                style.Width = new PixelLength(300);
                style.Height = new PixelLength(150);
                break;

            case "object":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.Width = new PixelLength(300);
                style.Height = new PixelLength(150);
                break;

            case "param":
                style.Display = DisplayType.None;
                break;

            // 图像映射
            case "map":
                style.Display = DisplayType.Inline;
                break;

            case "area":
                style.Display = DisplayType.None;
                style.Cursor = "pointer";
                break;
        }
    }
}
