using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class RootElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 根元素
            case "html":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                style.MarginLeft = new PixelLength(0);
                style.MarginRight = new PixelLength(0);
                style.FontSize = 16;
                style.LineHeight = 1.2f;
                style.BackgroundColor = SKColors.White;
                break;

            case "body":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                style.MarginLeft = new PixelLength(8);
                style.MarginRight = new PixelLength(8);
                style.FontSize = 16;
                style.LineHeight = 1.2f;
                style.Color = SKColors.Black;
                style.BackgroundColor = SKColors.White;
                style.FontFamily = "Arial, sans-serif";
                break;

            // 元数据元素 - 不显示
            case "head":
                style.Display = DisplayType.None;
                break;

            case "title":
                style.Display = DisplayType.None;
                break;

            case "base":
                style.Display = DisplayType.None;
                break;

            case "link":
                style.Display = DisplayType.None;
                break;

            case "meta":
                style.Display = DisplayType.None;
                break;

            case "style":
                style.Display = DisplayType.None;
                break;

            case "script":
                style.Display = DisplayType.None;
                break;

            // 其他根级元素
            case "noscript":
                style.Display = DisplayType.Block;
                break;

            case "template":
                style.Display = DisplayType.None;
                break;

            // DOCTYPE 声明（虚拟元素）
            case "doctype":
                style.Display = DisplayType.None;
                break;
        }
    }
}
