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
                style.Appearance = "auto";
                style.PaddingTop = new PixelLength(1);
                style.PaddingBottom = new PixelLength(1);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.BackgroundColor = SKColors.White;
                style.FontSize = 14;
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                style.Cursor = "text";
                style.VerticalAlign = VerticalAlignType.Middle;
                style.LineHeight = 1.15f;
                style.BoxSizing = BoxSizingType.BorderBox;

                switch (inputType)
                {
                    case "checkbox":
                    case "radio":
                        style.Width = new PixelLength(16);
                        style.Height = new PixelLength(16);
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.Cursor = "default";
                        style.BackgroundColor = SKColors.Transparent;
                        style.MarginTop = new PixelLength(3);
                        style.MarginBottom = new PixelLength(3);
                        style.MarginLeft = new PixelLength(4);
                        style.MarginRight = new PixelLength(3);
                        break;
                    case "button":
                    case "submit":
                    case "reset":
                        style.Display = DisplayType.InlineBlock;
                        style.BoxSizing = BoxSizingType.BorderBox;
                        style.BorderTopWidth = 2;
                        style.BorderRightWidth = 2;
                        style.BorderBottomWidth = 2;
                        style.BorderLeftWidth = 2;
                        style.BorderTopStyle = BorderStyle.Outset;
                        style.BorderRightStyle = BorderStyle.Outset;
                        style.BorderBottomStyle = BorderStyle.Outset;
                        style.BorderLeftStyle = BorderStyle.Outset;
                        style.BorderTopColor = SKColor.Parse("#808080");
                        style.BorderRightColor = SKColor.Parse("#808080");
                        style.BorderBottomColor = SKColor.Parse("#808080");
                        style.BorderLeftColor = SKColor.Parse("#808080");
                        style.PaddingTop = new PixelLength(1);
                        style.PaddingBottom = new PixelLength(1);
                        style.PaddingLeft = new PixelLength(6);
                        style.PaddingRight = new PixelLength(6);
                        style.BackgroundColor = SKColor.Parse("#E1E1E1");
                        style.Color = SKColors.Black;
                        style.FontSize = 14;
                        style.FontFamily = "Segoe UI, Arial, sans-serif";
                        style.TextAlign = TextAlignType.Center;
                        style.Cursor = "default";
                        style.WhiteSpace = WhiteSpaceMode.Nowrap;
                        style.LineHeight = 1.2f;
                        break;
                    case "file":
                        style.Cursor = "default";
                        style.PaddingLeft = new PixelLength(2);
                        style.PaddingRight = new PixelLength(2);
                        style.BoxSizing = BoxSizingType.BorderBox;
                        break;
                    case "image":
                        style.Cursor = "pointer";
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.PaddingTop = new PixelLength(0);
                        style.PaddingBottom = new PixelLength(0);
                        style.PaddingLeft = new PixelLength(0);
                        style.PaddingRight = new PixelLength(0);
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "hidden":
                        style.Display = DisplayType.None;
                        break;
                    case "range":
                        style.PaddingTop = new PixelLength(2);
                        style.PaddingBottom = new PixelLength(2);
                        style.PaddingLeft = new PixelLength(2);
                        style.PaddingRight = new PixelLength(2);
                        style.BorderTopWidth = 0;
                        style.BorderRightWidth = 0;
                        style.BorderBottomWidth = 0;
                        style.BorderLeftWidth = 0;
                        style.Cursor = "default";
                        style.BackgroundColor = SKColors.Transparent;
                        break;
                    case "color":
                        style.Width = new PixelLength(50);
                        style.Height = new PixelLength(30);
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
                        style.PaddingLeft = new PixelLength(1);
                        style.PaddingRight = new PixelLength(1);
                        style.BackgroundColor = SKColor.Parse("#E1E1E1");
                        style.Cursor = "default";
                        break;
                    case "search":
                        style.BoxSizing = BoxSizingType.BorderBox;
                        break;
                    case "password":
                        style.Cursor = "text";
                        break;
                }
                break;

            case "textarea":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                style.BackgroundColor = SKColors.White;
                style.Color = SKColors.Black;
                style.FontSize = 14;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.PreWrap;
                style.Cursor = "text";
                style.LineHeight = 1.2f;
                style.Resize = ResizeType.Both;
                break;

            case "select":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderTopStyle = BorderStyle.Inset;
                style.BorderRightStyle = BorderStyle.Inset;
                style.BorderBottomStyle = BorderStyle.Inset;
                style.BorderLeftStyle = BorderStyle.Inset;
                style.BorderTopColor = SKColor.Parse("#767676");
                style.BorderRightColor = SKColor.Parse("#767676");
                style.BorderBottomColor = SKColor.Parse("#767676");
                style.BorderLeftColor = SKColor.Parse("#767676");
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.BackgroundColor = SKColors.White;
                style.Color = SKColors.Black;
                style.FontSize = 14;
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                style.Cursor = "default";
                style.LineHeight = 1.2f;
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                break;

            case "button":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Outset;
                style.BorderRightStyle = BorderStyle.Outset;
                style.BorderBottomStyle = BorderStyle.Outset;
                style.BorderLeftStyle = BorderStyle.Outset;
                style.BorderTopColor = SKColor.Parse("#808080");
                style.BorderRightColor = SKColor.Parse("#808080");
                style.BorderBottomColor = SKColor.Parse("#808080");
                style.BorderLeftColor = SKColor.Parse("#808080");
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
                style.Cursor = "default";
                style.WhiteSpace = WhiteSpaceMode.Nowrap;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;

            case "fieldset":
                style.Display = DisplayType.Block;
                style.BorderTopWidth = 2;
                style.BorderRightWidth = 2;
                style.BorderBottomWidth = 2;
                style.BorderLeftWidth = 2;
                style.BorderTopStyle = BorderStyle.Groove;
                style.BorderRightStyle = BorderStyle.Groove;
                style.BorderBottomStyle = BorderStyle.Groove;
                style.BorderLeftStyle = BorderStyle.Groove;
                style.BorderTopColor = SKColor.Parse("#808080");
                style.BorderRightColor = SKColor.Parse("#808080");
                style.BorderBottomColor = SKColor.Parse("#808080");
                style.BorderLeftColor = SKColor.Parse("#808080");
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(10);
                style.PaddingLeft = new PixelLength(12);
                style.PaddingRight = new PixelLength(12);
                style.MarginTop = new PixelLength(2);
                style.MarginBottom = new PixelLength(2);
                style.MarginLeft = new PixelLength(2);
                style.MarginRight = new PixelLength(2);
                style.BackgroundColor = SKColors.Transparent;
                style.AlignItems = AlignItemsType.Stretch;
                break;

            case "legend":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.FontSize = 14;
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                style.MarginTop = new PixelLength(-4);
                style.MarginBottom = new PixelLength(2);
                style.Color = SKColors.Black;
                break;

            case "label":
                style.Display = DisplayType.Inline;
                style.Cursor = "default";
                break;

            case "output":
                style.Display = DisplayType.Inline;
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                break;

            case "datalist":
                style.Display = DisplayType.None;
                break;

            case "optgroup":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                break;

            case "option":
                style.Display = DisplayType.Block;
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.FontFamily = "Segoe UI, Arial, sans-serif";
                style.FontSize = 14;
                break;

            case "form":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(0);
                style.MarginBottom = new PixelLength(0);
                break;

            case "progress":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Height = new PixelLength(20);
                style.Width = new PixelLength(150);
                style.BorderTopWidth = 0;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BackgroundColor = SKColors.Transparent;
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
                break;

            case "meter":
                style.Display = DisplayType.InlineBlock;
                style.Appearance = "auto";
                style.BoxSizing = BoxSizingType.BorderBox;
                style.VerticalAlign = VerticalAlignType.Middle;
                style.Height = new PixelLength(20);
                style.Width = new PixelLength(150);
                style.BorderTopWidth = 0;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BackgroundColor = SKColors.Transparent;
                style.PaddingTop = new PixelLength(2);
                style.PaddingBottom = new PixelLength(2);
                style.PaddingLeft = new PixelLength(2);
                style.PaddingRight = new PixelLength(2);
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

            case "menu":
                style.Display = DisplayType.Block;
                style.ListStyleType = ListStyleType.Disc;
                break;
        }
    }
}
