using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Css;

namespace UpBrowser.Rendering;

public class PaintVisitor
{
    private readonly DisplayList _displayList = new();
    private SKTypeface _defaultTypeface = SKTypeface.Default;
    private Dictionary<string, SKTypeface> _typefaceCache;
    private ImageCache _imageCache;
    private float _contentOffsetY;
    private string[]? _fontFamilies;

    public PaintVisitor(float contentOffsetY = 0,
        Dictionary<string, SKTypeface>? sharedTypefaceCache = null,
        ImageCache? sharedImageCache = null,
        string[]? fontFamilies = null)
    {
        _contentOffsetY = contentOffsetY;
        _defaultTypeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _typefaceCache = sharedTypefaceCache ?? new Dictionary<string, SKTypeface>();
        _imageCache = sharedImageCache ?? new ImageCache();
        _fontFamilies = fontFamilies;
    }

    private float TotalOffsetY => _contentOffsetY;
    private float TotalOffsetX => 0;

    public DisplayList GetDisplayList() => _displayList;

    private SKTypeface GetTypeface(string family, FontWeight weight)
    {
        var key = $"{family}:{weight}";
        if (!_typefaceCache.TryGetValue(key, out var typeface))
        {
            var families = _fontFamilies ?? SKFontManager.Default.FontFamilies.ToArray();
            var index = Array.IndexOf(families, family);
            if (index >= 0)
            {
                var style = SKFontManager.Default.GetFontStyles(index);
                typeface = style.CreateTypeface(0) ?? _defaultTypeface;
            }
            else
                typeface = _defaultTypeface;
            _typefaceCache[key] = typeface ?? _defaultTypeface ?? SKTypeface.Default;
        }
        return typeface ?? _defaultTypeface ?? SKTypeface.Default;
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

        if (element.TagName.Equals("HR", StringComparison.OrdinalIgnoreCase))
        {
            float y = layoutBox.ContentBox.Top + TotalOffsetY + layoutBox.ContentBox.Height / 2;
            float x1 = layoutBox.ContentBox.Left;
            float x2 = layoutBox.ContentBox.Right;

            var lineOp = PaintOpPool.GetDrawLineOp();
            lineOp.X1 = x1;
            lineOp.Y1 = y;
            lineOp.X2 = x2;
            lineOp.Y2 = y;
            lineOp.Color = style.BorderTopColor;
            lineOp.StrokeWidth = style.BorderTopWidth;
            lineOp.Bounds = new SKRect(x1, y - 1, x2, y + 1);
            _displayList.Add(lineOp);
        }

        bool hasOverflowHidden = style.Overflow == OverflowType.Hidden || style.OverflowX == OverflowType.Hidden || style.OverflowY == OverflowType.Hidden;
        if (hasOverflowHidden)
        {
            var clipRect = new SKRect(
                layoutBox.PaddingBox.Left,
                layoutBox.PaddingBox.Top + TotalOffsetY,
                layoutBox.PaddingBox.Right,
                layoutBox.PaddingBox.Bottom + TotalOffsetY);
            if (clipRect.Width > 0 && clipRect.Height > 0)
            {
                var clipOp = PaintOpPool.GetPushClipOp();
                clipOp.ClipRect = clipRect;
                _displayList.Add(clipOp);
            }
        }

        DrawElementContent(element, layoutBox, style);

        if (style.Display == DisplayType.ListItem)
        {
            DrawListMarker(element, layoutBox, style);
        }

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

        if (hasOverflowHidden)
        {
            _displayList.Add(PaintOpPool.GetPopClipOp());
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

        var op = PaintOpPool.GetDrawTextOp();
        op.Text = markerText;
        op.X = markerX;
        op.Y = markerY + TotalOffsetY;
        op.Color = style.Color;
        op.FontSize = style.FontSize;
        op.FontFamily = style.FontFamily ?? "Arial";
        op.Bounds = new SKRect(markerX, markerY, markerX + 20, markerY + style.FontSize);
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
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
            return;

        SKColor bgColor;
        if (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0)
        {
            bgColor = style.BackgroundColor.Value;
        }
        else
        {
            return;
        }

        if (bgColor.Alpha == 0) return;

        var bgRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);

        if (bgRect.Width <= 0 || bgRect.Height <= 0) return;

        if (style.Opacity < 1.0f)
        {
            bgColor = bgColor.WithAlpha((byte)(bgColor.Alpha * style.Opacity));
        }

        DrawBoxShadow(borderRect, style);

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = bgRect;
        op.FillColor = bgColor;
        op.BorderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius, Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
        op.Bounds = borderRect;
        _displayList.Add(op);
    }

    private void DrawBoxShadow(SKRect rect, ComputedStyle style)
    {
        if (style.BoxShadow == null) return;

        var shadow = style.BoxShadow;
        using var path = new SKPath();
        float radius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));

        if (radius > 0)
            path.AddRoundRect(rect, radius, radius);
        else
            path.AddRect(rect);

        var shadowOp = PaintOpPool.GetDrawShadowOp();
        shadowOp.Path = path;
        shadowOp.Color = shadow.Color;
        shadowOp.BlurRadius = Math.Max(1, shadow.BlurRadius);
        shadowOp.OffsetX = shadow.OffsetX;
        shadowOp.OffsetY = shadow.OffsetY;
        shadowOp.ZIndex = -1;
        shadowOp.Bounds = new SKRect(
            rect.Left + shadow.OffsetX - shadow.BlurRadius,
            rect.Top + shadow.OffsetY - shadow.BlurRadius,
            rect.Right + shadow.OffsetX + shadow.BlurRadius,
            rect.Bottom + shadow.OffsetY + shadow.BlurRadius);
        _displayList.Add(shadowOp);
    }
    private async void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var url = style.BackgroundImage;
        if (string.IsNullOrEmpty(url)) return;

        var image = await _imageCache.GetImageAsync(url);
        if (image == null) return;

        float imgW = image.Width;
        float imgH = image.Height;
        float tileWidth = imgW;
        float tileHeight = imgH;

        if (style.BackgroundSize == BackgroundSizeType.Cover)
        {
            float srcRatio = imgW / imgH;
            float destRatio = rect.Width / rect.Height;
            if (srcRatio > destRatio)
            {
                tileHeight = rect.Height;
                tileWidth = tileHeight * srcRatio;
            }
            else
            {
                tileWidth = rect.Width;
                tileHeight = tileWidth / srcRatio;
            }
        }
        else if (style.BackgroundSize == BackgroundSizeType.Contain)
        {
            float srcRatio = imgW / imgH;
            float destRatio = rect.Width / rect.Height;
            if (srcRatio > destRatio)
            {
                tileWidth = rect.Width;
                tileHeight = tileWidth / srcRatio;
            }
            else
            {
                tileHeight = rect.Height;
                tileWidth = tileHeight * srcRatio;
            }
        }
        else if (style.BackgroundSize == BackgroundSizeType.Length)
        {
            if (style.BackgroundSizeWidth != null)
                tileWidth = style.BackgroundSizeWidth.ToPixels(rect.Width, 16, rect.Width, rect.Height);
            if (style.BackgroundSizeHeight != null)
                tileHeight = style.BackgroundSizeHeight.ToPixels(rect.Height, 16, rect.Width, rect.Height);
        }

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
                rect.Left + posX + tileWidth, rect.Top + posY + tileHeight);
            var op = PaintOpPool.GetDrawImageOp();
            op.Image = image;
            op.SourceRect = new SKRect(0, 0, imgW, imgH);
            op.DestRect = destRect;
            op.Fit = ImageFit.None;
            op.Bounds = rect;
            _displayList.Add(op);
            return;
        }
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

                    var op = PaintOpPool.GetDrawImageOp();
                    op.Image = image;
                    op.SourceRect = new SKRect(srcLeft, srcTop, srcRight, srcBottom);
                    op.DestRect = clipRect;
                    op.Fit = ImageFit.None;
                    op.Bounds = rect;
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

        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
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

        var op = PaintOpPool.GetDrawRectOp();
        op.Rect = borderRect;
        op.BorderTopWidth = style.BorderTopWidth;
        op.BorderRightWidth = style.BorderRightWidth;
        op.BorderBottomWidth = style.BorderBottomWidth;
        op.BorderLeftWidth = style.BorderLeftWidth;
        op.BorderTopColor = style.BorderTopColor;
        op.BorderRightColor = style.BorderRightColor;
        op.BorderBottomColor = style.BorderBottomColor;
        op.BorderLeftColor = style.BorderLeftColor;
        op.Bounds = borderRect;
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
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = borderPath;
            bgOp.FillPaint = new SKPaint { Color = style.BackgroundColor.Value, Style = SKPaintStyle.Fill };
            bgOp.Bounds = rect;
            _displayList.Add(bgOp);
        }

        var borderColor = style.BorderTopColor;
        var borderWidth = style.BorderTopWidth;

        if (borderWidth > 0)
        {
            var strokeOp = PaintOpPool.GetDrawPathOp();
            strokeOp.Path = borderPath;
            strokeOp.StrokePaint = new SKPaint
            {
                Color = borderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = borderWidth,
                IsAntialias = true
            };
            strokeOp.Bounds = rect;
            _displayList.Add(strokeOp);
        }
    }

    private void DrawElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            string buttonText = GetButtonText(element);
            if (!string.IsNullOrEmpty(buttonText))
            {
                var borderBox = box.BorderBox;
                float btnFontSize = style.FontSize > 0 ? style.FontSize : 13.3333f;
                float btnLineHeightValue = style.LineHeight > 0 ? style.LineHeight : 1.2f;

                float borderTopWidth = style.BorderTopWidth > 0 ? style.BorderTopWidth : 2;
                float borderBottomWidth = style.BorderBottomWidth > 0 ? style.BorderBottomWidth : 2;
                float borderLeftWidth = style.BorderLeftWidth > 0 ? style.BorderLeftWidth : 2;
                float borderRightWidth = style.BorderRightWidth > 0 ? style.BorderRightWidth : 2;

                float padTop = GetPixelLengthFromStyle(style.PaddingTop, 4);
                float padBottom = GetPixelLengthFromStyle(style.PaddingBottom, 4);
                float padLeft = GetPixelLengthFromStyle(style.PaddingLeft, 10);
                float padRight = GetPixelLengthFromStyle(style.PaddingRight, 10);

                var bgRect = new SKRect(
                    borderBox.Left + TotalOffsetX,
                    borderBox.Top + TotalOffsetY,
                    borderBox.Right + TotalOffsetX,
                    borderBox.Bottom + TotalOffsetY
                );

                SKColor btnBgColor;
                if (style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0)
                    btnBgColor = style.BackgroundColor.Value;
                else
                    btnBgColor = SKColor.Parse("#EFEFEF");

                SKColor btnBorderColor;
                if (style.BorderTopColor.Alpha > 0)
                    btnBorderColor = style.BorderTopColor;
                else
                    btnBorderColor = SKColor.Parse("#767676");

                float borderRadius = Math.Max(style.BorderTopLeftRadius,
                    Math.Max(style.BorderTopRightRadius,
                    Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
                if (borderRadius <= 0) borderRadius = 2;

                if (borderRadius > 0)
                {
                    var path = CreateRoundedRectPath(bgRect, borderRadius);

                    var bgOp = PaintOpPool.GetDrawPathOp();
                    bgOp.Path = path;
                    bgOp.FillPaint = new SKPaint
                    {
                        Color = btnBgColor,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    bgOp.Bounds = bgRect;
                    _displayList.Add(bgOp);

                    var borderOp = PaintOpPool.GetDrawPathOp();
                    borderOp.Path = path;
                    borderOp.StrokePaint = new SKPaint
                    {
                        Color = btnBorderColor,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = borderTopWidth,
                        IsAntialias = true
                    };
                    borderOp.Bounds = bgRect;
                    _displayList.Add(borderOp);
                }
                else
                {
                    var bgOp = PaintOpPool.GetDrawRectOp();
                    bgOp.Rect = bgRect;
                    bgOp.FillColor = btnBgColor;
                    bgOp.Bounds = bgRect;
                    _displayList.Add(bgOp);

                    var borderOp = PaintOpPool.GetDrawRectOp();
                    borderOp.Rect = bgRect;
                    borderOp.BorderTopWidth = borderTopWidth;
                    borderOp.BorderBottomWidth = borderBottomWidth;
                    borderOp.BorderLeftWidth = borderLeftWidth;
                    borderOp.BorderRightWidth = borderRightWidth;
                    borderOp.BorderTopColor = btnBorderColor;
                    borderOp.BorderBottomColor = btnBorderColor;
                    borderOp.BorderLeftColor = btnBorderColor;
                    borderOp.BorderRightColor = btnBorderColor;
                    borderOp.Bounds = bgRect;
                    _displayList.Add(borderOp);
                }

                float textWidth = MeasureTextWidth(buttonText, btnFontSize, style.FontFamily);

                float contentLeft = bgRect.Left + borderLeftWidth + padLeft;
                float contentTop = bgRect.Top + borderTopWidth + padTop;
                float contentRight = bgRect.Right - borderRightWidth - padRight;
                float contentBottom = bgRect.Bottom - borderBottomWidth - padBottom;
                float contentWidthAvailable = contentRight - contentLeft;
                float contentHeightAvailable = contentBottom - contentTop;

                float textX = contentLeft + Math.Max(0, (contentWidthAvailable - textWidth) / 2);

                float textHeight = btnFontSize;
                float textY = contentTop + Math.Max(0, (contentHeightAvailable - textHeight) / 2) + btnFontSize * 0.8f;

                SKColor textColor = style.Color.Alpha > 0 ? style.Color : SKColors.Black;

                var textOp = PaintOpPool.GetDrawTextOp();
                textOp.Text = buttonText;
                textOp.X = textX;
                textOp.Y = textY;
                textOp.Color = textColor;
                textOp.FontSize = btnFontSize;
                textOp.FontFamily = style.FontFamily ?? "Arial, sans-serif";
                textOp.FontWeight = style.FontWeight;
                textOp.TextAlign = TextAlignType.Left;
                textOp.Bounds = bgRect;
                _displayList.Add(textOp);
            }

            return;
        }

        if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
        {
            DrawImageElement(element, style, box);
            return;
        }

        if (box.LineRuns != null && box.LineRuns.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }
        if (box.Lines != null && box.Lines.Count > 0)
        {
            DrawInlineRuns(box);
            return;
        }

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
        var textBuilder = new System.Text.StringBuilder();
        foreach (var child in button.Children)
        {
            if (child is TextNode textNode)
            {
                string text = textNode.TextContent?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    textBuilder.Append(text);
                }
            }
            else if (child is Element childElement && childElement.TagName.Equals("SPAN", StringComparison.OrdinalIgnoreCase))
            {
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
            return result;
        }

        var valueAttr = button.GetAttribute("value");
        if (!string.IsNullOrEmpty(valueAttr))
            return valueAttr;

        var innerText = button.GetAttribute("innerText");
        if (!string.IsNullOrEmpty(innerText))
        {
            return innerText;
        }

        return "Button";
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        float avgCharWidth = fontSize * 0.55f;
        return text.Length * avgCharWidth;
    }

    private void DrawTextNode(TextNode textNode, LayoutBox box, ComputedStyle parentStyle)
    {
        var text = textNode.TextContent;
        if (string.IsNullOrEmpty(text)) return;

        var contentBox = box.ContentBox;
        float y = contentBox.Top + (parentStyle?.FontSize ?? 16);

        var textColor = parentStyle?.Color ?? SKColors.Black;
        if (parentStyle != null && parentStyle.Opacity < 1.0f)
            textColor = textColor.WithAlpha((byte)(textColor.Alpha * parentStyle.Opacity));

        var op = PaintOpPool.GetDrawTextOp();
        op.Text = text;
        op.X = contentBox.Left;
        op.Y = y;
        op.Color = textColor;
        op.FontSize = parentStyle?.FontSize ?? 16;
        op.FontFamily = parentStyle?.FontFamily ?? "Arial";
        op.FontWeight = parentStyle?.FontWeight ?? FontWeight.Normal;
        op.TextAlign = parentStyle?.TextAlign ?? TextAlignType.Start;
        op.Underline = parentStyle?.TextDecoration == TextDecorationType.Underline;
        op.LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough;
        op.Bounds = new SKRect(contentBox.Left, contentBox.Top, contentBox.Right, contentBox.Bottom);
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

            var op = PaintOpPool.GetDrawImageOp();
            op.Image = image;
            op.SourceRect = new SKRect(0, 0, image.Width, image.Height);
            op.DestRect = rect;
            op.Fit = ImageFit.Fill;
            op.ZIndex = element.ComputedStyle?.ZIndex ?? 0;
            op.Bounds = rect;
            _displayList.Add(op);
        });
    }

    private float GetPixelLengthFromStyle(Length length, float defaultValue)
    {
        if (length is PixelLength pixelLength)
            return pixelLength.Value;

        return defaultValue;
    }

    private SKPath CreateRoundedRectPath(SKRect rect, float radius)
    {
        var path = new SKPath();
        float x = rect.Left;
        float y = rect.Top;
        float w = rect.Width;
        float h = rect.Height;

        path.MoveTo(x + radius, y);
        path.LineTo(x + w - radius, y);
        path.QuadTo(x + w, y, x + w, y + radius);
        path.LineTo(x + w, y + h - radius);
        path.QuadTo(x + w, y + h, x + w - radius, y + h);
        path.LineTo(x + radius, y + h);
        path.QuadTo(x, y + h, x, y + h - radius);
        path.LineTo(x, y + radius);
        path.QuadTo(x, y, x + radius, y);
        path.Close();

        return path;
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
                float lineOffsetX = line.TextAlignOffsetX;
                
                foreach (var run in line.Runs)
                {
                    if (run.IsText && run.Node is TextNode textNode)
                    {
                        var parentStyle = textNode.ParentElement?.ComputedStyle;
                        var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;

                        var op = PaintOpPool.GetDrawTextOp();
                        op.Text = run.Text;
                        op.X = currentX + lineOffsetX;
                        op.Y = baseline;
                        op.Color = run.Color ?? parentStyle?.Color ?? SKColors.Black;
                        op.FontSize = actualFontSize;
                        op.FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial";
                        op.Underline = parentStyle?.TextDecoration == TextDecorationType.Underline;
                        op.LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough;
                        op.Bounds = new SKRect(currentX + lineOffsetX, lineY, currentX + run.Width + lineOffsetX, lineY + line.Height);
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

                    var op = PaintOpPool.GetDrawTextOp();
                    op.Text = run.Text;
                    op.X = x;
                    op.Y = baseline;
                    op.Color = run.Color ?? parentStyle?.Color ?? SKColors.Black;
                    op.FontSize = actualFontSize;
                    op.FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial";
                    op.Underline = parentStyle?.TextDecoration == TextDecorationType.Underline;
                    op.LineThrough = parentStyle?.TextDecoration == TextDecorationType.LineThrough;
                    op.Bounds = new SKRect(x, boxTop, x + run.Width, boxTop + run.Height);
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
