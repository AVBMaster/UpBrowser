using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.ElementStyles;

public static class BlockElements
{
    public static void Apply(ComputedStyle style, string tagName)
    {
        switch (tagName)
        {
            // 通用块级容器
            case "div":
                style.Display = DisplayType.Block;
                break;
            case "p":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;

            // 标题元素
            case "h1":
                style.Display = DisplayType.Block;
                style.FontSize = 32;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(24);
                style.MarginBottom = new PixelLength(16);
                break;
            case "h2":
                style.Display = DisplayType.Block;
                style.FontSize = 24;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(20);
                style.MarginBottom = new PixelLength(12);
                break;
            case "h3":
                style.Display = DisplayType.Block;
                style.FontSize = 20;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(18);
                style.MarginBottom = new PixelLength(10);
                break;
            case "h4":
                style.Display = DisplayType.Block;
                style.FontSize = 18;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(8);
                break;
            case "h5":
                style.Display = DisplayType.Block;
                style.FontSize = 16;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(14);
                style.MarginBottom = new PixelLength(6);
                break;
            case "h6":
                style.Display = DisplayType.Block;
                style.FontSize = 14;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(12);
                style.MarginBottom = new PixelLength(4);
                break;

            // 列表元素
            case "ul":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "ol":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "li":
                style.Display = DisplayType.ListItem;
                style.PaddingLeft = new PixelLength(20);
                break;
            case "dl":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "dt":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.MarginTop = new PixelLength(16);
                break;
            case "dd":
                style.Display = DisplayType.Block;
                style.MarginLeft = new PixelLength(40);
                style.MarginBottom = new PixelLength(16);
                break;

            // 表格元素
            case "table":
                style.Display = DisplayType.Table;
                style.BorderCollapse = true;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "thead":
                style.Display = DisplayType.TableRow;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tbody":
                style.Display = DisplayType.TableRow;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tfoot":
                style.Display = DisplayType.TableRow;
                style.VerticalAlign = VerticalAlignType.Middle;
                break;
            case "tr":
                style.Display = DisplayType.TableRow;
                style.VerticalAlign = VerticalAlignType.Inherit;
                break;
            case "th":
                style.Display = DisplayType.TableCell;
                style.FontWeight = FontWeight.Bold;
                style.TextAlign = TextAlignType.Center;
                style.VerticalAlign = VerticalAlignType.Inherit;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                break;
            case "td":
                style.Display = DisplayType.TableCell;
                style.VerticalAlign = VerticalAlignType.Inherit;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                break;
            case "caption":
                style.Display = DisplayType.TableCaption;
                style.TextAlign = TextAlignType.Center;
                style.PaddingTop = new PixelLength(4);
                style.PaddingBottom = new PixelLength(4);
                break;
            case "colgroup":
                style.Display = DisplayType.TableColumnGroup;
                break;
            case "col":
                style.Display = DisplayType.TableColumn;
                break;

            // 格式化文本
            case "pre":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(8);
                style.PaddingRight = new PixelLength(8);
                style.BackgroundColor = SKColor.Parse("#F5F5F5");
                style.BorderTopWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopColor = SKColors.LightGray;
                style.BorderBottomColor = SKColors.LightGray;
                style.BorderLeftColor = SKColors.LightGray;
                style.BorderRightColor = SKColors.LightGray;
                break;

            // 引用和注释
            case "blockquote":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.MarginLeft = new PixelLength(40);
                style.MarginRight = new PixelLength(40);
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(16);
                style.PaddingRight = new PixelLength(16);
                style.BorderLeftWidth = 4;
                style.BorderLeftColor = SKColors.LightGray;
                break;
            case "address":
                style.Display = DisplayType.Block;
                style.FontStyle = FontStyleType.Italic;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;

            // HTML5 语义化元素
            case "article":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "aside":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "main":
                style.Display = DisplayType.Block;
                break;
            case "section":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "nav":
                style.Display = DisplayType.Block;
                break;
            case "header":
                style.Display = DisplayType.Block;
                style.MarginBottom = new PixelLength(16);
                break;
            case "footer":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                break;
            case "figure":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.MarginLeft = new PixelLength(40);
                style.MarginRight = new PixelLength(40);
                break;
            case "figcaption":
                style.Display = DisplayType.Block;
                style.TextAlign = TextAlignType.Center;
                style.FontSize = 14;
                style.Color = SKColors.Gray;
                style.MarginTop = new PixelLength(8);
                break;

            // 交互元素
            case "details":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "summary":
                style.Display = DisplayType.Block;
                style.FontWeight = FontWeight.Bold;
                style.Cursor = "pointer";
                break;
            case "dialog":
                style.Display = DisplayType.None;
                style.Position = PositionType.Absolute;
                style.BackgroundColor = SKColors.White;
                style.BorderTopWidth = 1;
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                style.PaddingTop = new PixelLength(8);
                style.PaddingBottom = new PixelLength(8);
                style.PaddingLeft = new PixelLength(16);
                style.PaddingRight = new PixelLength(16);
                break;

            // 分隔线
            case "hr":
                style.Display = DisplayType.Block;
                style.BorderTopWidth = 1;
                style.BorderRightWidth = 0;
                style.BorderBottomWidth = 0;
                style.BorderLeftWidth = 0;
                style.BorderTopColor = SKColors.Gray;
                style.BorderTopStyle = BorderStyle.Solid;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.MarginLeft = new PixelLength(0);
                style.MarginRight = new PixelLength(0);
                style.Height = new PixelLength(0);
                break;

            // 其他块级元素
            case "center":
                style.Display = DisplayType.Block;
                style.TextAlign = TextAlignType.Center;
                break;
            case "dir":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
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
                style.BorderBottomWidth = 1;
                style.BorderLeftWidth = 1;
                style.BorderRightWidth = 1;
                style.BorderTopColor = SKColors.Gray;
                style.BorderBottomColor = SKColors.Gray;
                style.BorderLeftColor = SKColors.Gray;
                style.BorderRightColor = SKColors.Gray;
                break;
            case "legend":
                style.Display = DisplayType.Block;
                style.PaddingLeft = new PixelLength(4);
                style.PaddingRight = new PixelLength(4);
                break;
            case "listing":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "menu":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                style.PaddingLeft = new PixelLength(40);
                break;
            case "plaintext":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                break;
            case "xmp":
                style.Display = DisplayType.Block;
                style.FontFamily = "monospace";
                style.WhiteSpace = WhiteSpaceMode.Pre;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;

            // 表单相关块级元素
            case "form":
                style.Display = DisplayType.Block;
                style.MarginTop = new PixelLength(16);
                style.MarginBottom = new PixelLength(16);
                break;
            case "noscript":
                style.Display = DisplayType.Block;
                break;
        }
    }
}
