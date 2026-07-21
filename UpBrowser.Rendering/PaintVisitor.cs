using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Css;
using UpBrowser.Core.Performance;
using UpBrowser.Core.Performance.Resources;
using System.Text;

namespace UpBrowser.Rendering;

public class PaintVisitor
{
    private readonly DisplayList _displayList = new();
    private SKTypeface _defaultTypeface = SKTypeface.Default;
    private Dictionary<string, SKTypeface> _typefaceCache;     // cached typefaces per family:weight
    private ImageCache _imageCache;
    private float _contentOffsetY;
    private string[]? _fontFamilies;
    private string? _baseUrl;
    private Core.Dom.Element? _focusedElement;
    private int _inputCursorPos;
    private int _inputSelStart = -1;
    private bool _inputShowCursor = true;
    private bool _inputImeComposing;
    private string _inputImeComposition = "";
    private int _inputImeCursor;
    private Core.Dom.TextNode? _selAnchorNode;
    private int _selAnchorOffset;
    private Core.Dom.TextNode? _selFocusNode;
    private int _selFocusOffset;
    private bool _hasSelection;
    private bool _selStartIsAnchor;

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

    public void SetInputState(int cursorPos, int selStart, bool showCursor,
        bool isImeComposing, string imeComposition, int imeCursor)
    {
        _inputCursorPos = cursorPos;
        _inputSelStart = selStart;
        _inputShowCursor = showCursor;
        _inputImeComposing = isImeComposing;
        _inputImeComposition = imeComposition;
        _inputImeCursor = imeCursor;
    }

    public void SetSelectionRange(Core.Dom.TextNode? anchorNode, int anchorOffset, Core.Dom.TextNode? focusNode, int focusOffset)
    {
        _selAnchorNode = anchorNode;
        _selAnchorOffset = anchorOffset;
        _selFocusNode = focusNode;
        _selFocusOffset = focusOffset;
        _hasSelection = anchorNode != null && focusNode != null;
        if (_hasSelection)
        {
            int cmp = CompareDomPosition(anchorNode!, focusNode!);
            _selStartIsAnchor = cmp <= 0;
        }
    }

    private SKRect? GetSelHighlight(Core.Dom.TextNode? runNode, string runText, SKRect runBounds,
        float fontSize, string fontFamily, Core.Dom.FontWeight fontWeight, int runStartOffset = 0)
    {
        if (!_hasSelection || runNode == null) return null;

        int startOff, endOff;

        if (_selAnchorNode == _selFocusNode)
        {
            if (runNode != _selAnchorNode) return null;
            startOff = Math.Min(_selAnchorOffset, _selFocusOffset);
            endOff = Math.Max(_selAnchorOffset, _selFocusOffset);
        }
        else
        {
            Core.Dom.TextNode? startNode, endNode;
            if (_selStartIsAnchor)
            {
                startNode = _selAnchorNode;
                startOff = _selAnchorOffset;
                endNode = _selFocusNode;
                endOff = _selFocusOffset;
            }
            else
            {
                startNode = _selFocusNode;
                startOff = _selFocusOffset;
                endNode = _selAnchorNode;
                endOff = _selAnchorOffset;
            }

            if (runNode == startNode)
            {
                int localStart = Math.Max(0, startOff - runStartOffset);
                if (localStart >= runText.Length) return null;
                return GetSubRunBounds(runText, runBounds, localStart, runText.Length, fontSize, fontFamily, fontWeight);
            }
            if (runNode == endNode)
            {
                int localEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
                if (localEnd <= 0) return null;
                return GetSubRunBounds(runText, runBounds, 0, localEnd, fontSize, fontFamily, fontWeight);
            }
            if (IsNodeBetween(runNode, startNode, endNode))
                return runBounds;
            return null;
        }

        // Single-node: convert global offsets to local offsets for this run
        int snLocalStart = Math.Max(0, startOff - runStartOffset);
        int snLocalEnd = Math.Max(0, Math.Min(runText.Length, endOff - runStartOffset));
        if (snLocalStart >= snLocalEnd) return null;
        return GetSubRunBounds(runText, runBounds, snLocalStart, snLocalEnd, fontSize, fontFamily, fontWeight);
    }

    private SKRect? GetSubRunBounds(string text, SKRect runBounds, int startOff, int endOff,
        float fontSize, string fontFamily, Core.Dom.FontWeight fontWeight)
    {
        if (startOff >= endOff || string.IsNullOrEmpty(text)) return null;
        int clampedStart = Math.Clamp(startOff, 0, text.Length);
        int clampedEnd = Math.Clamp(endOff, clampedStart, text.Length);
        if (clampedStart >= clampedEnd) return null;

        float left = runBounds.Left;
        if (clampedStart > 0)
        {
            string before = text[..clampedStart];
            left += MeasureTextWidth(before, fontSize, fontFamily, fontWeight);
        }
        float right = runBounds.Left;
        if (clampedEnd <= text.Length)
        {
            string upToEnd = text[..clampedEnd];
            right += MeasureTextWidth(upToEnd, fontSize, fontFamily, fontWeight);
        }
        else
        {
            right = runBounds.Right;
        }

        return new SKRect(left, runBounds.Top, right, runBounds.Bottom);
    }

    private static float MeasureTextWidth(string text, float fontSize, string fontFamily, Core.Dom.FontWeight weight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (Core.Layout.TextMeasurer.Instance != null)
            return Core.Layout.TextMeasurer.Instance.MeasureText(text, fontFamily, fontSize, weight);
        return text.Length * fontSize * 0.45f;
    }

    private static bool IsNodeBetween(Core.Dom.Node? target, Core.Dom.Node? a, Core.Dom.Node? b)
    {
        return CompareDomPosition(target, a) > 0 && CompareDomPosition(target, b) < 0;
    }

    private static int CompareDomPosition(Core.Dom.Node? a, Core.Dom.Node? b)
    {
        if (a == null || b == null) return a == b ? 0 : (a == null ? -1 : 1);
        if (a == b) return 0;
        var aPath = new List<Core.Dom.Node>();
        var bPath = new List<Core.Dom.Node>();
        var cur = a;
        while (cur != null) { aPath.Add(cur); cur = cur.ParentNode; }
        cur = b;
        while (cur != null) { bPath.Add(cur); cur = cur.ParentNode; }
        aPath.Reverse();
        bPath.Reverse();
        int depth = Math.Min(aPath.Count, bPath.Count);
        for (int i = 0; i < depth; i++)
        {
            if (aPath[i] != bPath[i])
            {
                var parent = aPath[i].ParentNode;
                if (parent != null)
                {
                    int ai = parent.Children.IndexOf(aPath[i]);
                    int bi = parent.Children.IndexOf(bPath[i]);
                    return ai.CompareTo(bi);
                }
                return 0;
            }
        }
        return aPath.Count.CompareTo(bPath.Count);
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
            if (child is Element element)
                VisitElement(element);
    }

    private void VisitElement(Element element)
    {
        var layoutBox = element.LayoutBox;

        // If no layout box (e.g. <tr>, <thead>, <tbody>), still visit children
        if (layoutBox == null)
        {
            foreach (var child in element.Children)
            {
                if (child is Element childElement && childElement.ComputedStyle != null && childElement.ComputedStyle.Display != DisplayType.None)
                    VisitElement(childElement);
            }
            return;
        }
        var style = element.ComputedStyle;
        if (style == null) return;
        if (style.Display == DisplayType.None)
            return;

        bool isVisibilityHidden = style.Visibility == VisibilityType.Hidden;

        var offsetBorderBox = new SKRect(
            layoutBox.BorderBox.Left,
            layoutBox.BorderBox.Top + TotalOffsetY,
            layoutBox.BorderBox.Right,
            layoutBox.BorderBox.Bottom + TotalOffsetY);

        // Compute sticky offset
        float stickyOffsetX = 0, stickyOffsetY = 0;
        if (layoutBox.IsSticky && layoutBox.Parent is LayoutBox parentBox && parentBox.IsScrollContainer)
        {
            float normalTop = layoutBox.BorderBox.Top;
            if (parentBox.ScrollY > normalTop - layoutBox.StickyTop)
                stickyOffsetY = parentBox.ScrollY - normalTop + layoutBox.StickyTop;
            if (parentBox.ScrollX > layoutBox.BorderBox.Left - layoutBox.StickyLeft)
                stickyOffsetX = parentBox.ScrollX - layoutBox.BorderBox.Left + layoutBox.StickyLeft;
        }

        SKImageFilter? elementFilter = null;
        if (!string.IsNullOrEmpty(style.Filter) && style.Filter != "none")
            elementFilter = FilterRenderer.ParseAndChain(style.Filter);

        bool hasClipPath = !string.IsNullOrEmpty(style.ClipPath) && style.ClipPath != "none";
        bool hasOpacityLayer = style.Opacity < 1.0f && style.Opacity >= 0f;

        if (!isVisibilityHidden && (elementFilter != null || hasOpacityLayer || hasClipPath))
        {
            var layerOp = PaintOpPool.GetPushLayerOp();
            layerOp.Opacity = hasOpacityLayer ? style.Opacity : 1.0f;
            layerOp.ImageFilter = elementFilter;
            if (hasClipPath)
            {
                var clipPath = ParseClipPath(style.ClipPath, layoutBox);
                if (clipPath != null)
                    layerOp.ClipPath = clipPath;
            }
            layerOp.Bounds = offsetBorderBox;
            _displayList.Add(layerOp);
        }

        if (!isVisibilityHidden)
        {
            // Apply CSS transform BEFORE background so the entire element (including background) is transformed
            bool hasTransform = !string.IsNullOrEmpty(style.Transform) && style.Transform != "none";
            SKMatrix transformMatrix = SKMatrix.Identity;
            if (hasTransform)
            {
                var transformOrigin = ParseTransformOrigin(style.TransformOrigin, layoutBox);
                var operations = TransformParser.Parse(style.Transform);
                if (operations.Count > 0)
                {
                    transformMatrix = TransformParser.ToMatrix(operations, transformOrigin.X, transformOrigin.Y);
                    var transformOp = PaintOpPool.GetPushTransformOp();
                    transformOp.Matrix = transformMatrix;
                    transformOp.Bounds = offsetBorderBox;
                    _displayList.Add(transformOp);
                }
            }

            bool isInline = style.Display == DisplayType.Inline;
            if (!isInline && !isVisibilityHidden)
            {
                DrawElementBackground(element, layoutBox, style, offsetBorderBox);
                DrawElementBorder(element, layoutBox, style, offsetBorderBox);
                DrawElementOutline(element, layoutBox, style, offsetBorderBox);
            }

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

            // Draw disclosure triangle for <summary> elements
            if (element.TagName.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                float arrowSize = Math.Min(10, layoutBox.ContentBox.Height * 0.6f);
                float arrowX = layoutBox.ContentBox.Left + 4;
                float arrowY = layoutBox.ContentBox.Top + TotalOffsetY + (layoutBox.ContentBox.Height - arrowSize) / 2;

                // Check if parent <details> has 'open' attribute
                bool isOpen = false;
                var detailsParent = element.ParentElement;
                while (detailsParent != null && detailsParent.TagName != "DETAILS")
                    detailsParent = detailsParent.ParentElement;
                if (detailsParent != null && detailsParent.HasAttribute("open"))
                    isOpen = true;

                var arrowPath = new SKPath();
                if (isOpen)
                {
                    // Downward-pointing triangle
                    arrowPath.MoveTo(arrowX, arrowY);
                    arrowPath.LineTo(arrowX + arrowSize, arrowY);
                    arrowPath.LineTo(arrowX + arrowSize * 0.5f, arrowY + arrowSize);
                    arrowPath.Close();
                }
                else
                {
                    // Rightward-pointing triangle
                    arrowPath.MoveTo(arrowX, arrowY);
                    arrowPath.LineTo(arrowX, arrowY + arrowSize);
                    arrowPath.LineTo(arrowX + arrowSize, arrowY + arrowSize * 0.5f);
                    arrowPath.Close();
                }

                var arrowOp = PaintOpPool.GetDrawPathOp();
                arrowOp.Path = arrowPath;
                arrowOp.FillPaint = new SKPaint { Color = style.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
                arrowOp.Bounds = new SKRect(arrowX, arrowY, arrowX + arrowSize, arrowY + arrowSize);
                _displayList.Add(arrowOp);
            }

            bool hasOverflowHidden = style.Overflow == OverflowType.Hidden || style.OverflowX == OverflowType.Hidden || style.OverflowY == OverflowType.Hidden;
            bool isScrollContainer = layoutBox.IsScrollContainer &&
                (layoutBox.ScrollContentHeight > layoutBox.ContentBox.Height || layoutBox.ScrollContentWidth > layoutBox.ContentBox.Width);
            bool needsClip = hasOverflowHidden || isScrollContainer;
            if (needsClip)
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

            if (isScrollContainer)
            {
                float scrollOffsetX = -layoutBox.ScrollX;
                float scrollOffsetY = -layoutBox.ScrollY;
                if (scrollOffsetX != 0 || scrollOffsetY != 0)
                {
                    var translateOp = PaintOpPool.GetPushTransformOp();
                    translateOp.Matrix = SKMatrix.CreateTranslation(scrollOffsetX, scrollOffsetY);
                    translateOp.Bounds = new SKRect(
                        layoutBox.ContentBox.Left,
                        layoutBox.ContentBox.Top + TotalOffsetY,
                        layoutBox.ContentBox.Right,
                        layoutBox.ContentBox.Bottom + TotalOffsetY);
                    _displayList.Add(translateOp);
                }
            }

            bool hasStickyOffset = (stickyOffsetX != 0 || stickyOffsetY != 0);
            if (hasStickyOffset)
            {
                var stickyOp = PaintOpPool.GetPushTransformOp();
                stickyOp.Matrix = SKMatrix.CreateTranslation(stickyOffsetX, stickyOffsetY);
                stickyOp.Bounds = offsetBorderBox;
                _displayList.Add(stickyOp);
            }

            if (!isInline)
                DrawInlineChildrenDecorations(element);

            DrawElementContent(element, layoutBox, style);

            if (style.Display == DisplayType.ListItem)
                DrawListMarker(element, layoutBox, style);

            if (hasStickyOffset)
                _displayList.Add(PaintOpPool.GetPopTransformOp());

            if (isScrollContainer && (layoutBox.ScrollX != 0 || layoutBox.ScrollY != 0))
                _displayList.Add(PaintOpPool.GetPopTransformOp());

            if (needsClip)
                _displayList.Add(PaintOpPool.GetPopClipOp());

            // Draw scrollbar for scroll containers
            if (layoutBox.IsScrollContainer && (layoutBox.ScrollContentHeight > layoutBox.ContentBox.Height || layoutBox.ScrollContentWidth > layoutBox.ContentBox.Width))
            {
                DrawScrollbar(layoutBox, style);
            }

            if (hasTransform && transformMatrix != SKMatrix.Identity)
            {
                _displayList.Add(PaintOpPool.GetPopTransformOp());
            }
        }

        foreach (var child in element.Children)
        {
            if (child is Element childElement && childElement.ComputedStyle != null && childElement.ComputedStyle.Display != DisplayType.None)
                VisitElement(childElement);
        }

        if (!isVisibilityHidden && (elementFilter != null || hasOpacityLayer || hasClipPath))
        {
            _displayList.Add(PaintOpPool.GetPopLayerOp());
        }
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

        float markerWidth = style.ListStyleType switch
        {
            ListStyleType.Disc or ListStyleType.Circle or ListStyleType.Square => 12f,
            ListStyleType.Decimal => ((itemIndex + 1).ToString() + ".").Length * style.FontSize * 0.6f,
            ListStyleType.DecimalLeadingZero => ((itemIndex + 1).ToString().PadLeft(2, '0') + ".").Length * style.FontSize * 0.6f,
            ListStyleType.LowerRoman or ListStyleType.UpperRoman => (ToRoman(itemIndex + 1) + ".").Length * style.FontSize * 0.6f,
            ListStyleType.LowerAlpha or ListStyleType.UpperAlpha => 2 * style.FontSize * 0.6f,
            _ => 12f
        };

        float markerGap = 8f;
        float markerX = parent.LayoutBox.ContentBox.Left - markerWidth - markerGap;
        
        float markerY;
        if (box.Lines != null && box.Lines.Count > 0 && box.Lines[0].Baseline > 0)
            markerY = box.Lines[0].Baseline;
        else
            markerY = box.ContentBox.Top + style.FontSize * 0.85f;
        
        string markerText = style.ListStyleType switch
        {
            ListStyleType.Disc => "\u2022",
            ListStyleType.Circle => "\u25CB",
            ListStyleType.Square => "\u25A0",
            ListStyleType.Decimal => (itemIndex + 1).ToString() + ".",
            ListStyleType.DecimalLeadingZero => (itemIndex + 1).ToString().PadLeft(2, '0') + ".",
            ListStyleType.LowerRoman => ToRoman(itemIndex + 1).ToLower() + ".",
            ListStyleType.UpperRoman => ToRoman(itemIndex + 1) + ".",
            ListStyleType.LowerAlpha => ((char)('a' + (itemIndex % 26))).ToString() + ".",
            ListStyleType.UpperAlpha => ((char)('A' + (itemIndex % 26))).ToString() + ".",
            _ => "\u2022"
        };
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = markerText;
        op.X = markerX;
        op.Y = markerY + TotalOffsetY;
        op.Color = style.Color;
        op.FontSize = style.FontSize;
        op.FontFamily = style.FontFamily ?? "Arial";
        op.Bounds = new SKRect(markerX, markerY, markerX + markerWidth, markerY + style.FontSize);
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
        var paddingRect = new SKRect(borderRect.Left + style.BorderLeftWidth, borderRect.Top + style.BorderTopWidth,
                                     borderRect.Right - style.BorderRightWidth, borderRect.Bottom - style.BorderBottomWidth);
        if (paddingRect.Width <= 0 || paddingRect.Height <= 0) return;

        // Determine the background clip rect based on background-clip property
        SKRect bgClipRect;
        switch (style.BackgroundClip?.ToLowerInvariant())
        {
            case "border-box":
                bgClipRect = borderRect;
                break;
            case "content-box":
                float padL = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
                float padT = style.PaddingTop is PixelLength pt ? pt.Value : 0;
                float padR = style.PaddingRight is PixelLength pr ? pr.Value : 0;
                float padB = style.PaddingBottom is PixelLength pb ? pb.Value : 0;
                bgClipRect = new SKRect(paddingRect.Left + padL, paddingRect.Top + padT,
                                        paddingRect.Right - padR, paddingRect.Bottom - padB);
                if (bgClipRect.Width <= 0) bgClipRect.Right = bgClipRect.Left;
                if (bgClipRect.Height <= 0) bgClipRect.Bottom = bgClipRect.Top;
                break;
            default: // padding-box
                bgClipRect = paddingRect;
                break;
        }

        // Push clip to background clip rect
        bool needsClip = style.BackgroundClip != null && style.BackgroundClip != "" &&
                         style.BackgroundClip.ToLowerInvariant() != "padding-box";
        if (needsClip)
        {
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipRect = bgClipRect;
            clipOp.Bounds = borderRect;
            _displayList.Add(clipOp);
        }

        DrawBoxShadow(borderRect, style);

        bool hasBackgroundColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBackgroundImage = !string.IsNullOrEmpty(style.BackgroundImage);

        if (hasBackgroundColor)
        {
            SKColor bgColor = style.BackgroundColor.Value;
            if (style.Opacity < 1.0f)
                bgColor = bgColor.WithAlpha((byte)(bgColor.Alpha * style.Opacity));

            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = paddingRect;
            op.FillColor = bgColor;
            op.BorderRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius, Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
            op.Bounds = borderRect;
            _displayList.Add(op);
        }

        if (hasBackgroundImage && style.BackgroundImage != null)
        {
            if (style.BackgroundImage.Contains("gradient", StringComparison.OrdinalIgnoreCase))
                DrawGradientBackground(style, paddingRect);
            else
                DrawBackgroundImage(element, style, paddingRect);
        }

        // Pop clip if we pushed one
        if (needsClip)
        {
            var popOp = PaintOpPool.GetPopClipOp();
            popOp.Bounds = borderRect;
            _displayList.Add(popOp);
        }
    }

    private void DrawBoxShadow(SKRect rect, ComputedStyle style)
    {
        if (style.BoxShadow == null) return;
        var shadow = style.BoxShadow;

        if (shadow.Inset)
        {
            float radius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
                Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
            float blur = Math.Max(1, shadow.BlurRadius);
            using var innerPath = new SKPath();
            if (radius > 0)
                innerPath.AddRoundRect(rect, radius, radius);
            else
                innerPath.AddRect(rect);
            var clipOp = PaintOpPool.GetPushClipOp();
            clipOp.ClipPath = new SKPath(innerPath);
            clipOp.AntiAlias = true;
            clipOp.Bounds = rect;
            _displayList.Add(clipOp);
            var shadowOp = PaintOpPool.GetDrawShadowOp();
            shadowOp.Path = new SKPath(innerPath);
            shadowOp.Color = shadow.Color;
            shadowOp.BlurRadius = blur;
            shadowOp.OffsetX = shadow.OffsetX;
            shadowOp.OffsetY = shadow.OffsetY;
            shadowOp.Inset = true;
            shadowOp.ZIndex = -1;
            shadowOp.Bounds = new SKRect(rect.Left - Math.Abs(shadow.OffsetX) - blur, rect.Top - Math.Abs(shadow.OffsetY) - blur,
                                         rect.Right + Math.Abs(shadow.OffsetX) + blur, rect.Bottom + Math.Abs(shadow.OffsetY) + blur);
            _displayList.Add(shadowOp);
            var popClipOp = PaintOpPool.GetPopClipOp();
            popClipOp.Bounds = rect;
            _displayList.Add(popClipOp);
            return;
        }

        var path = new SKPath();
        float borderradius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));
        if (borderradius > 0)
            path.AddRoundRect(rect, borderradius, borderradius);
        else
            path.AddRect(rect);
        var shadowOutsetOp = PaintOpPool.GetDrawShadowOp();
        shadowOutsetOp.Path.Dispose();
        shadowOutsetOp.Path = path;
        shadowOutsetOp.Color = shadow.Color;
        shadowOutsetOp.BlurRadius = Math.Max(1, shadow.BlurRadius);
        shadowOutsetOp.OffsetX = shadow.OffsetX;
        shadowOutsetOp.OffsetY = shadow.OffsetY;
        shadowOutsetOp.ZIndex = -1;
        shadowOutsetOp.Bounds = new SKRect(rect.Left + shadow.OffsetX - shadow.BlurRadius, rect.Top + shadow.OffsetY - shadow.BlurRadius,
                                     rect.Right + shadow.OffsetX + shadow.BlurRadius, rect.Bottom + shadow.OffsetY + shadow.BlurRadius);
        _displayList.Add(shadowOutsetOp);
    }

    private void DrawScrollbar(LayoutBox box, ComputedStyle style)
    {
        var paddingBox = box.PaddingBox;
        float scrollbarWidth = 12f;

        // Vertical scrollbar
        if (box.ScrollContentHeight > box.ContentBox.Height)
        {
            float trackHeight = paddingBox.Height;
            float thumbRatio = box.ContentBox.Height / box.ScrollContentHeight;
            float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
            float trackX = paddingBox.Right - scrollbarWidth;
            float trackY = paddingBox.Top + TotalOffsetY;

            var trackOp = PaintOpPool.GetDrawRectOp();
            trackOp.Rect = new SKRect(trackX, trackY, trackX + scrollbarWidth, trackY + trackHeight);
            trackOp.FillColor = new SKColor(240, 240, 240);
            trackOp.Bounds = trackOp.Rect;
            _displayList.Add(trackOp);

            float scrollRange = Math.Max(1, box.ScrollContentHeight - box.ContentBox.Height);
            float thumbY = trackY + (trackHeight - thumbHeight) * (box.ScrollY / scrollRange);
            var thumbOp = PaintOpPool.GetDrawRectOp();
            thumbOp.Rect = new SKRect(trackX + 2, thumbY + 1, trackX + scrollbarWidth - 2, thumbY + thumbHeight - 1);
            thumbOp.FillColor = new SKColor(180, 180, 180);
            thumbOp.Bounds = thumbOp.Rect;
            _displayList.Add(thumbOp);
        }

        // Horizontal scrollbar
        if (box.ScrollContentWidth > box.ContentBox.Width)
        {
            float trackWidth = paddingBox.Width;
            float thumbRatio = box.ContentBox.Width / box.ScrollContentWidth;
            float thumbWidth = Math.Max(20, trackWidth * thumbRatio);
            float trackX = paddingBox.Left;
            float trackY = paddingBox.Bottom - scrollbarWidth + TotalOffsetY;

            var trackOp = PaintOpPool.GetDrawRectOp();
            trackOp.Rect = new SKRect(trackX, trackY, trackX + trackWidth, trackY + scrollbarWidth);
            trackOp.FillColor = new SKColor(240, 240, 240);
            trackOp.Bounds = trackOp.Rect;
            _displayList.Add(trackOp);

            float scrollRange = Math.Max(1, box.ScrollContentWidth - box.ContentBox.Width);
            float thumbX = trackX + (trackWidth - thumbWidth) * (box.ScrollX / scrollRange);
            var thumbOp = PaintOpPool.GetDrawRectOp();
            thumbOp.Rect = new SKRect(thumbX + 1, trackY + 2, thumbX + thumbWidth - 1, trackY + scrollbarWidth - 2);
            thumbOp.FillColor = new SKColor(180, 180, 180);
            thumbOp.Bounds = thumbOp.Rect;
            _displayList.Add(thumbOp);
        }
    }

    private SKPoint ParseTransformOrigin(string? origin, LayoutBox box)
    {
        if (string.IsNullOrEmpty(origin))
            return new SKPoint(box.ContentBox.MidX, box.ContentBox.MidY);

        var parts = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float x = box.ContentBox.MidX;
        float y = box.ContentBox.MidY;

        if (parts.Length >= 1)
            x = ParseOriginValue(parts[0], box.ContentBox.Width, box.ContentBox.Left);
        if (parts.Length >= 2)
            y = ParseOriginValue(parts[1], box.ContentBox.Height, box.ContentBox.Top);

        return new SKPoint(x, y);
    }

    private float ParseOriginValue(string value, float size, float offset)
    {
        if (value.EndsWith("%"))
        {
            if (float.TryParse(value[..^1], out var pct))
                return offset + size * pct / 100f;
        }
        if (float.TryParse(value.Replace("px", ""), out var px))
            return offset + px;
        return offset + size / 2f;
    }

    private void DrawBackgroundImage(Element element, ComputedStyle style, SKRect rect)
    {
        var url = style.BackgroundImage;
        if (string.IsNullOrEmpty(url)) return;
        var task = _imageCache.GetImageAsync(url);
        task.Wait();
        var image = task.Result;
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

    private void DrawGradientBackground(ComputedStyle style, SKRect rect)
    {
        var shader = GradientRenderer.CreateGradient(style.BackgroundImage!, rect);
        if (shader == null) return;

        float maxRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));

        var path = new SKPath();
        if (maxRadius > 0)
            path.AddRoundRect(rect, maxRadius, maxRadius);
        else
            path.AddRect(rect);

        var op = PaintOpPool.GetDrawPathOp();
        op.Path.Dispose();
        op.Path = path;
        op.FillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Shader = shader,
            IsAntialias = true
        };
        op.Bounds = rect;
        _displayList.Add(op);
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
        op.BorderTopStyle = style.BorderTopStyle;
        op.BorderRightStyle = style.BorderRightStyle;
        op.BorderBottomStyle = style.BorderBottomStyle;
        op.BorderLeftStyle = style.BorderLeftStyle;
        op.Bounds = borderRect;
        _displayList.Add(op);
    }

    private void DrawElementOutline(Element element, LayoutBox box, ComputedStyle style, SKRect borderRect)
    {
        if (style.OutlineWidth <= 0 || style.OutlineStyle == BorderStyle.None) return;

        float offset = style.OutlineOffset;
        var outlineRect = new SKRect(
            borderRect.Left - offset - style.OutlineWidth,
            borderRect.Top - offset - style.OutlineWidth,
            borderRect.Right + offset + style.OutlineWidth,
            borderRect.Bottom + offset + style.OutlineWidth);

        using var paint = new SKPaint
        {
            Color = style.OutlineColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = style.OutlineWidth,
            IsAntialias = true
        };

        float maxRadius = Math.Max(style.BorderTopLeftRadius, Math.Max(style.BorderTopRightRadius,
            Math.Max(style.BorderBottomLeftRadius, style.BorderBottomRightRadius)));

        if (maxRadius > 0)
        {
            var op = PaintOpPool.GetDrawPathOp();
            var path = new SKPath();
            path.AddRoundRect(outlineRect, maxRadius + offset, maxRadius + offset);
            op.Path.Dispose();
            op.Path = path;
            op.StrokePaint = paint;
            op.Bounds = outlineRect;
            _displayList.Add(op);
        }
        else
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = outlineRect;
            op.BorderTopWidth = style.OutlineWidth;
            op.BorderRightWidth = style.OutlineWidth;
            op.BorderBottomWidth = style.OutlineWidth;
            op.BorderLeftWidth = style.OutlineWidth;
            op.BorderTopColor = style.OutlineColor;
            op.BorderRightColor = style.OutlineColor;
            op.BorderBottomColor = style.OutlineColor;
            op.BorderLeftColor = style.OutlineColor;
            op.Bounds = outlineRect;
            _displayList.Add(op);
        }
    }

    private void DrawInlineChildrenDecorations(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Element childElement && childElement.LayoutBox != null &&
                childElement.ComputedStyle?.Display == DisplayType.Inline)
            {
                var childBox = childElement.LayoutBox;
                var childStyle = childElement.ComputedStyle;
                var childBorderRect = new SKRect(
                    childBox.BorderBox.Left,
                    childBox.BorderBox.Top + TotalOffsetY,
                    childBox.BorderBox.Right,
                    childBox.BorderBox.Bottom + TotalOffsetY);

                bool childVisHidden = childStyle.Visibility == VisibilityType.Hidden;
                if (!childVisHidden)
                {
                    DrawElementBackground(childElement, childBox, childStyle, childBorderRect);
                    DrawElementBorder(childElement, childBox, childStyle, childBorderRect);
                    DrawElementOutline(childElement, childBox, childStyle, childBorderRect);
                }
            }
        }
    }

    private SKPath? ParseClipPath(string? clipPath, LayoutBox box)
    {
        if (string.IsNullOrEmpty(clipPath)) return null;

        var path = new SKPath();
        var rect = box.BorderBox;

        if (clipPath.StartsWith("circle("))
        {
            var inner = clipPath[7..^1];
            var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float radius = Math.Min(rect.Width, rect.Height) / 2f;
            float cx = rect.MidX, cy = rect.MidY;
            if (parts.Length >= 1)
            {
                var rStr = parts[0].Trim();
                if (rStr.EndsWith("%"))
                {
                    if (float.TryParse(rStr[..^1].Trim(), out var pct))
                        radius = Math.Min(rect.Width, rect.Height) * pct / 100f;
                }
                else
                {
                    float.TryParse(rStr.Replace("px", "").Trim(), out radius);
                }
            }
            // at keyword for center: circle(50% at 30% 40%)
            int atIdx = Array.FindIndex(parts, p => p.Equals("at", StringComparison.OrdinalIgnoreCase));
            if (atIdx >= 0 && parts.Length > atIdx + 2)
            {
                var cxStr = parts[atIdx + 1];
                if (cxStr.EndsWith("%"))
                {
                    if (float.TryParse(cxStr[..^1].Trim(), out var pct))
                        cx = rect.Left + rect.Width * pct / 100f;
                }
                else
                {
                    if (float.TryParse(cxStr.Replace("px", "").Trim(), out var v))
                        cx = rect.Left + v;
                }
                var cyStr = parts[atIdx + 2];
                if (cyStr.EndsWith("%"))
                {
                    if (float.TryParse(cyStr[..^1].Trim(), out var pct))
                        cy = rect.Top + rect.Height * pct / 100f;
                }
                else
                {
                    if (float.TryParse(cyStr.Replace("px", "").Trim(), out var v))
                        cy = rect.Top + v;
                }
            }
            path.AddCircle(cx, cy, Math.Max(0, radius));
        }
        else if (clipPath.StartsWith("ellipse("))
        {
            var inner = clipPath[8..^1];
            var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float rx = rect.Width / 2f, ry = rect.Height / 2f;
            float cx = rect.MidX, cy = rect.MidY;
            if (parts.Length >= 2)
            {
                var rxStr = parts[0];
                if (rxStr.EndsWith("%"))
                {
                    if (float.TryParse(rxStr[..^1].Trim(), out var pct))
                        rx = rect.Width * pct / 100f;
                }
                else
                {
                    float.TryParse(rxStr.Replace("px", "").Trim(), out rx);
                }
                var ryStr = parts[1];
                if (ryStr.EndsWith("%"))
                {
                    if (float.TryParse(ryStr[..^1].Trim(), out var pct))
                        ry = rect.Height * pct / 100f;
                }
                else
                {
                    float.TryParse(ryStr.Replace("px", "").Trim(), out ry);
                }
            }
            int atIdx = Array.FindIndex(parts, p => p.Equals("at", StringComparison.OrdinalIgnoreCase));
            if (atIdx >= 0 && parts.Length > atIdx + 2)
            {
                var cxStr = parts[atIdx + 1];
                if (cxStr.EndsWith("%"))
                {
                    if (float.TryParse(cxStr[..^1].Trim(), out var pct))
                        cx = rect.Left + rect.Width * pct / 100f;
                }
                else
                {
                    if (float.TryParse(cxStr.Replace("px", "").Trim(), out var v))
                        cx = rect.Left + v;
                }
                var cyStr = parts[atIdx + 2];
                if (cyStr.EndsWith("%"))
                {
                    if (float.TryParse(cyStr[..^1].Trim(), out var pct))
                        cy = rect.Top + rect.Height * pct / 100f;
                }
                else
                {
                    if (float.TryParse(cyStr.Replace("px", "").Trim(), out var v))
                        cy = rect.Top + v;
                }
            }
            path.AddOval(new SKRect(cx - Math.Max(0, rx), cy - Math.Max(0, ry), cx + Math.Max(0, rx), cy + Math.Max(0, ry)));
        }
        else if (clipPath.StartsWith("inset("))
        {
            var inner = clipPath[6..^1];
            // Split by spaces, handling round keyword
            var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            float top = 0, right = 0, bottom = 0, left = 0;
            int count = 0;
            foreach (var p in parts)
            {
                if (p.Equals("round", StringComparison.OrdinalIgnoreCase)) break;
                float val = 0;
                var trimmed = p.Replace("px", "").Trim();
                if (trimmed.EndsWith("%"))
                {
                    if (float.TryParse(trimmed[..^1].Trim(), out var pct))
                        val = pct / 100f;
                }
                else
                {
                    float.TryParse(trimmed, out val);
                }
                if (count == 0) top = val;
                else if (count == 1) right = val;
                else if (count == 2) bottom = val;
                else if (count == 3) left = val;
                count++;
            }
            if (count == 1) right = bottom = left = top;
            else if (count == 2) { bottom = top; left = right; }
            else if (count == 3) { left = right; }
            // Convert percentages to absolute values
            if (top < 1 && top >= 0 && parts[0].EndsWith("%")) top = rect.Height * top;
            if (right < 1 && right >= 0 && parts[Math.Min(1, parts.Count - 1)].EndsWith("%")) right = rect.Width * right;
            if (bottom < 1 && bottom >= 0 && parts[Math.Min(2, parts.Count - 1)].EndsWith("%")) bottom = rect.Height * bottom;
            if (left < 1 && left >= 0 && parts[Math.Min(3, parts.Count - 1)].EndsWith("%")) left = rect.Width * left;
            var insetRect = new SKRect(rect.Left + left, rect.Top + top, rect.Right - right, rect.Bottom - bottom);
            path.AddRect(insetRect);
        }
        else if (clipPath.StartsWith("polygon("))
        {
            var inner = clipPath[8..^1];
            var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
            // Skip optional "fill" or "nonzero"/"evenodd" keywords
            int firstPt = 0;
            if (parts.Length > 0)
            {
                var trimmed = parts[0].Trim();
                if (trimmed.Equals("fill", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("nonzero", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                    firstPt = 1;
            }
            bool first = true;
            for (int pi = firstPt; pi < parts.Length; pi++)
            {
                var coords = parts[pi].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (coords.Length < 2) continue;
                float x = 0, y = 0;
                if (coords[0].EndsWith("%"))
                {
                    if (float.TryParse(coords[0][..^1].Trim(), out var pct))
                        x = rect.Left + rect.Width * pct / 100f;
                }
                else
                {
                    float.TryParse(coords[0].Replace("px", "").Trim(), out x);
                    x += rect.Left;
                }
                if (coords[1].EndsWith("%"))
                {
                    if (float.TryParse(coords[1][..^1].Trim(), out var pct))
                        y = rect.Top + rect.Height * pct / 100f;
                }
                else
                {
                    float.TryParse(coords[1].Replace("px", "").Trim(), out y);
                    y += rect.Top;
                }
                if (first) { path.MoveTo(x, y); first = false; }
                else path.LineTo(x, y);
            }
            path.Close();
        }
        else if (clipPath.StartsWith("rect("))
        {
            var inner = clipPath[5..^1];
            var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
            float top = 0, right = rect.Width, bottom = rect.Height, left = 0;
            if (parts.Length >= 1) float.TryParse(parts[0].Replace("px", "").Trim(), out top);
            if (parts.Length >= 2) float.TryParse(parts[1].Replace("px", "").Trim(), out right);
            if (parts.Length >= 3) float.TryParse(parts[2].Replace("px", "").Trim(), out bottom);
            if (parts.Length >= 4) float.TryParse(parts[3].Replace("px", "").Trim(), out left);
            var r = new SKRect(rect.Left + left, rect.Top + top, rect.Left + right, rect.Top + bottom);
            path.AddRect(r);
        }
        else
        {
            path.Dispose();
            return null;
        }

        return path;
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
            var inputType = element.InputType?.ToLowerInvariant();
            if (inputType == "checkbox" || inputType == "radio")
            {
                DrawCheckRadioElement(element, box, style, inputType);
                return;
            }
            if (inputType == "range")
            {
                DrawRangeElement(element, box, style);
                return;
            }
            if (inputType == "color")
            {
                DrawColorInputElement(element, box, style);
                return;
            }
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
        if (element.TagName.Equals("PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            DrawProgressElement(element, box, style);
            return;
        }
        if (element.TagName.Equals("METER", StringComparison.OrdinalIgnoreCase))
        {
            DrawMeterElement(element, box, style);
            return;
        }
        if (style.Display == DisplayType.Inline)
            return;
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
        textOp.Italic = style.FontStyle == FontStyleType.Italic || style.FontStyle == FontStyleType.Oblique;
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
        var contentBox = box.ContentBox;
        float textY = contentBox.Top + fontSize * 0.85f;
        SKColor textColor = showPlaceholder ? new SKColor(160, 160, 160) : (style.Color.Alpha > 0 ? style.Color : SKColors.Black);
        float textX = contentBox.Left + 2;
        float usableWidth = contentBox.Width - 4;
        bool isFocused = _focusedElement == element;

        // Determine the effective text to display and cursor/selection positions
        string effectText = isFocused && _inputImeComposing
            ? displayText[..Math.Min(_inputCursorPos, displayText.Length)] + _inputImeComposition +
              displayText[Math.Min(_inputCursorPos, displayText.Length)..]
            : displayText;

        int cursorPos = isFocused && _inputImeComposing
            ? Math.Min(_inputCursorPos, displayText.Length) + Math.Min(_inputImeCursor, _inputImeComposition.Length)
            : isFocused ? _inputCursorPos : 0;

        int selStart = isFocused ? _inputSelStart : -1;

        // Measure widths
        float fullTextWidth = MeasureTextWidth(effectText, fontSize, style.FontFamily);

        // Horizontal scroll offset: keep cursor visible (stateless, cursor at ~33% from left)
        float scrollOffset = 0;
        if (isFocused && fullTextWidth > usableWidth)
        {
            float cursorWidth = MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily);
            float desiredOffset = cursorWidth - usableWidth * 0.33f;
            scrollOffset = Math.Clamp(desiredOffset, 0, Math.Max(0, fullTextWidth - usableWidth));
        }

        // Draw selection background
        if (isFocused && selStart >= 0 && selStart != cursorPos)
        {
            int a = Math.Min(selStart, cursorPos);
            int b = Math.Max(selStart, cursorPos);
            a = Math.Min(a, effectText.Length);
            b = Math.Min(b, effectText.Length);
            string beforeSel = effectText[..a];
            string selStr = effectText[a..b];
            float selX = textX + MeasureTextWidth(beforeSel, fontSize, style.FontFamily) - scrollOffset;
            float selW = MeasureTextWidth(selStr, fontSize, style.FontFamily);
            float clampLeft = Math.Max(textX, selX);
            float clampRight = Math.Min(textX + usableWidth, selX + selW);
            if (clampRight > clampLeft)
            {
                var selOp = PaintOpPool.GetDrawRectOp();
                selOp.Rect = new SKRect(clampLeft, contentBox.Top + TotalOffsetY + 1,
                    clampRight, contentBox.Bottom + TotalOffsetY - 1);
                selOp.FillColor = new SKColor(0x1A, 0x73, 0xE8);
                selOp.Bounds = selOp.Rect;
                _displayList.Add(selOp);
            }
        }

        // Draw text (clipped to content area)
        float drawTextX = textX - scrollOffset;
        var textOp = PaintOpPool.GetDrawTextOp();
        textOp.Text = effectText;
        textOp.X = drawTextX;
        textOp.Y = textY + TotalOffsetY;
        textOp.Color = textColor;
        textOp.FontSize = fontSize;
        textOp.FontFamily = style.FontFamily ?? "Arial";
        textOp.FontWeight = style.FontWeight;
        textOp.Italic = style.FontStyle == FontStyleType.Italic || style.FontStyle == FontStyleType.Oblique;
        textOp.Bounds = new SKRect(contentBox.Left, contentBox.Top + TotalOffsetY,
            contentBox.Right, contentBox.Bottom + TotalOffsetY);
        _displayList.Add(textOp);

        // IME composition underline
        if (isFocused && _inputImeComposing && _inputImeComposition.Length > 0)
        {
            float compStartX = textX + MeasureTextWidth(effectText[..Math.Min(_inputCursorPos, displayText.Length)], fontSize, style.FontFamily) - scrollOffset;
            float compWidth = MeasureTextWidth(_inputImeComposition, fontSize, style.FontFamily);
            float compY = contentBox.Bottom + TotalOffsetY - 2;
            var lineOp = PaintOpPool.GetDrawLineOp();
            lineOp.X1 = Math.Max(contentBox.Left, compStartX);
            lineOp.Y1 = compY;
            lineOp.X2 = Math.Min(contentBox.Right, compStartX + compWidth);
            lineOp.Y2 = compY;
            lineOp.Color = new SKColor(0, 0, 0);
            lineOp.StrokeWidth = 1;
            lineOp.Bounds = new SKRect(lineOp.X1, compY - 1, lineOp.X2, compY + 1);
            _displayList.Add(lineOp);
        }

        // Draw cursor
        if (isFocused && _inputShowCursor && !_inputImeComposing)
        {
            float cursorWidth = MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily);
            float cursorX = textX + cursorWidth - scrollOffset;
            cursorX = Math.Clamp(cursorX, textX, textX + usableWidth);
            float cursorTop = contentBox.Top + TotalOffsetY + 2;
            float cursorBottom = contentBox.Bottom + TotalOffsetY - 2;
            var caretColor = style.CaretColor ?? new SKColor(0, 0, 0);
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = cursorX;
            cursorOp.Y1 = cursorTop;
            cursorOp.X2 = cursorX;
            cursorOp.Y2 = cursorBottom;
            cursorOp.Color = caretColor;
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(cursorX - 1, cursorTop, cursorX + 1, cursorBottom);
            _displayList.Add(cursorOp);
        }

        // IME composition cursor
        if (isFocused && _inputImeComposing)
        {
            float imeCursorX = textX + MeasureTextWidth(effectText[..Math.Min(cursorPos, effectText.Length)], fontSize, style.FontFamily) - scrollOffset;
            imeCursorX = Math.Clamp(imeCursorX, textX, textX + usableWidth);
            float cursorTop = contentBox.Top + TotalOffsetY + 2;
            float cursorBottom = contentBox.Bottom + TotalOffsetY - 2;
            var caretColor = style.CaretColor ?? new SKColor(0, 0, 0);
            var cursorOp = PaintOpPool.GetDrawLineOp();
            cursorOp.X1 = imeCursorX;
            cursorOp.Y1 = cursorTop;
            cursorOp.X2 = imeCursorX;
            cursorOp.Y2 = cursorBottom;
            cursorOp.Color = caretColor;
            cursorOp.StrokeWidth = 1.5f;
            cursorOp.Bounds = new SKRect(imeCursorX - 1, cursorTop, imeCursorX + 1, cursorBottom);
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

    private void DrawCheckRadioElement(Element element, LayoutBox box, ComputedStyle style, string inputType)
    {
        bool isChecked = element.HasAttribute("checked");
        var contentBox = box.ContentBox;
        float size = Math.Min(contentBox.Width, contentBox.Height);
        float cx = contentBox.Left + contentBox.Width / 2;
        float cy = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
        float boxSize = Math.Min(size, 16);
        float halfBox = boxSize / 2;

        SKColor borderColor = new SKColor(120, 120, 120);
        SKColor fillColor = isChecked ? new SKColor(0x1A, 0x73, 0xE8) : SKColors.White;

        if (inputType == "checkbox")
        {
            var rect = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = CreateRoundedRectPath(rect, 3);
            bgOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.StrokePaint = new SKPaint { Color = borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            bgOp.Bounds = rect;
            _displayList.Add(bgOp);

            if (isChecked)
            {
                var checkPath = new SKPath();
                checkPath.MoveTo(cx - halfBox * 0.5f, cy);
                checkPath.LineTo(cx - halfBox * 0.1f, cy + halfBox * 0.4f);
                checkPath.LineTo(cx + halfBox * 0.5f, cy - halfBox * 0.35f);
                var checkOp = PaintOpPool.GetDrawPathOp();
                checkOp.Path = checkPath;
                checkOp.StrokePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
                checkOp.Bounds = rect;
                _displayList.Add(checkOp);
            }
        }
        else // radio
        {
            var bgOp = PaintOpPool.GetDrawPathOp();
            bgOp.Path = new SKPath();
            bgOp.Path.AddCircle(cx, cy, halfBox);
            bgOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            bgOp.StrokePaint = new SKPaint { Color = borderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            bgOp.Bounds = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
            _displayList.Add(bgOp);

            if (isChecked)
            {
                var dotOp = PaintOpPool.GetDrawPathOp();
                dotOp.Path = new SKPath();
                dotOp.Path.AddCircle(cx, cy, halfBox * 0.45f);
                dotOp.FillPaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8), Style = SKPaintStyle.Fill, IsAntialias = true };
                dotOp.Bounds = new SKRect(cx - halfBox, cy - halfBox, cx + halfBox, cy + halfBox);
                _displayList.Add(dotOp);
            }
        }
    }

    private void DrawRangeElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        float trackY = contentBox.Top + contentBox.Height / 2 + TotalOffsetY;
        float trackLeft = contentBox.Left + 4;
        float trackRight = contentBox.Right - 4;

        var trackOp = PaintOpPool.GetDrawLineOp();
        trackOp.X1 = trackLeft;
        trackOp.Y1 = trackY;
        trackOp.X2 = trackRight;
        trackOp.Y2 = trackY;
        trackOp.Color = new SKColor(200, 200, 200);
        trackOp.StrokeWidth = 4;
        trackOp.Bounds = new SKRect(trackLeft, trackY - 2, trackRight, trackY + 2);
        _displayList.Add(trackOp);

        float min = 0, max = 100, val = 50;
        float.TryParse(element.GetAttribute("min") ?? "0", out min);
        float.TryParse(element.GetAttribute("max") ?? "100", out max);
        float.TryParse(element.GetAttribute("value") ?? "50", out val);
        float ratio = max > min ? (val - min) / (max - min) : 0.5f;
        float thumbX = trackLeft + (trackRight - trackLeft) * ratio;

        var filledOp = PaintOpPool.GetDrawLineOp();
        filledOp.X1 = trackLeft;
        filledOp.Y1 = trackY;
        filledOp.X2 = thumbX;
        filledOp.Y2 = trackY;
        filledOp.Color = new SKColor(0x1A, 0x73, 0xE8);
        filledOp.StrokeWidth = 4;
        filledOp.Bounds = new SKRect(trackLeft, trackY - 2, thumbX, trackY + 2);
        _displayList.Add(filledOp);

        float thumbRadius = 8;
        var thumbOp = PaintOpPool.GetDrawPathOp();
        thumbOp.Path = new SKPath();
        thumbOp.Path.AddCircle(thumbX, trackY, thumbRadius);
        thumbOp.FillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        thumbOp.StrokePaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        thumbOp.Bounds = new SKRect(thumbX - thumbRadius, trackY - thumbRadius, thumbX + thumbRadius, trackY + thumbRadius);
        _displayList.Add(thumbOp);
    }

    private void DrawColorInputElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        string colorStr = element.GetAttribute("value") ?? "#000000";
        SKColor color;
        try { color = SKColor.Parse(colorStr); } catch { color = SKColors.Black; }

        float swatchSize = Math.Min(contentBox.Width, contentBox.Height) - 4;
        float x = contentBox.Left + (contentBox.Width - swatchSize) / 2;
        float y = contentBox.Top + (contentBox.Height - swatchSize) / 2 + TotalOffsetY;

        var borderOp = PaintOpPool.GetDrawPathOp();
        borderOp.Path = CreateRoundedRectPath(new SKRect(x - 1, y - 1, x + swatchSize + 1, y + swatchSize + 1), 3);
        borderOp.StrokePaint = new SKPaint { Color = new SKColor(120, 120, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        borderOp.Bounds = new SKRect(x - 1, y - 1, x + swatchSize + 1, y + swatchSize + 1);
        _displayList.Add(borderOp);

        var fillOp = PaintOpPool.GetDrawPathOp();
        fillOp.Path = CreateRoundedRectPath(new SKRect(x, y, x + swatchSize, y + swatchSize), 2);
        fillOp.FillPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        fillOp.Bounds = new SKRect(x, y, x + swatchSize, y + swatchSize);
        _displayList.Add(fillOp);
    }

    private void DrawProgressElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        float radius = contentBox.Height / 2;
        float barY = contentBox.Top + TotalOffsetY;

        var bgPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height), radius);
        var bgOp = PaintOpPool.GetDrawPathOp();
        bgOp.Path = bgPath;
        bgOp.FillPaint = new SKPaint { Color = new SKColor(220, 220, 220), Style = SKPaintStyle.Fill, IsAntialias = true };
        bgOp.Bounds = new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height);
        _displayList.Add(bgOp);

        float max = 1, val = 0;
        float.TryParse(element.GetAttribute("max") ?? "1", out max);
        float.TryParse(element.GetAttribute("value") ?? "0", out val);
        float ratio = max > 0 ? Math.Clamp(val / max, 0, 1) : 0;
        float fillRight = contentBox.Left + contentBox.Width * ratio;

        if (ratio > 0)
        {
            var fillPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height), radius);
            var fillOp = PaintOpPool.GetDrawPathOp();
            fillOp.Path = fillPath;
            fillOp.FillPaint = new SKPaint { Color = new SKColor(0x1A, 0x73, 0xE8), Style = SKPaintStyle.Fill, IsAntialias = true };
            fillOp.Bounds = new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height);
            _displayList.Add(fillOp);
        }
    }

    private void DrawMeterElement(Element element, LayoutBox box, ComputedStyle style)
    {
        var contentBox = box.ContentBox;
        float radius = contentBox.Height / 2;
        float barY = contentBox.Top + TotalOffsetY;

        var bgPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height), radius);
        var bgOp = PaintOpPool.GetDrawPathOp();
        bgOp.Path = bgPath;
        bgOp.FillPaint = new SKPaint { Color = new SKColor(220, 220, 220), Style = SKPaintStyle.Fill, IsAntialias = true };
        bgOp.Bounds = new SKRect(contentBox.Left, barY, contentBox.Right, barY + contentBox.Height);
        _displayList.Add(bgOp);

        float min = 0, max = 1, low = float.NaN, high = float.NaN, optimum = float.NaN;
        float.TryParse(element.GetAttribute("min") ?? "0", out min);
        float.TryParse(element.GetAttribute("max") ?? "1", out max);
        float val = 0;
        float.TryParse(element.GetAttribute("value") ?? "0", out val);
        float ratio = max > min ? Math.Clamp((val - min) / (max - min), 0, 1) : 0;
        float fillRight = contentBox.Left + contentBox.Width * ratio;

        SKColor fillColor = new SKColor(0x1A, 0x73, 0xE8);
        if (!float.TryParse(element.GetAttribute("low") ?? "", out float lowVal)) lowVal = min;
        if (!float.TryParse(element.GetAttribute("high") ?? "", out float highVal)) highVal = max;
        if (!float.TryParse(element.GetAttribute("optimum") ?? "", out float optVal)) optVal = (min + max) / 2;

        if (val < lowVal || val > highVal)
            fillColor = new SKColor(220, 50, 50);
        else if ((optVal >= lowVal && val >= optVal) || (optVal <= highVal && val <= optVal))
            fillColor = new SKColor(0x0B, 0x80, 0x43);
        else
            fillColor = new SKColor(0xF4, 0xB4, 0x00);

        if (ratio > 0)
        {
            var fillPath = CreateRoundedRectPath(new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height), radius);
            var fillOp = PaintOpPool.GetDrawPathOp();
            fillOp.Path = fillPath;
            fillOp.FillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            fillOp.Bounds = new SKRect(contentBox.Left, barY, fillRight, barY + contentBox.Height);
            _displayList.Add(fillOp);
        }
    }

    private void DrawTextNode(TextNode textNode, LayoutBox box, ComputedStyle parentStyle)
    {
        var text = textNode.TextContent;
        if (string.IsNullOrEmpty(text)) return;
        text = ApplyTextTransform(text, parentStyle?.TextTransform);
        var contentBox = box.ContentBox;
        float y = contentBox.Top + (parentStyle?.FontSize ?? 16) + TotalOffsetY;
        var textColor = parentStyle?.Color ?? SKColors.Black;
        if (parentStyle != null && parentStyle.Opacity < 1.0f)
            textColor = textColor.WithAlpha((byte)(textColor.Alpha * parentStyle.Opacity));
        float textWidth = MeasureTextWidth(text, parentStyle?.FontSize ?? 16, parentStyle?.FontFamily ?? "Arial", parentStyle?.FontWeight ?? FontWeight.Normal);
        float textX = contentBox.Left;
        if (parentStyle?.TextAlign == TextAlignType.Center)
            textX = contentBox.Left + contentBox.Width / 2;
        else if (parentStyle?.TextAlign == TextAlignType.Right || parentStyle?.TextAlign == TextAlignType.End)
            textX = contentBox.Right;
        var op = PaintOpPool.GetDrawTextOp();
        op.Text = text;
        op.X = textX;
        op.Y = y;
        op.Color = textColor;
        op.FontSize = parentStyle?.FontSize ?? 16;
        op.FontFamily = parentStyle?.FontFamily ?? "Arial";
        op.FontWeight = parentStyle?.FontWeight ?? FontWeight.Normal;
        op.TextAlign = parentStyle?.TextAlign ?? TextAlignType.Start;
        op.Underline = parentStyle?.TextDecorationLine == TextDecorationLineType.Underline || parentStyle?.TextDecoration == TextDecorationType.Underline;
        op.LineThrough = parentStyle?.TextDecorationLine == TextDecorationLineType.LineThrough || parentStyle?.TextDecoration == TextDecorationType.LineThrough;
        op.Overline = parentStyle?.TextDecorationLine == TextDecorationLineType.Overline || parentStyle?.TextDecoration == TextDecorationType.Overline;
        if (parentStyle != null) op.UnderlineColor = parentStyle.TextDecorationColor;
        op.DecorationStyle = parentStyle?.TextDecorationStyle ?? TextDecorationStyleType.Solid;
        op.LetterSpacing = parentStyle?.LetterSpacing ?? 0;
        op.Italic = parentStyle?.FontStyle == FontStyleType.Italic || parentStyle?.FontStyle == FontStyleType.Oblique;
        if (parentStyle?.TextShadow != null && parentStyle.TextShadow.Count > 0)
            op.TextShadows = parentStyle.TextShadow;
        float boundTop = y - (parentStyle?.FontSize ?? 16);
        float boundBottom = y;
        op.Bounds = new SKRect(textX, boundTop, textX + textWidth, boundBottom);

        // Add selection highlight clipped to the overlapping region
        var selHighlight = GetSelHighlight(textNode, text, op.Bounds,
            op.FontSize, op.FontFamily, op.FontWeight);
        if (selHighlight.HasValue)
        {
            var highlightOp = PaintOpPool.GetDrawRectOp();
            highlightOp.Rect = selHighlight.Value;
            highlightOp.FillColor = new SKColor(0x1A, 0x73, 0xE8, 0x40);
            highlightOp.BorderTopWidth = 0;
            highlightOp.BorderBottomWidth = 0;
            highlightOp.BorderLeftWidth = 0;
            highlightOp.BorderRightWidth = 0;
            highlightOp.Bounds = selHighlight.Value;
            _displayList.Add(highlightOp);
        }

        _displayList.Add(op);
    }

    private void DrawImageElement(Element element, ComputedStyle style, LayoutBox box)
    {
        var src = element.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return;
        var resolvedSrc = ResolveImageUrl(src);
        if (resolvedSrc == null) return;
        var task = _imageCache.GetImageAsync(resolvedSrc);
        task.Wait();
        var image = task.Result;
        if (image == null) return;
        var rect = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom);
        var op = PaintOpPool.GetDrawImageOp();
        op.Image = image;
        op.SourceRect = new SKRect(0, 0, image.Width, image.Height);
        op.DestRect = rect;
        op.Fit = MapObjectFitToImageFit(style.ObjectFit);
        op.ZIndex = element.ComputedStyle?.ZIndex ?? 0;
        op.Bounds = rect;
        _displayList.Add(op);
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

    private static string ApplyTextTransform(string text, string? transform)
    {
        if (string.IsNullOrEmpty(transform) || transform == "none") return text;
        return transform.ToLowerInvariant() switch
        {
            "uppercase" => text.ToUpperInvariant(),
            "lowercase" => text.ToLowerInvariant(),
            "capitalize" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            _ => text
        };
    }

    private static ImageFit MapObjectFitToImageFit(ObjectFitType objectFit) => objectFit switch
    {
        ObjectFitType.Fill => ImageFit.Fill,
        ObjectFitType.Contain => ImageFit.Contain,
        ObjectFitType.Cover => ImageFit.Cover,
        ObjectFitType.None => ImageFit.None,
        ObjectFitType.ScaleDown => ImageFit.ScaleDown,
        _ => ImageFit.Fill
    };

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
            TextNode? lastTextNode = null;
            int runStartOffset = 0;
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
                        if (textNode != lastTextNode)
                        {
                            runStartOffset = 0;
                            lastTextNode = textNode;
                        }

                        var parentStyle = textNode.ParentElement?.ComputedStyle;
                        var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;
                        var runText = ApplyTextTransform(run.Text, parentStyle?.TextTransform);
                        float runY = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                        var op = PaintOpPool.GetDrawTextOp();
                        op.Text = runText;
                        op.X = currentX + lineOffsetX;
                        op.Y = runY;
                        op.Color = run.Color ?? parentStyle?.Color ?? SKColors.Black;
                        op.FontSize = actualFontSize;
                        op.FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial";
                        op.FontWeight = run.FontWeight;
                        op.Underline = parentStyle?.TextDecorationLine == TextDecorationLineType.Underline || parentStyle?.TextDecoration == TextDecorationType.Underline;
                        op.LineThrough = parentStyle?.TextDecorationLine == TextDecorationLineType.LineThrough || parentStyle?.TextDecoration == TextDecorationType.LineThrough;
                        op.Overline = parentStyle?.TextDecorationLine == TextDecorationLineType.Overline || parentStyle?.TextDecoration == TextDecorationType.Overline;
                        if (parentStyle != null) op.UnderlineColor = parentStyle.TextDecorationColor;
                        op.DecorationStyle = parentStyle?.TextDecorationStyle ?? TextDecorationStyleType.Solid;
                        op.LetterSpacing = parentStyle?.LetterSpacing ?? 0;
                        op.Italic = parentStyle?.FontStyle == FontStyleType.Italic || parentStyle?.FontStyle == FontStyleType.Oblique;
                        if (parentStyle?.TextShadow != null && parentStyle.TextShadow.Count > 0)
                            op.TextShadows = parentStyle.TextShadow;
                        op.Bounds = new SKRect(currentX + lineOffsetX, lineY, currentX + run.Width + lineOffsetX, lineY + line.Height);

                        // Add selection highlight clipped to the overlapping region
                        var selHighlight = GetSelHighlight(textNode, runText, op.Bounds,
                            op.FontSize, op.FontFamily, op.FontWeight, runStartOffset);
                        if (selHighlight.HasValue)
                        {
                            var highlightOp = PaintOpPool.GetDrawRectOp();
                            highlightOp.Rect = selHighlight.Value;
                            highlightOp.FillColor = new SKColor(0x1A, 0x73, 0xE8, 0x40);
                            highlightOp.BorderTopWidth = 0;
                            highlightOp.BorderBottomWidth = 0;
                            highlightOp.BorderLeftWidth = 0;
                            highlightOp.BorderRightWidth = 0;
                            highlightOp.Bounds = selHighlight.Value;
                            _displayList.Add(highlightOp);
                        }

                        _displayList.Add(op);
                        runStartOffset += runText.Length;
                    }
                    else
                    {
                        lastTextNode = null;
                        runStartOffset = 0;
                    }
                    currentX += run.Width;
                }
                currentX = box.ContentBox.Left;
            }
        }

        else if (box.LineRuns != null)
        {
            TextNode? lastTextNode = null;
            int runStartOffset = 0;
            float x = box.ContentBox.Left;
            float fontSize = box.LineRuns.FirstOrDefault()?.FontSize ?? 16;
            float baseline = boxTop + fontSize * 0.85f;
            foreach (var run in box.LineRuns)
            {
                if (run.IsText && run.Node is TextNode textNode)
                {
                    if (textNode != lastTextNode)
                    {
                        runStartOffset = 0;
                        lastTextNode = textNode;
                    }

                    var parentStyle = textNode.ParentElement?.ComputedStyle;
                    var actualFontSize = run.FontSize ?? parentStyle?.FontSize ?? 16;
                    var runText = ApplyTextTransform(run.Text, parentStyle?.TextTransform);
                    float runY = run.Baseline > 0 ? run.Baseline + TotalOffsetY : baseline;
                    var op = PaintOpPool.GetDrawTextOp();
                    op.Text = runText;
                    op.X = x;
                    op.Y = runY;
                    op.Color = run.Color ?? parentStyle?.Color ?? SKColors.Black;
                    op.FontSize = actualFontSize;
                    op.FontFamily = run.FontFamily ?? parentStyle?.FontFamily ?? "Arial";
                    op.FontWeight = run.FontWeight;
                    op.Underline = parentStyle?.TextDecorationLine == TextDecorationLineType.Underline || parentStyle?.TextDecoration == TextDecorationType.Underline;
                    op.LineThrough = parentStyle?.TextDecorationLine == TextDecorationLineType.LineThrough || parentStyle?.TextDecoration == TextDecorationType.LineThrough;
                    op.Overline = parentStyle?.TextDecorationLine == TextDecorationLineType.Overline || parentStyle?.TextDecoration == TextDecorationType.Overline;
                    if (parentStyle != null) op.UnderlineColor = parentStyle.TextDecorationColor;
                    op.DecorationStyle = parentStyle?.TextDecorationStyle ?? TextDecorationStyleType.Solid;
                    op.LetterSpacing = parentStyle?.LetterSpacing ?? 0;
                    op.Italic = parentStyle?.FontStyle == FontStyleType.Italic || parentStyle?.FontStyle == FontStyleType.Oblique;
                    if (parentStyle?.TextShadow != null && parentStyle.TextShadow.Count > 0)
                        op.TextShadows = parentStyle.TextShadow;
                    op.Bounds = new SKRect(x, boxTop, x + run.Width, boxTop + run.Height);

                    // Add selection highlight clipped to the overlapping region
                    var selHighlight = GetSelHighlight(textNode, runText, op.Bounds,
                        op.FontSize, op.FontFamily, op.FontWeight, runStartOffset);
                    if (selHighlight.HasValue)
                    {
                        var highlightOp = PaintOpPool.GetDrawRectOp();
                        highlightOp.Rect = selHighlight.Value;
                        highlightOp.FillColor = new SKColor(0x1A, 0x73, 0xE8, 0x40);
                        highlightOp.BorderTopWidth = 0;
                        highlightOp.BorderBottomWidth = 0;
                        highlightOp.BorderLeftWidth = 0;
                        highlightOp.BorderRightWidth = 0;
                        highlightOp.Bounds = selHighlight.Value;
                        _displayList.Add(highlightOp);
                    }

                    _displayList.Add(op);
                    runStartOffset += runText.Length;
                }
                else
                {
                    lastTextNode = null;
                    runStartOffset = 0;
                }
                x += run.Width;
            }
        }
    }
}

public class ImageCache
{
    private readonly Dictionary<string, SKImage> _cache = new();
    private readonly Dictionary<string, Task<SKImage?>> _pendingLoads = new();
    private readonly LinkedList<string> _accessOrder = new();
    private readonly HttpClient _httpClient = new();
    private readonly object _lock = new();
    private const int MaxCacheSize = 200;

    // Performance-integrated storage layers:
    //  - DecodedImagePool: byte-budgeted LRU of decoded SKImages (the hot path)
    //  - ResourceCache:    byte-budgeted LRU of raw HTTP bodies (re-decode source)
    //  - StreamingHttpFetcher: priority-aware HTTP client with inflight dedup
    private readonly DecodedImagePool _decodedPool = new();
    private readonly ResourceCache _resourceCache = new();
    private readonly StreamingHttpFetcher _fetcher;

    public DecodedImagePool DecodedPool => _decodedPool;
    public ResourceCache ResourceCache => _resourceCache;
    public StreamingHttpFetcher Fetcher => _fetcher;

    public ImageCache()
    {
        _decodedPool.SetCapacity(64L * 1024 * 1024); // 64 MB decoded budget
        _resourceCache.SetCapacity(32L * 1024 * 1024); // 32 MB raw body budget
        _fetcher = new StreamingHttpFetcher(_httpClient, _resourceCache, new PriorityResourceQueue());
    }

    public async Task<SKImage?> GetImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Hot path: decoded pool hit
        var pooled = _decodedPool.Get(url);
        if (pooled != null)
        {
            PipelineTimings.ImageCacheHits.AddSample(1);
            return pooled;
        }

        // Fallback: legacy dictionary cache hit (kept for stability)
        SKImage? cachedImage = null;
        Task<SKImage?>? pendingTask = null;

        lock (_lock)
        {
            if (_cache.TryGetValue(url, out cachedImage))
            {
                _accessOrder.Remove(url);
                _accessOrder.AddFirst(url);
            }
            else if (!_pendingLoads.TryGetValue(url, out pendingTask))
            {
                pendingTask = LoadImageAsync(url);
                _pendingLoads[url] = pendingTask;
            }
        }

        if (cachedImage != null)
        {
            // Promote into the byte-budgeted pool
            _decodedPool.Put(url, cachedImage, cachedImage.Width, cachedImage.Height);
            return cachedImage;
        }
        if (pendingTask == null) return null;

        try
        {
            var image = await pendingTask;
            lock (_lock)
            {
                _pendingLoads.Remove(url);
                if (image != null)
                {
                    _cache[url] = image;
                    _accessOrder.AddFirst(url);
                    EvictIfNeeded();

                    // Promote into the byte-budgeted pool with actual decoded size
                    _decodedPool.Put(url, image, image.Width, image.Height);
                    PipelineTimings.ImagesDecoded.AddSample(1);
                }
            }
            return image;
        }
        catch
        {
            lock (_lock) _pendingLoads.Remove(url);
            return null;
        }
    }

    private async Task<SKImage?> LoadImageAsync(string url)
    {
        var sw = Clock.NowNanos();
        try
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                var ext = Path.GetExtension(url).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".bmp" || ext == ".ico")
                {
                    var data = await File.ReadAllBytesAsync(url);
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(data);
                }
                return null;
            }
            else
            {
                // Fast path: check the resource cache for the raw body first
                ResourceResponse? resp = null;
                if (_resourceCache.TryGet(url, out resp) && resp != null)
                {
                    PipelineTimings.ResourceCacheHits.AddSample(1);
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(resp.Body);
                }

                // Slow path: go through the streaming fetcher (which also caches)
                var request = new ResourceRequest
                {
                    Url = url,
                    Kind = ResourceKind.Image,
                    Priority = ResourcePriority.Medium,
                };
                var fetched = await _fetcher.FetchAsync(request);
                if (fetched.Body != null && fetched.Body.Length > 0)
                {
                    PipelineTimings.ImageDecode.AddSample(Clock.NowNanos() - sw);
                    return SKImage.FromEncodedData(fetched.Body);
                }
                return null;
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
        _decodedPool.Clear();
        _resourceCache.Clear();
    }

}