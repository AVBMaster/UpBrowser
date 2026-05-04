using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class FormElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 输入元素
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.BackgroundColor = SKColors.White;
                style.Height = new PixelLength(24);
                style.FontSize = 14;
                style.Cursor = "text";
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.Width = new PixelLength(200);
                style.Height = new PixelLength(100);
                style.Cursor = "text";
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.BackgroundColor = SKColors.White;
                style.Height = new PixelLength(24);
                style.FontSize = 14;
                style.Cursor = "pointer";
                break;

            // 按钮
            case "button":
            case "input[type=button]":
            case "input[type=submit]":
            case "input[type=reset]":
                style.Display = DisplayType.InlineBlock;
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.DarkGray;
                style.BorderBottomColor = SKColors.DarkGray;
                style.BorderLeftColor = SKColors.Gray;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                style.PaddingLeft = new PixelLength(12);
                style.PaddingRight = new PixelLength(12);
                style.BackgroundColor = SKColor.Parse("#E0E0E0");
                style.Height = new PixelLength(24);
                style.FontSize = 14;
                style.Cursor = "pointer";
                break;

            // 表单容器
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                break;

            case "legend":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                break;

            // 标签
            case "label":
                style.Display = DisplayType.InlineBlock;
                style.Cursor = "pointer";
                break;

            // 输出和计量
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.BorderTopLeftRadius = 8;
                style.BorderTopRightRadius = 8;
                style.BorderBottomLeftRadius = 8;
                style.BorderBottomRightRadius = 8;
                break;

            // 数据列表
            case "datalist":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(8);
                style.MarginBottom = new PixelLength(8);
                break;

            // 选项组
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

            // 其他表单元素
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
                style.BorderTopColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                break;

            // 特殊输入类型
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
