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
        _displayList.Clear();

        var root = document.DocumentElement ?? document.Body;
        if (root != null)
        {
            VisitElement(root);
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
            layoutBox.BorderBox.Top + _contentOffsetY,
            layoutBox.BorderBox.Right,
            layoutBox.BorderBox.Bottom + _contentOffsetY);

        DrawElementBackground(element, layoutBox, style, offsetBorderBox);
        DrawElementBorder(element, layoutBox, style, offsetBorderBox);

        DrawElementContent(element, layoutBox, style);

        var sortedChildren = element.Children
            .OfType<Element>()
            .Where(e => e.ComputedStyle != null && e.ComputedStyle.Display != DisplayType.None)
            .OrderBy(e => e.ComputedStyle?.ZIndex ?? 0)
            .ThenBy(e => GetTreeOrder(element, e));

        foreach (var child in sortedChildren)
        {
            VisitElement(child);
        }
    }

    private int GetTreeOrder(Element parent, Element child)
    {
        return parent.Children.IndexOf(child);
    }

    private void DrawElementBackground(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        if (!style.BackgroundColor.HasValue || style.BackgroundColor.Value.Alpha == 0)
            return;

        var bgRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);

        if (bgRect.Width <= 0 || bgRect.Height <= 0) return;

        var op = new DrawRectOp
        {
            Rect = bgRect,
            FillColor = style.BackgroundColor.Value,
            BorderRadius = style.BorderTopLeftRadius > 0 ? style.BorderTopLeftRadius : 0,
            Bounds = borderRect
        };
        _displayList.Add(op);

        if (!string.IsNullOrEmpty(style.BackgroundImage))
        {
            DrawBackgroundImage(element, style, bgRect);
        }
    }

    private async void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var url = style.BackgroundImage;
        if (string.IsNullOrEmpty(url)) return;

        var image = await _imageCache.GetImageAsync(url);
        if (image == null) return;

        var destRect = rect;
        if (style.BackgroundRepeat == BackgroundRepeat.NoRepeat)
        {
            float x = style.BackgroundPositionX is PixelLength px ? px.Value : 0;
            float y = style.BackgroundPositionY is PixelLength py ? py.Value : 0;
            destRect = new SKRect(rect.Left + x, rect.Top + y, rect.Left + x + image.Width, rect.Top + y + image.Height);
        }

        var op = new DrawImageOp
        {
            Image = image,
            SourceRect = new SKRect(0, 0, image.Width, image.Height),
            DestRect = destRect,
            Fit = ImageFit.Cover,
            Bounds = rect
        };
        _displayList.Add(op);
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
        if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase))
        {
            DrawImageElement(element, style, box);
            return;
        }

        DrawInlineRuns(box);
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

        float y = box.ContentBox.Top + _contentOffsetY;
        float x = box.ContentBox.Left;

        if (box.Lines != null)
        {
            foreach (var line in box.Lines)
            {
                foreach (var run in line.Runs)
                {
                    if (run.IsText && run.Node is TextNode textNode)
                    {
                        var parentStyle = textNode.Parent?.ParentElement?.ComputedStyle;

                        var op = new DrawTextOp
                        {
                            Text = run.Text,
                            X = x,
                            Y = y + line.Baseline - box.ContentBox.Top,
                            Color = run.Color ?? parentStyle?.Color ?? SKColors.Black,
                            FontSize = run.FontSize ?? parentStyle?.FontSize ?? 16,
                            FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial",
                            Bounds = new SKRect(x, y, x + run.Width, y + run.Height)
                        };
                        _displayList.Add(op);
                    }
                    x += run.Width;
                }
                x = box.ContentBox.Left;
                y += line.Height;
            }
        }

        if (box.LineRuns != null && (box.Lines == null || box.Lines.Count == 0))
        {
            y = box.ContentBox.Top + _contentOffsetY;
            x = box.ContentBox.Left;
            float fontSize = box.LineRuns.FirstOrDefault()?.FontSize ?? 16;
            float baseline = box.ContentBox.Top + _contentOffsetY + fontSize * 0.85f;

            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode textNode)
                {
                    var parentStyle = textNode.Parent?.ParentElement?.ComputedStyle;
                    var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;

var op = new DrawTextOp
                        {
                            Text = run.Text,
                            X = x,
                            Y = baseline,
                            Color = run.Color ?? parentStyle?.Color ?? SKColors.Black,
                            FontSize = actualFontSize,
                            FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial",
                            Bounds = new SKRect(x, box.ContentBox.Top + _contentOffsetY, x + run.Width, box.ContentBox.Top + _contentOffsetY + run.Height)
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