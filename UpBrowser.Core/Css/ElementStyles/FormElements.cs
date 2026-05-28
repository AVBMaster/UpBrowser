using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class FormElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName.ToLowerInvariant())
        {
            case "input":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(6);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.Cursor = "text";
                style.BorderTopLeftRadius = 4;
                style.BorderTopRightRadius = 4;
                style.BorderBottomLeftRadius = 4;
                style.BorderBottomRightRadius = 4;
                break;

            case "textarea":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(6);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.Width = new PixelLength(200);
                style.Height = new PixelLength(100);
                style.Cursor = "text";
                style.BorderTopLeftRadius = 4;
                style.BorderTopRightRadius = 4;
                style.BorderBottomLeftRadius = 4;
                style.BorderBottomRightRadius = 4;
                break;

            case "select":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(6);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.Cursor = "pointer";
                style.BorderTopLeftRadius = 4;
                style.BorderTopRightRadius = 4;
                style.BorderBottomLeftRadius = 4;
                style.BorderBottomRightRadius = 4;
                break;

            case "button":
                style.Display = DisplayType.InlineBlock;
                style.BoxSizing = BoxSizingType.BorderBox;
                // 边框清零（与 Edge 一致）
                style.BorderTopWidth = 0;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BorderTopStyle = BorderStyle.None;
                style.BorderRightStyle = BorderStyle.None;
                style.BorderBottomStyle = BorderStyle.None;
                style.BorderLeftStyle = BorderStyle.None;
                // 内边距
                style.PaddingTop = new PixelLength(6);
                style.PaddingBottom = new PixelLength(6);
                style.PaddingLeft = new PixelLength(12);
                style.PaddingRight = new PixelLength(12);
                // 背景色
                style.BackgroundColor = SKColor.Parse("#2196F3");
                // 文字样式
                style.Color = SKColors.White;
                style.FontSize = 14;
                style.FontWeight = FontWeight.Normal;
                style.FontFamily = "Arial, sans-serif";
                style.TextAlign = TextAlignType.Center;
                style.LineHeight = 1.2f;
                style.Cursor = "pointer";
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                style.BorderTopLeftRadius = 4;
                style.BorderTopRightRadius = 4;
                style.BorderBottomLeftRadius = 4;
                style.BorderBottomRightRadius = 4;
                break;

            case "fieldset":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                break;

            case "legend":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                break;

            case "label":
                style.Display = DisplayType.InlineBlock;
                style.Cursor = "pointer";
                break;

            case "output":
                style.Display = DisplayType.InlineBlock;
                style.FontFamily = "monospace";
                break;

            case "progress":
                style.Display = DisplayType.InlineBlock;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Height = new PixelLength(16);
                style.Width = new PixelLength(150);
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                style.BorderTopLeftRadius = 8;
                style.BorderTopRightRadius = 8;
                style.BorderBottomLeftRadius = 8;
                style.BorderBottomRightRadius = 8;
                break;

            case "meter":
                style.Display = DisplayType.InlineBlock;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Height = new PixelLength(16);
                style.Width = new PixelLength(150);
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                style.BorderTopLeftRadius = 8;
                style.BorderTopRightRadius = 8;
                style.BorderBottomLeftRadius = 8;
                style.BorderBottomRightRadius = 8;
                break;

            case "datalist":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                break;

            case "optgroup":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                break;

            case "option":
                style.Display = DisplayType.Block;
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                break;

            case "form":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;

            case "keygen":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopColor = SKColor.Parse("#DADCE0");
                style.BorderRightColor = SKColor.Parse("#DADCE0");
                style.BorderBottomColor = SKColor.Parse("#DADCE0");
                style.BorderLeftColor = SKColor.Parse("#DADCE0");
                break;

            case "input[type=checkbox]":
            case "input[type=radio]":
                style.Display = DisplayType.InlineBlock;
                style.Width = new PixelLength(13);
                style.Height = new PixelLength(13);
                style.Cursor = "pointer";
                break;

            case "input[type=file]":
                style.Display = DisplayType.InlineBlock;
                style.Cursor = "pointer";
                break;

            case "input[type=image]":
                style.Display = DisplayType.InlineBlock;
                style.Cursor = "pointer";
                break;

            case "input[type=hidden]":
                style.Display = DisplayType.None;
                break;
        }
    }
}