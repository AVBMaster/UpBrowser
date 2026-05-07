using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Css;

namespace UpBrowser.Rendering;

public class PaintVisitor
{
    private readonly DisplayList _displayList = new();
    private SKTypeface? _defaultTypeface;
    private Dictionary<string, SKTypeface> _typefaceCache = new();
    private ImageCache _imageCache = new();
    private float _contentOffsetY;

    public PaintVisitor(float contentOffsetY = 0)
    {
        _contentOffsetY = contentOffsetY;
        _defaultTypeface = GetChineseTypeface();
    }

    private float TotalOffsetY => _contentOffsetY;
    private float TotalOffsetX => 0;

    public DisplayList GetDisplayList() => _displayList;

    private SKTypeface? GetChineseTypeface()
    {
        var fontFamilies = SKFontManager.Default.FontFamilies.ToArray();

        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "FangSong", "KaiTi" };

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

        return SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    }

    private SKTypeface GetTypeface(string family, FontWeight weight)
    {
        var key = $"{family}:{weight}";
        if (!_typefaceCache.TryGetValue(key, out var typeface))
        {
            var families = SKFontManager.Default.FontFamilies.ToArray();
            var index = Array.IndexOf(families, family);
            if (index >= 0)
            {
                var style = SKFontManager.Default.GetFontStyles(index);
                typeface = style.CreateTypeface(0) ?? _defaultTypeface;
            }
            else
                typeface = _defaultTypeface;
            _typefaceCache[key] = typeface;
        }
        return typeface;
    }

    public void VisitDocument(Document document)
    {
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        VisitElement(root);

        foreach (var child in root.Children)
        {
            if (child is Element element)
            {
                VisitElement(element);
            }
        }
    }

    private void VisitElement(Element element)
    {
        var layoutBox = element.LayoutBox;
        if (layoutBox == null) return;

        var style = element.ComputedStyle;
        if (style == null) return;

        if (style.Visibility == VisibilityType.Hidden || style.Display == DisplayType.None)
            return;

        var offsetBorderBox = new SKRect(
            layoutBox.BorderBox.Left,
            layoutBox.BorderBox.Top + TotalOffsetY,
            layoutBox.BorderBox.Right,
            layoutBox.BorderBox.Bottom + TotalOffsetY);

        DrawElementBackground(element, layoutBox, style, offsetBorderBox);
        DrawElementBorder(element, layoutBox, style, offsetBorderBox);

        DrawElementBackground(element, layoutBox, style, offsetBorderBox);
        DrawElementBorder(element, layoutBox, style, offsetBorderBox);

        if (element.TagName.Equals("HR", StringComparison.OrdinalIgnoreCase))
        {
            float y = layoutBox.ContentBox.Top + TotalOffsetY + layoutBox.ContentBox.Height / 2;
            float x1 = layoutBox.ContentBox.Left;
            float x2 = layoutBox.ContentBox.Right;

            var lineOp = new DrawLineOp
            {
                X1 = x1,
                Y1 = y,
                X2 = x2,
                Y2 = y,
                Color = style.BorderTopColor,
                StrokeWidth = style.BorderTopWidth,
                Bounds = new SKRect(x1, y - 1, x2, y + 1)
            };
            _displayList.Add(lineOp);
        }

        DrawElementContent(element, layoutBox, style);

        if (style.Display == DisplayType.ListItem)
        {
            DrawListMarker(element, layoutBox, style);
        }

        // 遍历所有子元素进行渲染
        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                if (childElement.ComputedStyle != null && childElement.ComputedStyle.Display != DisplayType.None)
                {
                    VisitElement(childElement);
                }
            }
        }
    }

    private int GetTreeOrder(Element parent, Element child)
    {
        return parent.Children.IndexOf(child);
    }

    private void DrawListMarker(Element element, LayoutBox box, ComputedStyle style)
    {
        if (style.ListStyleType == ListStyleType.None) return;

        if (element.Parent is not Element parent || parent.LayoutBox == null) return;

        int itemIndex = 0;
        foreach (var child in parent.Children)
        {
            if (child is Element childElement && childElement.ComputedStyle?.Display == DisplayType.ListItem)
            {
                if (childElement == element) break;
                itemIndex++;
            }
        }

        float markerX = parent.LayoutBox.ContentBox.Left - 25;
        float markerY = box.ContentBox.Top + style.FontSize * 0.8f;

        string markerText = style.ListStyleType switch
        {
            ListStyleType.Disc => "\u2022",
            ListStyleType.Circle => "\u25CB",
            ListStyleType.Square => "\u25A0",
            ListStyleType.Decimal => (itemIndex + 1).ToString() + ".",
            ListStyleType.LowerRoman => ToRoman(itemIndex + 1).ToLower() + ".",
            ListStyleType.UpperRoman => ToRoman(itemIndex + 1) + ".",
            _ => "\u2022"
        };

        var op = new DrawTextOp
        {
            Text = markerText,
            X = markerX,
            Y = markerY + TotalOffsetY,
            Color = style.Color,
            FontSize = style.FontSize,
            FontFamily = style.FontFamily ?? "Arial",
            Bounds = new SKRect(markerX, markerY, markerX + 20, markerY + style.FontSize)
        };
        _displayList.Add(op);
    }

    private string ToRoman(int number)
    {
        if (number <= 0) return "";
        var result = "";
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                result += symbols[i];
                number -= values[i];
            }
        }
        return result;
    }

    private void DrawElementBackground(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        // 确保按钮有背景色
        SKColor bgColor;
        if (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0)
        {
            bgColor = style.BackgroundColor.Value;
        }
        else if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            // 按钮默认背景色
            bgColor = SKColor.Parse("#1A73E8");
        }
        else
        {
            return;
        }

        if (bgColor.Alpha == 0) return;

        var bgRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);

        if (bgRect.Width <= 0 || bgRect.Height <= 0) return;

        var op = new DrawRectOp
        {
            Rect = bgRect,
            FillColor = bgColor,
            BorderRadius = style.BorderTopLeftRadius > 0 ? style.BorderTopLeftRadius : 0,
            Bounds = borderRect
        };
        _displayList.Add(op);
    }

    private async void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var url = style.BackgroundImage;
        if (string.IsNullOrEmpty(url)) return;

        var image = await _imageCache.GetImageAsync(url);
        if (image == null) return;

        float posX = 0, posY = 0;
        if (style.BackgroundPositionX != null)
        {
            if (style.BackgroundPositionX is PixelLength px) posX = px.Value;
            else if (style.BackgroundPositionX is PercentLength ppx) posX = rect.Width * ppx.Value;
        }
        if (style.BackgroundPositionY != null)
        {
            if (style.BackgroundPositionY is PixelLength py) posY = py.Value;
            else if (style.BackgroundPositionY is PercentLength ppy) posY = rect.Height * ppy.Value;
        }

        if (style.BackgroundRepeat == BackgroundRepeat.NoRepeat)
        {
            var destRect = new SKRect(rect.Left + posX, rect.Top + posY, 
                rect.Left + posX + image.Width, rect.Top + posY + image.Height);
            var op = new DrawImageOp
            {
                Image = image,
                SourceRect = new SKRect(0, 0, image.Width, image.Height),
                DestRect = destRect,
                Fit = ImageFit.None,
                Bounds = rect
            };
            _displayList.Add(op);
            return;
        }

        float tileWidth = image.Width;
        float tileHeight = image.Height;
        bool repeatX = style.BackgroundRepeat == BackgroundRepeat.Repeat || style.BackgroundRepeat == BackgroundRepeat.RepeatX;
        bool repeatY = style.BackgroundRepeat == BackgroundRepeat.Repeat || style.BackgroundRepeat == BackgroundRepeat.RepeatY;

        float startX = rect.Left + posX;
        if (repeatX)
        {
            while (startX > rect.Left) startX -= tileWidth;
        }

        float startY = rect.Top + posY;
        if (repeatY)
        {
            while (startY > rect.Top) startY -= tileHeight;
        }

        for (float y = startY; y < rect.Bottom; y += tileHeight)
        {
            if (!repeatY && y > rect.Top) break;
            for (float x = startX; x < rect.Right; x += tileWidth)
            {
                if (!repeatX && x > rect.Left) break;

                var tileRect = new SKRect(x, y, x + tileWidth, y + tileHeight);

                var clipRect = tileRect;
                clipRect.Left = Math.Max(clipRect.Left, rect.Left);
                clipRect.Top = Math.Max(clipRect.Top, rect.Top);
                clipRect.Right = Math.Min(clipRect.Right, rect.Right);
                clipRect.Bottom = Math.Min(clipRect.Bottom, rect.Bottom);

                if (clipRect.Width > 0 && clipRect.Height > 0)
                {
                    float srcLeft = (clipRect.Left - x) / tileWidth * image.Width;
                    float srcTop = (clipRect.Top - y) / tileHeight * image.Height;
                    float srcRight = srcLeft + (clipRect.Width / tileWidth * image.Width);
                    float srcBottom = srcTop + (clipRect.Height / tileHeight * image.Height);

                    var op = new DrawImageOp
                    {
                        Image = image,
                        SourceRect = new SKRect(srcLeft, srcTop, srcRight, srcBottom),
                        DestRect = clipRect,
                        Fit = ImageFit.None,
                        Bounds = rect
                    };
                    _displayList.Add(op);
                }
            }
        }
    }

    private void DrawElementBorder(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        if (style.BorderTopWidth <= 0 && style.BorderRightWidth <= 0 &&
            style.BorderBottomWidth <= 0 && style.BorderLeftWidth <= 0)
            return;

        float tl = style.BorderTopLeftRadius;
        float tr = style.BorderTopRightRadius;
        float br = style.BorderBottomRightRadius;
        float bl = style.BorderBottomLeftRadius;

        if (tl > 0 || tr > 0 || br > 0 || bl > 0)
        {
            DrawRoundedBorder(borderRect, tl, tr, br, bl, style);
            return;
        }

        var op = new DrawRectOp
        {
            Rect = borderRect,
            BorderTopWidth = style.BorderTopWidth,
            BorderRightWidth = style.BorderRightWidth,
            BorderBottomWidth = style.BorderBottomWidth,
            BorderLeftWidth = style.BorderLeftWidth,
            BorderTopColor = style.BorderTopColor,
            BorderRightColor = style.BorderRightColor,
            BorderBottomColor = style.BorderBottomColor,
            BorderLeftColor = style.BorderLeftColor,
            Bounds = borderRect
        };
        _displayList.Add(op);
    }

    private void DrawRoundedBorder(SKRect rect, float tl, float tr, float br, float bl, ComputedStyle style)
    {
        var borderPath = new SKPath();

        float x = rect.Left;
        float y = rect.Top;
        float w = rect.Width;
        float h = rect.Height;

        borderPath.MoveTo(x + tl, y);
        borderPath.LineTo(x + w - tr, y);
        borderPath.QuadTo(x + w, y, x + w, y + tr);
        borderPath.LineTo(x + w, y + h - br);
        borderPath.QuadTo(x + w, y + h, x + w - br, y + h);
        borderPath.LineTo(x + bl, y + h);
        borderPath.QuadTo(x, y + h, x, y + h - bl);
        borderPath.LineTo(x, y + tl);
        borderPath.QuadTo(x, y, x + tl, y);
        borderPath.Close();

        if (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0)
        {
            var bgOp = new DrawPathOp
            {
                Path = borderPath,
                FillPaint = new SKPaint { Color = style.BackgroundColor.Value, Style = SKPaintStyle.Fill },
                Bounds = rect
            };
            _displayList.Add(bgOp);
        }

        var borderColor = style.BorderTopColor;
        var borderWidth = style.BorderTopWidth;

        if (borderWidth > 0)
        {
            var strokeOp = new DrawPathOp
            {
                Path = borderPath,
                StrokePaint = new SKPaint
                {
                    Color = borderColor,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = borderWidth,
                    IsAntialias = true
                },
                Bounds = rect
            };
            _displayList.Add(strokeOp);
        }
    }

    private void DrawElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        // 处理按钮文本
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            string buttonText = GetButtonText(element);
            if (!string.IsNullOrEmpty(buttonText))
            {
                var cb = box.ContentBox;
                if (cb.Width <= 0 || cb.Height <= 0)
                    cb = box.BorderBox;   // 后备

                float fontSize = style.FontSize > 0 ? style.FontSize : 13.3333f;
                float lineHeight = style.LineHeight * fontSize;

                // 基线 = 内容框顶部 + (内容高度 - 行高)/2 + 行高 * 0.8（垂直居中效果）
                float baselineY = cb.Top + (cb.Height - lineHeight) / 2 + lineHeight * 0.8f;
                float textX = cb.Left + (cb.Width - MeasureTextWidth(buttonText, fontSize, style.FontFamily)) / 2;

                SKColor textColor = style.Color;
                if (textColor == SKColors.Black || textColor.Alpha == 0)
                    textColor = SKColors.Black;   // 按钮默认黑色文字

                var textOp = new DrawTextOp
                {
                    Text = buttonText,
                    X = textX,
                    Y = baselineY,
                    Color = textColor,
                    FontSize = fontSize,
                    FontFamily = style.FontFamily ?? "Arial",
                    FontWeight = style.FontWeight,
                    TextAlign = TextAlignType.Left,   // 已经手动居中
                    Bounds = new SKRect(cb.Left, cb.Top, cb.Right, cb.Bottom)
                };
                _displayList.Add(textOp);
            }
            return;
        }

        if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
        {
            DrawImageElement(element, style, box);
            return;
        }

        // 如果有 line runs，先绘制它们
        if (box.LineRuns != null && box.LineRuns.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }
        // 如果有 line boxes，也绘制它们
        if (box.Lines != null && box.Lines.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }

        // 否则，绘制子元素的内联内容
        foreach (var child in element.Children)
        {
            if (child is TextNode textNode)
            {
                DrawTextNode(textNode, box, style);
            }
        }
    }

    private string GetButtonText(Element button)
    {
        // 优先从子文本节点获取
        var textBuilder = new System.Text.StringBuilder();
        foreach (var child in button.Children)
        {
            if (child is TextNode textNode)
            {
                string text = textNode.TextContent?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    textBuilder.Append(text);
                }
            }
            else if (child is Element childElement && childElement.TagName.Equals("SPAN", StringComparison.OrdinalIgnoreCase))
            {
                // 递归获取 span 内的文本
                foreach (var subChild in childElement.Children)
                {
                    if (subChild is TextNode subText)
                    {
                        textBuilder.Append(subText.TextContent?.Trim());
                    }
                }
            }
        }

        string result = textBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(result))
        {
            Console.WriteLine($"Button text from children: '{result}'");
            return result;
        }

        // 尝试从 value 属性获取
        var valueAttr = button.GetAttribute("value");
        if (!string.IsNullOrEmpty(valueAttr))
        {
            Console.WriteLine($"Button text from value attribute: '{valueAttr}'");
            return valueAttr;
        }

        // 尝试从 innerText 属性获取（如果有）
        var innerText = button.GetAttribute("innerText");
        if (!string.IsNullOrEmpty(innerText))
        {
            return innerText;
        }

        Console.WriteLine($"Button has no text! TagName={button.TagName}, Children count={button.Children.Count}");
        return "Button";
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // 估算文本宽度
        float avgCharWidth = fontSize * 0.55f;
        return text.Length * avgCharWidth;
    }

    private void DrawTextNode(TextNode textNode, LayoutBox box, ComputedStyle parentStyle)
    {
        var text = textNode.TextContent;
        if (string.IsNullOrEmpty(text)) return;

        var contentBox = box.ContentBox;
        float y = contentBox.Top + (parentStyle?.FontSize ?? 16);

        var op = new DrawTextOp
        {
            Text = text,
            X = contentBox.Left,
            Y = y,
            Color = parentStyle?.Color ?? SKColors.Black,
            FontSize = parentStyle?.FontSize ?? 16,
            FontFamily = parentStyle?.FontFamily ?? "Arial",
            FontWeight = parentStyle?.FontWeight ?? FontWeight.Normal,
            TextAlign = parentStyle?.TextAlign ?? TextAlignType.Start,
            Underline = parentStyle?.TextDecoration == TextDecorationType.Underline,
            LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough,
            Bounds = new SKRect(contentBox.Left, contentBox.Top, contentBox.Right, contentBox.Bottom)
        };
        _displayList.Add(op);
    }

    private void DrawInlineElement(Element element, LayoutBox box, ComputedStyle parentStyle)
    {
        if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
        {
            if (element.LayoutBox != null)
                DrawImageElement(element, element.ComputedStyle!, element.LayoutBox);
            return;
        }

        var style = element.ComputedStyle;
        if (style == null) return;

        foreach (var child in element.Children)
        {
            if (child is TextNode textNode)
            {
                DrawTextNode(textNode, box, style);
            }
        }
    }

    private void DrawImageElement(Element element, ComputedStyle style, LayoutBox box)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return;

        Task.Run(async () =>
        {
            var image = await _imageCache.GetImageAsync(src);
            if (image == null) return;

            var rect = new SKRect(box.ContentBox.Left, box.ContentBox.Top,
                                  box.ContentBox.Right, box.ContentBox.Bottom);

            var op = new DrawImageOp
            {
                Image = image,
                SourceRect = new SKRect(0, 0, image.Width, image.Height),
                DestRect = rect,
                Fit = ImageFit.Fill,
                ZIndex = element.ComputedStyle?.ZIndex ?? 0,
                Bounds = rect
            };
            _displayList.Add(op);
        });
    }

    private void DrawInlineRuns(LayoutBox box)
    {
        if (box.LineRuns == null && (box.Lines == null || box.Lines.Count == 0))
        {
            return;
        }

        float boxTop = box.ContentBox.Top + TotalOffsetY;

        if (box.Lines != null)
        {
            float currentX = box.ContentBox.Left;
            
            foreach (var line in box.Lines)
            {
                float lineY = line.Y + TotalOffsetY;
                float baseline = line.Baseline + TotalOffsetY;
                
                foreach (var run in line.Runs)
                {
                    if (run.IsText && run.Node is TextNode textNode)
                    {
                        var parentStyle = textNode.ParentElement?.ComputedStyle;
                        var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;

                        var op = new DrawTextOp
                        {
                            Text = run.Text,
                            X = currentX,
                            Y = baseline,
                            Color = run.Color ?? parentStyle?.Color ?? SKColors.Black,
                            FontSize = actualFontSize,
                            FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial",
                            Underline = parentStyle?.TextDecoration == TextDecorationType.Underline,
                            LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough,
                            Bounds = new SKRect(currentX, lineY, currentX + run.Width, lineY + line.Height)
                        };
                        _displayList.Add(op);
                    }
                    currentX += run.Width;
                }
                currentX = box.ContentBox.Left;
            }
        }

        if (box.LineRuns != null && (box.Lines == null || box.Lines.Count == 0))
        {
            float x = box.ContentBox.Left;
            float fontSize = box.LineRuns.FirstOrDefault()?.FontSize ?? 16;
            float baseline = boxTop + fontSize * 0.85f;

            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode textNode)
                {
                    var parentStyle = textNode.ParentElement?.ComputedStyle;
                    var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;

                    var op = new DrawTextOp
                    {
                        Text = run.Text,
                        X = x,
                        Y = baseline,
                        Color = run.Color ?? parentStyle?.Color ?? SKColors.Black,
                        FontSize = actualFontSize,
                        FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial",
                        Underline = parentStyle?.TextDecoration == TextDecorationType.Underline,
                        LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough,
                        Bounds = new SKRect(x, boxTop, x + run.Width, boxTop + run.Height)
                    };
                    _displayList.Add(op);
                }
                x += run.Width;
            }
        }
    }
}

public class ImageCache
{
    private readonly Dictionary<string, SKImage> _cache = new();
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();

    public async Task<SKImage?> GetImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        SKImage? image;
        lock (_lock)
        {
            if (_cache.TryGetValue(url, out image)) return image;
        }

        try
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                var ext = Path.GetExtension(url).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp")
                {
                    var data = await File.ReadAllBytesAsync(url);
                    image = SKImage.FromEncodedData(data);
                }
                else return null;
            }
            else
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                image = SKImage.FromEncodedData(bytes);
            }

            if (image != null)
            {
                lock (_lock)
                {
                    _cache[url] = image;
                }
            }

            return image;
        }
        catch
        {
            return null;
        }
    }
}