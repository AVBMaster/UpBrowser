using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class FormElements
{
    public static void Apply(ComputedStyle style, string tagName, Element? element = null)
    {
        string inputType = "";
        if (tagName == "input" && element != null)
            inputType = (element.GetAttribute("type") ?? "text").ToLowerInvariant();

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
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.Cursor = "text";
                style.VerticalAlign = VerticalAlignType.Baseline;

                switch (inputType)
                {
                    case "checkbox":
                    case "radio":
                        style.Width = new PixelLength(16);
                        style.Height = new PixelLength(16);
                        style.Cursor = "pointer";
                        break;
                    case "file":
                        style.Cursor = "pointer";
                        break;
                    case "image":
                        style.Cursor = "pointer";
                        break;
                    case "hidden":
                        style.Display = DisplayType.None;
                        break;
                }
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
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
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
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.Cursor = "pointer";
                break;

            case "button":
                style.Display = DisplayType.InlineBlock;
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Solid;
                style.BorderRightStyle = BorderStyle.Solid;
                style.BorderBottomStyle = BorderStyle.Solid;
                style.BorderLeftStyle = BorderStyle.Solid;
                style.BorderTopColor = SKColor.Parse("#CCCCCC");
                style.BorderRightColor = SKColor.Parse("#CCCCCC");
                style.BorderBottomColor = SKColor.Parse("#CCCCCC");
                style.BorderLeftColor = SKColor.Parse("#CCCCCC");
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(6);
                style.PaddingRight = new PixelLength(6);
                style.BackgroundColor = SKColor.Parse("#E1E1E1");
                style.Color = SKColors.Black;
                style.FontSize = 14;
                style.FontWeight = FontWeight.Normal;
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                style.TextAlign = TextAlignType.Center;
                style.LineHeight = 1.2f;
                style.Cursor = "pointer";
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
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

        }
    }
}