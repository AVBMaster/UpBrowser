using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Css;
using System.Text;

namespace UpBrowser.Rendering;

public class PaintVisitor
{
    private readonly DisplayList _displayList = new();
    private SKTypeface _defaultTypeface = SKTypeface.Default;
    private Dictionary<string, SKTypeface> _typefaceCache;
    private ImageCache _imageCache;
    private float _contentOffsetY;
    private string[]? _fontFamilies;
    private string? _baseUrl;
    private Core.Dom.Element? _focusedElement;

    public PaintVisitor(float contentOffsetY = 0,
        Dictionary<string, SKTypeface>? sharedTypefaceCache = null,
        ImageCache? sharedImageCache = null,
        string[]? fontFamilies = null,
        string? baseUrl = null)
    {
        _contentOffsetY = contentOffsetY;
        _defaultTypeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _typefaceCache = sharedTypefaceCache ?? new Dictionary<string, SKTypeface>();
        _imageCache = sharedImageCache ?? new ImageCache();
        _fontFamilies = fontFamilies;
        _baseUrl = baseUrl;
    }

    public void SetFocusedElement(Core.Dom.Element? element) => _focusedElement = element;
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
            if (child is Element element)
                VisitElement(element);
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
            DrawListMarker(element, layoutBox, style);

        foreach (var child in element.Children)
        {
            if (child is Element childElement && childElement.ComputedStyle != null && childElement.ComputedStyle.Display != DisplayType.None)
                VisitElement(childElement);
        }

        if (hasOverflowHidden)
            _displayList.Add(PaintOpPool.GetPopClipOp());
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
        if (!style.BackgroundColor.HasValue || style.BackgroundColor.Value.Alpha == 0)
            return;

        SKColor bgColor = style.BackgroundColor.Value;
        var bgRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);
        if (bgRect.Width <= 0 || bgRect.Height <= 0) return;
        if (style.Opacity < 1.0f)
            bgColor = bgColor.WithAlpha((byte)(bgColor.Alpha * style.Opacity));

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
        shadowOp.Bounds = new SKRect(rect.Left + shadow.OffsetX - shadow.BlurRadius, rect.Top + shadow.OffsetY - shadow.BlurRadius,
                                     rect.Right + shadow.OffsetX + shadow.BlurRadius, rect.Bottom + shadow.OffsetY + shadow.BlurRadius);
        _displayList.Add(shadowOp);
    }

    private async void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var url = style.BackgroundImage;
        if (string.IsNullOrEmpty(url)) return;
        var image = await _imageCache.GetImageAsync(url);
        if (image == null) return;
        float imgW = image.Width, imgH = image.Height;
        float tileWidth = imgW, tileHeight = imgH;
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
            var destRect = new SKRect(rect.Left + posX, rect.Top + posY, rect.Left + posX + tileWidth, rect.Top + posY + tileHeight);
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
        if (repeatX) while (startX > rect.Left) startX -= tileWidth;
        float startY = rect.Top + posY;
        if (repeatY) while (startY > rect.Top) startY -= tileHeight;

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
        // 按钮不绘制边框（已在 FormElements 中设为零，此处再确保一下）
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
            return;
        if (style.BorderTopWidth <= 0 && style.BorderRightWidth <= 0 && style.BorderBottomWidth <= 0 && style.BorderLeftWidth <= 0)
            return;

        float tl = style.BorderTopLeftRadius, tr = style.BorderTopRightRadius, br = style.BorderBottomRightRadius, bl = style.BorderBottomLeftRadius;
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
        bool hasFill = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasStroke = style.BorderTopWidth > 0;
        if (!hasFill && !hasStroke) return;

        using var borderPath = new SKPath();
        float x = rect.Left, y = rect.Top, w = rect.Width, h = rect.Height;
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

        if (hasFill)
        {
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = new SKPath(borderPath);
            bgOp.FillPaint = new SKPaint { Color = style.BackgroundColor.Value, Style = SKPaintStyle.Fill };
            bgOp.Bounds = rect;
            _displayList.Add(bgOp);
        }

        if (hasStroke)
        {
            var strokeOp = PaintOpPool.GetDrawPathOp();
            strokeOp.Path = new SKPath(borderPath);
            strokeOp.StrokePaint = new SKPaint
            {
                Color = style.BorderTopColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = style.BorderTopWidth,
                IsAntialias = true
            };
            strokeOp.Bounds = rect;
            _displayList.Add(strokeOp);
        }
    }

    private void DrawElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        if (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase))
        {
            DrawInputElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            DrawSelectElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            DrawButtonElement(element, box, style);
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
                DrawTextNode(textNode, box, style);
        }
    }

    private void CollectTextNodes(Node node, StringBuilder sb)
    {
        if (node is TextNode textNode)
        {
            var text = textNode.TextContent ?? "";
            if (!string.IsNullOrEmpty(text))
                sb.Append(text);
        }
        foreach (var child in node.Children)
            CollectTextNodes(child, sb);
    }

    private string GetButtonText(Element button)
    {
        var textBuilder = new StringBuilder();
        CollectTextNodes(button, textBuilder);
        string result = textBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(result))
            return result;
        var valueAttr = button.GetAttribute("value");
        return !string.IsNullOrEmpty(valueAttr) ? valueAttr : "Button";
    }

    private void DrawButtonElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string buttonText = GetButtonText(element);
        if (string.IsNullOrEmpty(buttonText)) buttonText = "Button";

        var borderBox = box.BorderBox;
        float btnFontSize = style.FontSize > 0 ? style.FontSize : 13.3333f;
        float btnLineHeightValue = style.LineHeight > 0 ? style.LineHeight : 1.2f;

        float borderTopWidth = style.BorderTopWidth;
        float borderBottomWidth = style.BorderBottomWidth;
        float borderLeftWidth = style.BorderLeftWidth;
        float borderRightWidth = style.BorderRightWidth;

        float padTop = GetPixelLengthFromStyle(style.PaddingTop, 6);
        float padBottom = GetPixelLengthFromStyle(style.PaddingBottom, 6);
        float padLeft = GetPixelLengthFromStyle(style.PaddingLeft, 12);
        float padRight = GetPixelLengthFromStyle(style.PaddingRight, 12);

        var bgRect = new SKRect(
            borderBox.Left + TotalOffsetX,
            borderBox.Top + TotalOffsetY,
            borderBox.Right + TotalOffsetX,
            borderBox.Bottom + TotalOffsetY
        );

        SKColor btnBgColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0 ? style.BackgroundColor.Value : SKColor.Parse("#2196F3");
        SKColor btnBorderColor = style.BorderTopColor.Alpha > 0 ? style.BorderTopColor : btnBgColor;
        float borderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
        if (borderRadius <= 0) borderRadius = 4;

        if (borderRadius > 0)
        {
            var path = CreateRoundedRectPath(bgRect, borderRadius);
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = path;
            bgOp.FillPaint = new SKPaint { Color = btnBgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.Bounds = bgRect;
            _displayList.Add(bgOp);

            if (borderTopWidth > 0)
            {
                var borderOp = PaintOpPool.GetDrawPathOp();
                borderOp.Path = path;
                borderOp.StrokePaint = new SKPaint { Color = btnBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = borderTopWidth, IsAntialias = true };
                borderOp.Bounds = bgRect;
                _displayList.Add(borderOp);
            }
        }
        else
        {
            var bgOp = PaintOpPool.GetDrawRectOp();
            bgOp.Rect = bgRect;
            bgOp.FillColor = btnBgColor;
            bgOp.Bounds = bgRect;
            _displayList.Add(bgOp);

            if (borderTopWidth > 0)
            {
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
        }

        // 精确测量文本宽度
        float textWidth = Core.Layout.TextMeasurer.Instance?.MeasureText(buttonText, style.FontFamily ?? "Arial", btnFontSize) ?? 0;
        float maxContentWidth = bgRect.Width - padLeft - padRight - borderLeftWidth - borderRightWidth;
        if (textWidth > maxContentWidth) textWidth = maxContentWidth;

        float contentLeft = bgRect.Left + borderLeftWidth + padLeft;
        float contentTop = bgRect.Top + borderTopWidth + padTop;
        float contentRight = bgRect.Right - borderRightWidth - padRight;
        float contentBottom = bgRect.Bottom - borderBottomWidth - padBottom;
        float contentWidth = contentRight - contentLeft;
        float contentHeight = contentBottom - contentTop;

        float textX = contentLeft + Math.Max(0, (contentWidth - textWidth) / 2);
        float textY = contentTop + Math.Max(0, (contentHeight - btnFontSize) / 2) + btnFontSize * 0.8f;
        textX = Math.Max(contentLeft, Math.Min(textX, contentRight - textWidth));
        textY = Math.Max(contentTop, Math.Min(textY, contentBottom));

        SKColor textColor = style.Color.Alpha > 0 ? style.Color : SKColors.Black;
        if (textColor.Alpha == 0) textColor = SKColors.Black;

        var textOp = PaintOpPool.GetDrawTextOp();
        textOp.Text = buttonText;
        textOp.X = textX;
        textOp.Y = textY;
        textOp.Color = textColor;
        textOp.FontSize = btnFontSize;
        textOp.FontFamily = style.FontFamily ?? "Arial, sans-serif";
        textOp.FontWeight = style.FontWeight;
        textOp.TextAlign = TextAlignType.Left;
        textOp.Bounds = new SKRect(textX, textY, textX + textWidth, textY + btnFontSize);
        _displayList.Add(textOp);
    }

    private void DrawInputElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string? value = element.Value;
        string? placeholder = element.GetAttribute("placeholder");
        string? inputType = element.InputType?.ToLowerInvariant();
        bool isPassword = inputType == "password";
        string displayText;
        bool showPlaceholder = false;
        if (!string.IsNullOrEmpty(value))
            displayText = isPassword ? new string('●', value.Length) : value;
        else if (!string.IsNullOrEmpty(placeholder))
        {
            displayText = placeholder;
            showPlaceholder = true;
        }
        else
            return;

        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float textWidth = MeasureTextWidth(displayText, fontSize, style.FontFamily);
        var contentBox = box.ContentBox;
        float textX = contentBox.Left + 2;
        float textY = contentBox.Top + fontSize * 0.85f;
        SKColor textColor = showPlaceholder ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        if (textWidth > contentBox.Width - 4)
            textWidth = contentBox.Width - 4;

        var op = PaintOpPool.GetDrawTextOp();
        op.Text = displayText;
        op.X = textX;
        op.Y = textY + TotalOffsetY;
        op.Color = textColor;
        op.FontSize = fontSize;
        op.FontFamily = style.FontFamily ?? "Arial";
        op.FontWeight = style.FontWeight;
        op.Bounds = new SKRect(textX, contentBox.Top + TotalOffsetY, textX + textWidth, contentBox.Bottom + TotalOffsetY);
        _displayList.Add(op);

        if (_focusedElement == element)
        {
            float cursorX = textX + Math.Min(textWidth, contentBox.Width - 4);
            float cursorTop = contentBox.Top + TotalOffsetY + 2;
            float cursorBottom = contentBox.Bottom + TotalOffsetY - 2;
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = cursorX;
            cursorOp.Y1 = cursorTop;
            cursorOp.X2 = cursorX;
            cursorOp.Y2 = cursorBottom;
            cursorOp.Color = new SKColor(0, 0, 0);
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(cursorX - 1, cursorTop, cursorX + 1, cursorBottom);
            _displayList.Add(cursorOp);
        }
    }

    private void DrawSelectElement(Element element, LayoutBox box, ComputedStyle style)
    {
        string displayText = "Select...";
        foreach (var child in element.Children)
        {
            if (child is Element childEl && childEl.TagName == "OPTION")
            {
                var selected = childEl.HasAttribute("selected");
                if (selected || string.IsNullOrEmpty(displayText) || displayText == "Select...")
                {
                    var optText = childEl.TextContent?.Trim();
                    if (!string.IsNullOrEmpty(optText))
                        displayText = optText;
                    if (selected) break;
                }
            }
        }
        float fontSize = style.FontSize > 0 ? style.FontSize : 14;
        float textWidth = MeasureTextWidth(displayText, fontSize, style.FontFamily);
        var contentBox = box.ContentBox;
        float textX = contentBox.Left + 4;
        float textY = contentBox.Top + fontSize * 0.85f;
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = displayText;
        op.X = textX;
        op.Y = textY + TotalOffsetY;
        op.Color = style.Color.Alpha > 0 ? style.Color : SKColors.Black;
        op.FontSize = fontSize;
        op.FontFamily = style.FontFamily ?? "Arial";
        op.Bounds = new SKRect(textX, contentBox.Top + TotalOffsetY, textX + textWidth, contentBox.Bottom + TotalOffsetY);
        _displayList.Add(op);

        float arrowSize = 6;
        float arrowX = contentBox.Right - 16;
        float arrowY = contentBox.Top + (contentBox.Height - arrowSize) / 2 + TotalOffsetY;
        var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX, arrowY);
        arrowPath.LineTo(arrowX + arrowSize, arrowY);
        arrowPath.LineTo(arrowX + arrowSize / 2, arrowY + arrowSize);
        arrowPath.Close();
        var arrowOp = PaintOpPool.GetDrawPathOp();
        arrowOp.Path = arrowPath;
        arrowOp.FillPaint = new SKPaint { Color = new SKColor(120, 120, 120), Style = SKPaintStyle.Fill, IsAntialias = true };
        arrowOp.Bounds = new SKRect(arrowX, arrowY, arrowX + arrowSize, arrowY + arrowSize);
        _displayList.Add(arrowOp);
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (Core.Layout.TextMeasurer.Instance != null)
            return Core.Layout.TextMeasurer.Instance.MeasureText(text, fontFamily ?? "Arial", fontSize);
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

    private void DrawImageElement(Element element, ComputedStyle style, LayoutBox box)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return;
        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null) return;
        Task.Run(async () =>
        {
            var image = await _imageCache.GetImageAsync(resolvedSrc);
            if (image == null) return;
            var rect = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom);
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

    private string? ResolveImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:") || url.StartsWith("blob:"))
            return url;
        if (url.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(_baseUrl) && _baseUrl.StartsWith("https://"))
                return "https:" + url;
            return "http:" + url;
        }
        if (string.IsNullOrEmpty(_baseUrl)) return url;
        try
        {
            var baseUri = new Uri(_baseUrl.EndsWith('/') ? _baseUrl : _baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return url; }
    }

    private float GetPixelLengthFromStyle(Length length, float defaultValue)
    {
        return length is PixelLength pixelLength ? pixelLength.Value : defaultValue;
    }

    private SKPath CreateRoundedRectPath(SKRect rect, float radius)
    {
        var path = new SKPath();
        float x = rect.Left, y = rect.Top, w = rect.Width, h = rect.Height;
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
            return;

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

        else if (box.LineRuns != null)
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
    private readonly LinkedList<string> _accessOrder = new();
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();
    private const int MaxCacheSize = 200;

    public async Task<SKImage?> GetImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        lock (_lock)
        {
            if (_cache.TryGetValue(url, out var image))
            {
                _accessOrder.Remove(url);
                _accessOrder.AddFirst(url);
                return image;
            }
        }
        try
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                var ext = Path.GetExtension(url).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp")
                {
                    var data = await File.ReadAllBytesAsync(url);
                    var image = SKImage.FromEncodedData(data);
                    if (image != null)
                    {
                        lock (_lock)
                        {
                            _cache[url] = image;
                            _accessOrder.AddFirst(url);
                            EvictIfNeeded();
                        }
                    }
                    return image;
                }
                return null;
            }
            else
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                var image = SKImage.FromEncodedData(bytes);
                if (image != null)
                {
                    lock (_lock)
                    {
                        _cache[url] = image;
                        _accessOrder.AddFirst(url);
                        EvictIfNeeded();
                    }
                }
                return image;
            }
        }
        catch
        {
            return null;
        }
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > MaxCacheSize)
        {
            var last = _accessOrder.Last;
            if (last == null) break;
            if (_cache.TryGetValue(last.Value, out var oldImage))
                oldImage?.Dispose();
            _cache.Remove(last.Value);
            _accessOrder.RemoveLast();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var img in _cache.Values)
                img?.Dispose();
            _cache.Clear();
            _accessOrder.Clear();
        }
    }
}