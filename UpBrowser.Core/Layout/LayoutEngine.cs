using UpBrowser.Core.Dom;
using SkiaSharp;
using System.Text;

namespace UpBrowser.Core.Layout;

public static class LayoutMath
{
    public static float RoundToDevicePixel(float value, float dpiScale = 1.0f)
    {
        if (dpiScale <= 0) dpiScale = 1.0f;
        float physical = value * dpiScale;
        float roundedPhysical = MathF.Round(physical);
        return roundedPhysical / dpiScale;
    }

    public static SKRect RoundRect(SKRect rect, float dpiScale = 1.0f)
    {
        return new SKRect(
            RoundToDevicePixel(rect.Left, dpiScale),
            RoundToDevicePixel(rect.Top, dpiScale),
            RoundToDevicePixel(rect.Right, dpiScale),
            RoundToDevicePixel(rect.Bottom, dpiScale)
        );
    }
}

public class LayoutEngine
{
    private float _viewportWidth;
    private float _viewportHeight;
    private float _contentHeight;
    private float _rootFontSize = 16;
    private float _dpiScale = 1.0f;

    public void Layout(Document document, float width, float height, float dpiScale = 1.0f)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _dpiScale = dpiScale;
        _contentHeight = 0;

        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        ClearLayoutBoxes(root);

        var rootBox = CreateLayoutBox(root, 0, 0, width, null);
        if (rootBox != null)
        {
            CalculateContentHeight(rootBox);
        }
    }

    private void ClearLayoutBoxes(Element element)
    {
        element.LayoutBox = null;
        foreach (var child in element.Children)
        {
            if (child is Element childElement)
                ClearLayoutBoxes(childElement);
        }
    }

    private void CalculateContentHeight(LayoutBox box)
    {
        foreach (var child in box.Children)
            CalculateContentHeight(child);
        if (box.MarginBox.Bottom > _contentHeight)
            _contentHeight = box.MarginBox.Bottom;
    }

    private string GetButtonTextFromElement(Element button)
    {
        var textBuilder = new StringBuilder();
        CollectTextNodes(button, textBuilder);
        string result = textBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(result))
            return result;
        var valueAttr = button.GetAttribute("value");
        return !string.IsNullOrEmpty(valueAttr) ? valueAttr : "Button";
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

    private float GetAbsoluteWidth(ComputedStyle style, LayoutBox? containingBlock, float fontSize)
    {
        float result = float.NaN;
        if (style.Width is PixelLength px) result = px.Value;
        else if (style.Width is PercentLength pct && containingBlock != null)
            result = pct.Value * containingBlock.ContentBox.Width;
        else if (style.Width is MathLength ml)
        {
            float refWidth = containingBlock?.ContentBox.Width ?? 0;
            result = ml.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        }
        else if (containingBlock != null && !(style.Left is AutoLength) && !(style.Right is AutoLength))
        {
            float left = Length.ToPixelsOrDefault(style.Left, fontSize, _viewportWidth, _viewportHeight);
            float right = Length.ToPixelsOrDefault(style.Right, fontSize, _viewportWidth, _viewportHeight);
            result = containingBlock.ContentBox.Width - left - right;
        }

        if (!float.IsNaN(result))
        {
            if (style.MinWidth is PixelLength minW) result = Math.Max(result, minW.Value);
            if (style.MaxWidth is PixelLength maxW) result = Math.Min(result, maxW.Value);
        }
        return result;
    }

    private float GetAbsoluteHeight(ComputedStyle style, LayoutBox? containingBlock, float fontSize, float parentHeight)
    {
        float result = float.NaN;
        if (style.Height is PixelLength px) result = px.Value;
        else if (style.Height is PercentLength pct && containingBlock != null)
            result = pct.Value * containingBlock.ContentBox.Height;
        else if (style.Height is MathLength ml)
        {
            float refHeight = containingBlock?.ContentBox.Height ?? parentHeight;
            result = ml.ToPixels(fontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        }
        else if (containingBlock != null && !(style.Top is AutoLength) && !(style.Bottom is AutoLength))
        {
            float top = Length.ToPixelsOrDefault(style.Top, fontSize, _viewportWidth, _viewportHeight);
            float bottom = Length.ToPixelsOrDefault(style.Bottom, fontSize, _viewportWidth, _viewportHeight);
            result = containingBlock.ContentBox.Height - top - bottom;
        }

        if (!float.IsNaN(result))
        {
            if (style.MinHeight is PixelLength minH) result = Math.Max(result, minH.Value);
            if (style.MaxHeight is PixelLength maxH) result = Math.Min(result, maxH.Value);
        }
        return result;
    }

    private LayoutBox? CreateLayoutBox(Element element, float x, float y, float availableWidth, LayoutBox? parentBox)
    {
        var style = element.ComputedStyle;
        if (style == null) return null;
        if (style.Display == DisplayType.None) return null;

        var box = new LayoutBox();
        box.Dimensions = BoxDimensions.FromStyle(style);

        float width = CalculateWidth(element, availableWidth);
        float parentHeight = parentBox?.ContentBox.Height ?? _viewportHeight;
        var parentElement = element.ParentElement;
        bool parentHeightExplicit = parentElement?.ComputedStyle != null && HasExplicitHeight(parentElement.ComputedStyle);
        float elementHeight = CalculateHeight(element, availableWidth, parentHeight, style, parentHeightExplicit);

        bool isAutoMarginLeft = style.MarginLeft is AutoLength;
        bool isAutoMarginRight = style.MarginRight is AutoLength;
        float marginLeft = isAutoMarginLeft ? 0 : style.MarginLeft.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float marginRight = isAutoMarginRight ? 0 : style.MarginRight.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float marginTop = style.MarginTop.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float marginBottom = style.MarginBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);

        float borderLeft = style.BorderLeftWidth;
        float borderRight = style.BorderRightWidth;
        float borderTop = style.BorderTopWidth;
        float borderBottom = style.BorderBottomWidth;

        float paddingLeft = style.PaddingLeft.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float paddingRight = style.PaddingRight.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float paddingTop = style.PaddingTop.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float paddingBottom = style.PaddingBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);

        float contentWidth = width;
        float contentHeight = float.IsNaN(elementHeight) ? 0 : elementHeight;

        if (style.AspectRatio > 0)
        {
            if (style.Width is not AutoLength && float.IsNaN(elementHeight))
                contentHeight = contentWidth / style.AspectRatio;
            else if (style.Width is AutoLength && !float.IsNaN(elementHeight) && elementHeight > 0)
                contentWidth = elementHeight * style.AspectRatio;
        }

        if (style.BoxSizing == BoxSizingType.BorderBox)
        {
            if (!float.IsNaN(width))
                contentWidth = Math.Max(0, width - borderLeft - borderRight - paddingLeft - paddingRight);
            if (!float.IsNaN(elementHeight) && elementHeight > 0)
                contentHeight = Math.Max(0, elementHeight - borderTop - borderBottom - paddingTop - paddingBottom);
        }

        // Apply min/max constraints
        if (style.MinWidth is PixelLength minW) contentWidth = Math.Max(contentWidth, minW.Value);
        if (style.MaxWidth is PixelLength maxW) contentWidth = Math.Min(contentWidth, maxW.Value);
        if (style.MinHeight is PixelLength minH) contentHeight = Math.Max(contentHeight, minH.Value);
        if (style.MaxHeight is PixelLength maxH) contentHeight = Math.Min(contentHeight, maxH.Value);

        bool isBlockLevel = style.Display != DisplayType.Inline && style.Display != DisplayType.InlineFlex &&
                            style.Display != DisplayType.InlineBlock && style.Position != PositionType.Absolute;
        if (style.Width is AutoLength && isBlockLevel)
        {
            float availableForContent = availableWidth - marginLeft - marginRight - borderLeft - borderRight - paddingLeft - paddingRight;
            if (contentWidth > availableForContent)
                contentWidth = Math.Max(0, availableForContent);
        }

        float totalHorizontal = marginLeft + borderLeft + paddingLeft + paddingRight + borderRight + marginRight;
        if (parentBox == null && contentWidth + totalHorizontal > availableWidth)
            contentWidth = Math.Max(0, availableWidth - totalHorizontal);

        if (isBlockLevel && (isAutoMarginLeft || isAutoMarginRight))
        {
            float fixedHorizontal = borderLeft + paddingLeft + contentWidth + paddingRight + borderRight;
            float usedHorizontal = fixedHorizontal;
            if (!isAutoMarginLeft) usedHorizontal += marginLeft;
            if (!isAutoMarginRight) usedHorizontal += marginRight;
            float remaining = availableWidth - usedHorizontal;
            if (remaining > 0)
            {
                if (isAutoMarginLeft && isAutoMarginRight)
                {
                    marginLeft = remaining / 2;
                    marginRight = remaining / 2;
                }
                else if (isAutoMarginLeft)
                    marginLeft = remaining;
                else
                    marginRight = remaining;
            }
            else
            {
                if (isAutoMarginLeft) marginLeft = 0;
                if (isAutoMarginRight) marginRight = 0;
            }
        }

        float totalWidth = marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight + marginRight;
        float totalHeight = marginTop + borderTop + paddingTop + contentHeight + paddingBottom + borderBottom + marginBottom;

        box.MarginBox = new SKRect(x, y, x + totalWidth, y + totalHeight);
        box.BorderBox = new SKRect(x + marginLeft + borderLeft, y + marginTop + borderTop,
                                   x + marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight,
                                   y + marginTop + borderTop + contentHeight + paddingBottom + borderBottom);
        box.PaddingBox = new SKRect(x + marginLeft + borderLeft + paddingLeft, y + marginTop + borderTop + paddingTop,
                                    x + marginLeft + borderLeft + paddingLeft + contentWidth,
                                    y + marginTop + borderTop + paddingTop + contentHeight);
        box.ContentBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top,
                                    box.PaddingBox.Left + contentWidth,
                                    box.PaddingBox.Top + contentHeight);

        box.MarginBox = LayoutMath.RoundRect(box.MarginBox, _dpiScale);
        box.BorderBox = LayoutMath.RoundRect(box.BorderBox, _dpiScale);
        box.PaddingBox = LayoutMath.RoundRect(box.PaddingBox, _dpiScale);
        box.ContentBox = LayoutMath.RoundRect(box.ContentBox, _dpiScale);

        box.LineHeight = style.LineHeight * style.FontSize;

        // Generate ::before and ::after pseudo-element content
        GeneratePseudoElementContent(element, box, style);

        element.LayoutBox = box;
        box.ZIndex = style.ZIndex;

        float childX = box.ContentBox.Left;
        float childY = box.ContentBox.Top;
        float childPaddingLeft = style.PaddingLeft.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float childPaddingRight = style.PaddingRight.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float childAvailableWidth = Math.Max(0, box.ContentBox.Width - childPaddingLeft - childPaddingRight);

        // 绝对定位处理
        if (style.Position == PositionType.Absolute)
        {
            LayoutBox? containingBlock = null;
            var ancestor = element.ParentElement;
            while (ancestor != null)
            {
                if (ancestor.LayoutBox != null && ancestor.ComputedStyle != null && ancestor.ComputedStyle.Position != PositionType.Static)
                {
                    containingBlock = ancestor.LayoutBox;
                    break;
                }
                ancestor = ancestor.ParentElement;
            }
            if (containingBlock == null)
                containingBlock = parentBox ?? new LayoutBox { ContentBox = new SKRect(0, 0, _viewportWidth, _viewportHeight) };

            float absWidth = GetAbsoluteWidth(style, containingBlock, style.FontSize);
            float absHeight = GetAbsoluteHeight(style, containingBlock, style.FontSize, containingBlock.ContentBox.Height);

            float tmpWidth = float.IsNaN(absWidth) ? _viewportWidth : Math.Max(absWidth, 0);
            float tmpHeight = float.IsNaN(absHeight) ? _viewportHeight : Math.Max(absHeight, 0);
            var tmpBox = new LayoutBox
            {
                ContentBox = new SKRect(0, 0, tmpWidth, tmpHeight),
                PaddingBox = new SKRect(0, 0, tmpWidth, tmpHeight),
                BorderBox = new SKRect(0, 0, tmpWidth, tmpHeight),
                MarginBox = new SKRect(0, 0, tmpWidth, tmpHeight)
            };

            float childStartY = 0;
            LayoutBlockChildren(element, tmpBox, 0, ref childStartY, tmpWidth);
            if (style.Display == DisplayType.Inline || style.Display == DisplayType.InlineBlock)
                LayoutInlineChildren(element, tmpBox, 0, 0, tmpWidth);

            AdjustBoxHeightFromContent(tmpBox);

            float maxChildRight = 0;
            foreach (var childBox in tmpBox.Children)
                if (childBox.MarginBox.Right > maxChildRight)
                    maxChildRight = childBox.MarginBox.Right;
            float maxChildBottom = 0;
            foreach (var childBox in tmpBox.Children)
                if (childBox.MarginBox.Bottom > maxChildBottom)
                    maxChildBottom = childBox.MarginBox.Bottom;
            if (tmpBox.LineRuns != null && tmpBox.LineRuns.Count > 0)
            {
                float runsWidth = 0;
                foreach (var run in tmpBox.LineRuns)
                    runsWidth += run.Width;
                maxChildRight = Math.Max(maxChildRight, runsWidth);
                float runsHeight = 0;
                foreach (var run in tmpBox.LineRuns)
                    runsHeight = Math.Max(runsHeight, run.Height);
                maxChildBottom = Math.Max(maxChildBottom, runsHeight);
            }
            if (tmpBox.Lines != null && tmpBox.Lines.Count > 0)
            {
                float linesWidth = 0;
                foreach (var line in tmpBox.Lines)
                {
                    float lineRunWidth = 0;
                    if (line.Runs != null)
                        foreach (var run in line.Runs)
                            lineRunWidth += run.Width;
                    linesWidth = Math.Max(linesWidth, lineRunWidth);
                }
                maxChildRight = Math.Max(maxChildRight, linesWidth);
                maxChildBottom = Math.Max(maxChildBottom, tmpBox.Lines.Last().Y + tmpBox.Lines.Last().Height);
            }

            float finalWidth = float.IsNaN(absWidth) ? maxChildRight : absWidth;
            float finalHeight = float.IsNaN(absHeight) ? maxChildBottom : absHeight;

            var finalBox = LayoutAbsolute(element, finalHeight, finalWidth, containingBlock, parentBox);
            element.LayoutBox = finalBox;

            finalBox.Children.Clear();
            foreach (var childBox in tmpBox.Children)
            {
                childBox.MarginBox = new SKRect(
                    childBox.MarginBox.Left + finalBox.ContentBox.Left,
                    childBox.MarginBox.Top + finalBox.ContentBox.Top,
                    childBox.MarginBox.Right + finalBox.ContentBox.Left,
                    childBox.MarginBox.Bottom + finalBox.ContentBox.Top);
                childBox.BorderBox = new SKRect(
                    childBox.BorderBox.Left + finalBox.ContentBox.Left,
                    childBox.BorderBox.Top + finalBox.ContentBox.Top,
                    childBox.BorderBox.Right + finalBox.ContentBox.Left,
                    childBox.BorderBox.Bottom + finalBox.ContentBox.Top);
                childBox.PaddingBox = new SKRect(
                    childBox.PaddingBox.Left + finalBox.ContentBox.Left,
                    childBox.PaddingBox.Top + finalBox.ContentBox.Top,
                    childBox.PaddingBox.Right + finalBox.ContentBox.Left,
                    childBox.PaddingBox.Bottom + finalBox.ContentBox.Top);
                childBox.ContentBox = new SKRect(
                    childBox.ContentBox.Left + finalBox.ContentBox.Left,
                    childBox.ContentBox.Top + finalBox.ContentBox.Top,
                    childBox.ContentBox.Right + finalBox.ContentBox.Left,
                    childBox.ContentBox.Bottom + finalBox.ContentBox.Top);
                finalBox.Children.Add(childBox);
            }
            if (tmpBox.LineRuns != null && tmpBox.LineRuns.Count > 0)
            {
                finalBox.LineRuns = new List<InlineRun>();
                foreach (var run in tmpBox.LineRuns)
                {
                    finalBox.LineRuns.Add(new InlineRun
                    {
                        Text = run.Text,
                        X = run.X + finalBox.ContentBox.Left,
                        Width = run.Width,
                        Height = run.Height,
                        Baseline = run.Baseline + finalBox.ContentBox.Top,
                        Node = run.Node,
                        IsText = run.IsText,
                        Color = run.Color,
                        FontSize = run.FontSize,
                        FontFamily = run.FontFamily
                    });
                }
            }
            if (tmpBox.Lines != null && tmpBox.Lines.Count > 0)
            {
                finalBox.Lines = new List<LineBox>();
                foreach (var line in tmpBox.Lines)
                {
                    var newLine = new LineBox
                    {
                        X = line.X + finalBox.ContentBox.Left,
                        Y = line.Y + finalBox.ContentBox.Top,
                        Width = line.Width,
                        Height = line.Height,
                        Baseline = line.Baseline + finalBox.ContentBox.Top,
                        TextAlignOffsetX = line.TextAlignOffsetX
                    };
                    foreach (var run in line.Runs)
                    {
                        newLine.Runs.Add(new InlineRun
                        {
                            Text = run.Text,
                            X = run.X + finalBox.ContentBox.Left,
                            Width = run.Width,
                            Height = run.Height,
                            Baseline = run.Baseline + finalBox.ContentBox.Top,
                            Node = run.Node,
                            IsText = run.IsText,
                            Color = run.Color,
                            FontSize = run.FontSize,
                            FontFamily = run.FontFamily
                        });
                    }
                    finalBox.Lines.Add(newLine);
                }
            }
            return finalBox;
        }

        // Multi-column: compute column width before laying out children
        if (style.ColumnCount > 0 || (style.ColumnWidth != null && style.ColumnWidth is not AutoLength))
        {
            var (colCount, colWidth, gapSize) = ComputeMultiColumn(style, box.ContentBox.Width);
            if (colCount > 1)
            {
                box.IsMultiColumn = true;
                box.ColumnCount = colCount;
                box.ColumnWidth = colWidth;
                box.ColumnGapSize = gapSize;
                childAvailableWidth = Math.Max(0, colWidth);
            }
        }

        switch (style.Display)
        {
            case DisplayType.Flex:
            case DisplayType.InlineFlex:
                LayoutFlexChildren(element, box, childX, childY, childAvailableWidth);
                break;
            case DisplayType.Inline:
            case DisplayType.InlineBlock:
                LayoutInlineChildren(element, box, childX, childY, childAvailableWidth);
                break;
            case DisplayType.Table:
                TableLayoutAlgorithm.LayoutTable(element, box, childAvailableWidth);
                break;
            case DisplayType.ListItem:
                LayoutBlockChildren(element, box, childX, ref childY, childAvailableWidth);
                break;
            default:
                LayoutBlockChildren(element, box, childX, ref childY, childAvailableWidth);
                break;
        }

        AdjustBoxHeightFromContent(box);

        // Detect scroll containers
        if (style.Overflow == OverflowType.Scroll || style.Overflow == OverflowType.Auto ||
            style.OverflowX == OverflowType.Scroll || style.OverflowX == OverflowType.Auto ||
            style.OverflowY == OverflowType.Scroll || style.OverflowY == OverflowType.Auto)
        {
            box.IsScrollContainer = true;
            box.ScrollContentWidth = box.ContentBox.Width;
            box.ScrollContentHeight = box.ContentBox.Height;
            foreach (var child in box.Children)
            {
                if (child.MarginBox.Right > box.ContentBox.Left + box.ScrollContentWidth)
                    box.ScrollContentWidth = child.MarginBox.Right - box.ContentBox.Left;
                if (child.MarginBox.Bottom > box.ContentBox.Top + box.ScrollContentHeight)
                    box.ScrollContentHeight = child.MarginBox.Bottom - box.ContentBox.Top;
            }
            if (box.Lines != null && box.Lines.Count > 0)
            {
                float maxLineWidth = 0;
                foreach (var line in box.Lines)
                {
                    float lineWidth = 0;
                    foreach (var run in line.Runs) lineWidth += run.Width;
                    if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
                }
                if (maxLineWidth > box.ScrollContentWidth)
                    box.ScrollContentWidth = maxLineWidth;
                float linesHeight = box.Lines.Last().Y + box.Lines.Last().Height - box.ContentBox.Top;
                if (linesHeight > box.ScrollContentHeight)
                    box.ScrollContentHeight = linesHeight;
            }
        }

        // Detect sticky
        if (style.Position == PositionType.Sticky)
        {
            box.IsSticky = true;
            box.StickyTop = style.Top is PixelLength st ? st.Value : 0;
            box.StickyLeft = style.Left is PixelLength sl ? sl.Value : 0;
        }

        // Apply text-overflow: ellipsis
        if (style.TextOverflow == TextOverflowType.Ellipsis &&
            (style.Overflow == OverflowType.Hidden || style.OverflowX == OverflowType.Hidden) &&
            box.Lines != null && box.Lines.Count > 0)
        {
            ApplyTextEllipsis(box, style);
        }

        // Multi-column layout: reposition children into columns
        if (box.IsMultiColumn)
        {
            ApplyMultiColumn(element, box, style);
        }

        bool isAutoWidth = style.Width is AutoLength || style.Width is null;
        if (isAutoWidth && (style.Display == DisplayType.Inline || style.Display == DisplayType.InlineBlock))
        {
            float contentWidthFromContent = 0;
            if (box.Lines != null && box.Lines.Count > 0)
            {
                foreach (var line in box.Lines)
                {
                    float lineWidth = 0;
                    if (line.Runs != null)
                        foreach (var r in line.Runs) lineWidth += r.Width;
                    if (lineWidth > contentWidthFromContent)
                        contentWidthFromContent = lineWidth;
                }
            }
            if (box.Children.Count > 0)
            {
                float childrenMaxRight = box.ContentBox.Left;
                foreach (var child in box.Children)
                    if (child.MarginBox.Right > childrenMaxRight)
                        childrenMaxRight = child.MarginBox.Right;
                float childrenWidth = childrenMaxRight - box.ContentBox.Left;
                if (childrenWidth > contentWidthFromContent)
                    contentWidthFromContent = childrenWidth;
            }
            if (box.LineRuns != null && box.LineRuns.Count > 0)
            {
                float runsWidth = 0;
                foreach (var r in box.LineRuns) runsWidth += r.Width;
                if (runsWidth > contentWidthFromContent)
                    contentWidthFromContent = runsWidth;
            }
            if (contentWidthFromContent > 0)
            {
                // ContentBox holds only the content (text/children) — no padding.
                // BorderBox includes padding + border around content.
                // MarginBox = BorderBox + margin.
                float newContentWidth = contentWidthFromContent;

                // ContentBox.Right = ContentBox.Left + content width (text only)
                float newContentRight = box.ContentBox.Left + newContentWidth;

                // BorderBox.Right = BorderBox.Left + content + padding + border on both sides
                float contentPlusPadding = newContentWidth + paddingLeft + paddingRight;
                float borderTotal = contentPlusPadding + borderLeft + borderRight;
                float newBorderRight = box.BorderBox.Left + borderTotal;

                // MarginBox.Right = MarginBox.Left + borderTotal + margin on both sides
                float marginTotal = marginLeft + marginRight;
                float newMarginRight = box.MarginBox.Left + borderTotal + marginTotal;

                box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, newContentRight, box.ContentBox.Bottom);
                box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, newContentRight, box.PaddingBox.Bottom);
                box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, newBorderRight, box.BorderBox.Bottom);
                box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, newMarginRight, box.MarginBox.Bottom);
            }
        }

        return box;
    }

    private float CalculateWidth(Element element, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return availableWidth;
        if (style.Width is PixelLength w) return w.Value;
        if (style.Width is PercentLength wp) return wp.Value * availableWidth;
        if (style.Width is EmLength em) return em.Value * style.FontSize;
        if (style.Width is RemLength rem) return rem.Value * _rootFontSize;
        return availableWidth;
    }

    private float CalculateHeight(Element element, float availableWidth, float parentHeight, ComputedStyle style, bool parentHeightExplicit = true)
    {
        if (style.Height is PixelLength h) return h.Value;
        if (style.Height is PercentLength hp)
        {
            if (parentHeightExplicit && parentHeight > 0)
                return hp.Value * parentHeight;
            return float.NaN;
        }
        if (style.Height is EmLength em) return em.Value * style.FontSize;
        if (style.Height is RemLength rem) return rem.Value * _rootFontSize;
        return float.NaN;
    }

    private bool HasExplicitHeight(ComputedStyle style)
    {
        return style.Height is PixelLength || style.Height is EmLength || style.Height is RemLength || style.Height is VhLength || style.Height is VminLength || style.Height is VmaxLength;
    }

    private void LayoutBlockChildren(Element element, LayoutBox box, float x, ref float y, float availableWidth)
    {
        float currentY = y;
        var style = element.ComputedStyle;
        float fontSize = style?.FontSize ?? 16f;
        float lineHeight = (style?.LineHeight ?? 1.5f) * fontSize;

        var floatLeftElements = new List<(Element elem, float marginTop)>();
        var floatRightElements = new List<(Element elem, float marginTop)>();
        var normalFlowElements = new List<Node>();

        // Check if details element without open attribute → hide all non-summary children
        bool isClosedDetails = element.TagName == "DETAILS" && !element.HasAttribute("open");
        bool detailsSummaryFound = false;

        foreach (var child in element.Children)
        {
            // For closed <details>, only include the first <summary> child
            if (isClosedDetails)
            {
                if (child is Element ce && ce.TagName == "SUMMARY" && !detailsSummaryFound)
                    detailsSummaryFound = true;
                else
                    continue;
            }

            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null) continue;
                if (childStyle.Display == DisplayType.None) continue;
                if (childStyle.Position == PositionType.Absolute)
                {
                    var childBox = CreateLayoutBox(childElement, 0, 0, availableWidth, box);
                    if (childBox != null) box.Children.Add(childBox);
                    continue;
                }
                if (childStyle.Float == FloatType.Left)
                    floatLeftElements.Add((childElement, childStyle.MarginTop.ToPixels(childStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight)));
                else if (childStyle.Float == FloatType.Right)
                    floatRightElements.Add((childElement, childStyle.MarginTop.ToPixels(childStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight)));
                else
                    normalFlowElements.Add(childElement);
            }
            else if (child is TextNode textNode)
                normalFlowElements.Add(textNode);
        }

        float collapsedMarginBottom = 0;
        LineBox? inlineCurrentLine = null;
        float inlineCurrentX = 0;
        float inlineMaxHeightInLine = 0;

        var (preserveSpaces, preserveNewlines, allowWrapping) = GetWhiteSpaceBehavior(style?.WhiteSpace ?? WhiteSpaceMode.Normal);

        int floatLeftIndex = 0, floatRightIndex = 0;
        detailsSummaryFound = false;

        // Merge floats and normal flow in source order
        var mergedChildren = new List<(Node node, bool isFloatLeft, int floatIdx)>();
        foreach (var child in element.Children)
        {
            // For closed <details>, only include the first <summary> child
            if (isClosedDetails)
            {
                if (child is Element ce && ce.TagName == "SUMMARY" && !detailsSummaryFound)
                {
                    detailsSummaryFound = true;
                    // Include the summary
                }
                else
                    continue;
            }

            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None)
                    continue;
                if (childStyle.Position == PositionType.Absolute) continue;
                if (childStyle.Float == FloatType.Left)
                    mergedChildren.Add((childElement, true, floatLeftIndex++));
                else if (childStyle.Float == FloatType.Right)
                    mergedChildren.Add((childElement, false, floatRightIndex++));
                else
                    mergedChildren.Add((childElement, false, -1));
            }
            else if (child is TextNode textNode)
                mergedChildren.Add((textNode, false, -1));
        }

        floatLeftIndex = 0;
        floatRightIndex = 0;

        foreach (var (item, isFloat, floatIdx) in mergedChildren)
        {
            if (isFloat && item is Element fElem)
            {
                // Place floats interleaved with normal flow
                if (floatIdx >= 0)
                {
                    var fStyle = fElem.ComputedStyle;
                    if (fStyle == null) continue;
                    float marginTop = fStyle.MarginTop.ToPixels(fStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                    float marginLeft = fStyle.MarginLeft.ToPixels(fStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                    float marginRight = fStyle.MarginRight.ToPixels(fStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);

                    // Flush pending inline content before float
                    if (inlineCurrentLine != null && inlineCurrentLine.Runs.Count > 0)
                    {
                        inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : (style?.LineHeight ?? 1.5f) * (style?.FontSize ?? 16);
                        currentY = inlineCurrentLine.Y + inlineCurrentLine.Height;
                        inlineCurrentLine = null;
                        inlineCurrentX = 0;
                        inlineMaxHeightInLine = 0;
                    }

                    currentY += marginTop;
                    float elemWidth = 0;
                    if (fStyle.Width is PixelLength w) elemWidth = w.Value;
                    else if (fStyle.Width is PercentLength wp) elemWidth = wp.Value * availableWidth;
                    else elemWidth = 100;

                    float elemX = (floatIdx >= 0 && item == floatLeftElements[floatLeftIndex].elem) ? x + marginLeft : x + availableWidth - elemWidth - marginRight;
                    // Actually determine position by type
                    // Let's use the FloatType from style
                    bool isLeft = fStyle.Float == FloatType.Left;
                    float fx = isLeft ? x + marginLeft : x + availableWidth - elemWidth - marginRight;

                    var childBox = CreateLayoutBox(fElem, fx, currentY, elemWidth, box);
                    if (childBox != null)
                    {
                        box.Children.Add(childBox);
                        childBox.Parent = box;
                        // move past the float
                        currentY = Math.Max(currentY, childBox.MarginBox.Bottom);
                    }
                    if (isLeft) floatLeftIndex++;
                    else floatRightIndex++;
                }
                continue;
            }

            if (item is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null) continue;
                float marginTop = childStyle.MarginTop.ToPixels(childStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                float marginBottom = childStyle.MarginBottom.ToPixels(childStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                float actualMarginTop = Math.Max(marginTop, collapsedMarginBottom);
                currentY += actualMarginTop;
                collapsedMarginBottom = 0;

                if (childStyle.Display == DisplayType.Inline)
                {
                    var textSb = new StringBuilder();
                    TextNode? firstTextNode = null;
                    foreach (var c in childElement.Children)
                    {
                        if (c is TextNode tn)
                        {
                            textSb.Append(tn.TextContent);
                            firstTextNode ??= tn;
                        }
                    }
                    string text = textSb.ToString();
                    if (!preserveSpaces) text = text.Trim();
                    if (!string.IsNullOrEmpty(text) && firstTextNode != null)
                    {
                        float inlineFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : fontSize;
                        float inlineLineHeight = (childStyle.LineHeight > 0 ? childStyle.LineHeight : 1.5f) * inlineFontSize;

                        float inlineStartX = inlineCurrentX;
                        float inlineStartY = inlineCurrentLine?.Y ?? currentY;

                        if (inlineCurrentLine == null)
                        {
                            inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + inlineLineHeight * 0.85f, Height = inlineLineHeight };
                            box.Lines ??= new List<LineBox>();
                            box.Lines.Add(inlineCurrentLine);
                            inlineCurrentX = x;
                            inlineMaxHeightInLine = 0;
                        }

                        if (preserveSpaces)
                        {
                            // pre/pre-wrap: preserve original spacing, don't split by spaces
                            float textWidth = MeasureTextWidth(text, inlineFontSize, childStyle.FontFamily ?? style?.FontFamily);
                            if (allowWrapping && inlineCurrentX + textWidth > x + availableWidth - 0.01f && inlineCurrentX > x)
                            {
                                inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : inlineLineHeight;
                                currentY += inlineCurrentLine.Height;
                                inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + inlineLineHeight * 0.85f, Height = inlineLineHeight };
                                box.Lines.Add(inlineCurrentLine);
                                inlineCurrentX = x;
                                inlineMaxHeightInLine = 0;
                            }
                            inlineCurrentLine.Runs.Add(new InlineRun
                            {
                                Text = text,
                                Width = textWidth,
                                Height = inlineLineHeight,
                                IsText = true,
                                Node = firstTextNode,
                                Color = childStyle.Color.Alpha > 0 ? childStyle.Color : style?.Color ?? SKColors.Black,
                                FontSize = inlineFontSize,
                                FontFamily = childStyle.FontFamily ?? style?.FontFamily
                            });
                            inlineCurrentX += textWidth;
                            if (inlineLineHeight > inlineMaxHeightInLine) inlineMaxHeightInLine = inlineLineHeight;
                        }
                        else
                        {
                            bool hasSpacesInline = text.Contains(' ');
                            if (hasSpacesInline)
                            {
                                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                for (int wi = 0; wi < words.Length; wi++)
                                {
                                    bool isLastWord = (wi == words.Length - 1);
                                    string token = isLastWord ? words[wi] : words[wi] + " ";
                                    float tokenWidth = MeasureTextWidth(token, inlineFontSize, childStyle.FontFamily ?? style?.FontFamily);
                                    if (allowWrapping && inlineCurrentX + tokenWidth > x + availableWidth - 0.01f && inlineCurrentX > x)
                                    {
                                        inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : inlineLineHeight;
                                        currentY += inlineCurrentLine.Height;
                                        inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + inlineLineHeight * 0.85f, Height = inlineLineHeight };
                                        box.Lines.Add(inlineCurrentLine);
                                        inlineCurrentX = x;
                                        inlineMaxHeightInLine = 0;
                                    }
                                    inlineCurrentLine.Runs.Add(new InlineRun
                                    {
                                        Text = token,
                                        Width = tokenWidth,
                                        Height = inlineLineHeight,
                                        IsText = true,
                                        Node = firstTextNode,
                                        Color = childStyle.Color.Alpha > 0 ? childStyle.Color : style?.Color ?? SKColors.Black,
                                        FontSize = inlineFontSize,
                                        FontFamily = childStyle.FontFamily ?? style?.FontFamily
                                    });
                                    inlineCurrentX += tokenWidth;
                                    if (inlineLineHeight > inlineMaxHeightInLine) inlineMaxHeightInLine = inlineLineHeight;
                                }
                            }
                            else
                            {
                                // CJK character-by-character wrapping
                                foreach (char c in text)
                                {
                                    string chStr = c.ToString();
                                    float charWidth = MeasureTextWidth(chStr, inlineFontSize, childStyle.FontFamily ?? style?.FontFamily);
                                    if (allowWrapping && inlineCurrentLine.Runs.Count > 0 && inlineCurrentX + charWidth > x + availableWidth - 0.01f)
                                    {
                                        inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : inlineLineHeight;
                                        currentY += inlineCurrentLine.Height;
                                        inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + inlineLineHeight * 0.85f, Height = inlineLineHeight };
                                        box.Lines.Add(inlineCurrentLine);
                                        inlineCurrentX = x;
                                        inlineMaxHeightInLine = 0;
                                    }
                                    inlineCurrentLine.Runs.Add(new InlineRun
                                    {
                                        Text = chStr,
                                        Width = charWidth,
                                        Height = inlineLineHeight,
                                        IsText = true,
                                        Node = firstTextNode,
                                        Color = childStyle.Color.Alpha > 0 ? childStyle.Color : style?.Color ?? SKColors.Black,
                                        FontSize = inlineFontSize,
                                        FontFamily = childStyle.FontFamily ?? style?.FontFamily
                                    });
                                    inlineCurrentX += charWidth;
                                    if (inlineLineHeight > inlineMaxHeightInLine) inlineMaxHeightInLine = inlineLineHeight;
                                }
                            }
                        }

                        // Create LayoutBox for this inline element so HitTest can find it
                        if (inlineCurrentLine != null)
                        {
                            float topY = inlineStartY;
                            float bottomY = inlineCurrentLine.Y + inlineCurrentLine.Height;
                            float leftX = Math.Min(inlineStartX, inlineCurrentX);
                            float rightX = Math.Max(inlineStartX, inlineCurrentX);
                            var inlineBox = new LayoutBox
                            {
                                MarginBox = new SKRect(leftX, topY, rightX, bottomY),
                                ContentBox = new SKRect(leftX, topY, rightX, bottomY),
                                PaddingBox = new SKRect(leftX, topY, rightX, bottomY),
                                BorderBox = new SKRect(leftX, topY, rightX, bottomY)
                            };
                            childElement.LayoutBox = inlineBox;
                            box.Children.Add(inlineBox);
                            inlineBox.Parent = box;
                        }
                    }
                    continue;
                }

                if (childStyle.Clear != ClearType.None)
                {
                    float clearY = currentY;
                    if (childStyle.Clear == ClearType.Left || childStyle.Clear == ClearType.Both)
                    {
                        foreach (var (floatElem, _) in floatLeftElements)
                            if (floatElem.LayoutBox != null && floatElem.LayoutBox.MarginBox.Bottom > clearY)
                                clearY = floatElem.LayoutBox.MarginBox.Bottom;
                    }
                    if (childStyle.Clear == ClearType.Right || childStyle.Clear == ClearType.Both)
                    {
                        foreach (var (floatElem, _) in floatRightElements)
                            if (floatElem.LayoutBox != null && floatElem.LayoutBox.MarginBox.Bottom > clearY)
                                clearY = floatElem.LayoutBox.MarginBox.Bottom;
                    }
                    currentY = clearY;
                }

                if (inlineCurrentLine != null && inlineCurrentLine.Runs.Count > 0)
                {
                    inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeight;
                    currentY = inlineCurrentLine.Y + inlineCurrentLine.Height;
                    inlineCurrentLine = null;
                    inlineCurrentX = 0;
                    inlineMaxHeightInLine = 0;
                }

                var childBox = CreateLayoutBox(childElement, x, currentY, availableWidth, box);
                if (childBox != null)
                {
                    box.Children.Add(childBox);
                    childBox.Parent = box;
                    currentY = childBox.BorderBox.Bottom;
                    collapsedMarginBottom = marginBottom;
                }
            }
            else if (item is TextNode textNode)
            {
                if (style == null) continue;

                if (textNode.IsWhitespaceOnly)
                {
                    // Whitespace-only text node between inline elements → add a space to preserve inter-element spacing
                    if (inlineCurrentLine != null && inlineCurrentLine.Runs.Count > 0)
                    {
                        float spaceWidth = MeasureTextWidth(" ", fontSize, style.FontFamily);
                        if (allowWrapping && inlineCurrentX + spaceWidth > x + availableWidth - 0.01f && inlineCurrentX > x)
                        {
                            inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeight;
                            currentY += inlineCurrentLine.Height;
                            float lineHeightPx = style.LineHeight * fontSize;
                            inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + lineHeightPx * 0.85f, Height = lineHeightPx };
                            box.Lines.Add(inlineCurrentLine);
                            inlineCurrentX = x;
                            inlineMaxHeightInLine = 0;
                        }
                        inlineCurrentLine.Runs.Add(new InlineRun
                        {
                            Text = " ",
                            Width = spaceWidth,
                            Height = style.LineHeight * fontSize,
                            IsText = true,
                            Node = textNode,
                            Color = style.Color,
                            FontSize = fontSize,
                            FontFamily = style.FontFamily
                        });
                        inlineCurrentX += spaceWidth;
                    }
                    continue;
                }

                var text = textNode.TextContent ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    float lineHeightPx = style.LineHeight * fontSize;

                    if (inlineCurrentLine == null)
                    {
                        inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + lineHeightPx * 0.85f, Height = lineHeightPx };
                        box.Lines ??= new List<LineBox>();
                        box.Lines.Add(inlineCurrentLine);
                        inlineCurrentX = x;
                        inlineMaxHeightInLine = 0;
                    }

                    if (preserveSpaces)
                    {
                        // pre/pre-wrap: preserve original spacing, don't split by spaces
                        float textWidth = MeasureTextWidth(text, fontSize, style.FontFamily);
                        if (allowWrapping && inlineCurrentX + textWidth > x + availableWidth - 0.01f && inlineCurrentX > x)
                        {
                            inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeightPx;
                            currentY += inlineCurrentLine.Height;
                            inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + lineHeightPx * 0.85f, Height = lineHeightPx };
                            box.Lines.Add(inlineCurrentLine);
                            inlineCurrentX = x;
                            inlineMaxHeightInLine = 0;
                        }
                        inlineCurrentLine.Runs.Add(new InlineRun
                        {
                            Text = text,
                            Width = textWidth,
                            Height = lineHeightPx,
                            IsText = true,
                            Node = textNode,
                            Color = style.Color,
                            FontSize = fontSize,
                            FontFamily = style.FontFamily
                        });
                        inlineCurrentX += textWidth;
                        if (lineHeightPx > inlineMaxHeightInLine) inlineMaxHeightInLine = lineHeightPx;
                    }
                    else
                    {
                        // Normalize whitespace so newlines/tabs collapse to spaces (matching CSS)
                        string normalized = NormalizeWhitespace(text);
                        bool hasLeadingSpace = normalized.Length > 0 && normalized[0] == ' ';
                        bool hasTrailingSpace = normalized.Length > 0 && normalized[normalized.Length - 1] == ' ';

                        bool hasSpacesText = normalized.Contains(' ');
                        if (hasSpacesText)
                        {
                            var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            for (int wi = 0; wi < words.Length; wi++)
                            {
                                bool isFirstWord = (wi == 0);
                                bool isLastWord = (wi == words.Length - 1);
                                string token = words[wi];
                                if (isFirstWord && hasLeadingSpace)
                                    token = " " + token;
                                if (isLastWord)
                                {
                                    if (hasTrailingSpace)
                                        token = token + " ";
                                }
                                else
                                {
                                    token = token + " ";
                                }
                                float tokenWidth = MeasureTextWidth(token, fontSize, style.FontFamily);
                                if (allowWrapping && inlineCurrentX + tokenWidth > x + availableWidth - 0.01f && inlineCurrentX > x)
                                {
                                    inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeightPx;
                                    currentY += inlineCurrentLine.Height;
                                    inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + lineHeightPx * 0.85f, Height = lineHeightPx };
                                    box.Lines.Add(inlineCurrentLine);
                                    inlineCurrentX = x;
                                    inlineMaxHeightInLine = 0;
                                }
                                inlineCurrentLine.Runs.Add(new InlineRun
                                {
                                    Text = token,
                                    Width = tokenWidth,
                                    Height = lineHeightPx,
                                    IsText = true,
                                    Node = textNode,
                                    Color = style.Color,
                                    FontSize = fontSize,
                                    FontFamily = style.FontFamily
                                });
                                inlineCurrentX += tokenWidth;
                                if (lineHeightPx > inlineMaxHeightInLine) inlineMaxHeightInLine = lineHeightPx;
                            }
                        }
                        else
                        {
                            // CJK character-by-character wrapping (no spaces in text)
                            foreach (char c in normalized)
                            {
                                string chStr = c.ToString();
                                float charWidth = MeasureTextWidth(chStr, fontSize, style.FontFamily);
                                if (allowWrapping && inlineCurrentLine.Runs.Count > 0 && inlineCurrentX + charWidth > x + availableWidth - 0.01f)
                                {
                                    inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeightPx;
                                    currentY += inlineCurrentLine.Height;
                                    inlineCurrentLine = new LineBox { Y = currentY, Baseline = currentY + lineHeightPx * 0.85f, Height = lineHeightPx };
                                    box.Lines.Add(inlineCurrentLine);
                                    inlineCurrentX = x;
                                    inlineMaxHeightInLine = 0;
                                }
                                inlineCurrentLine.Runs.Add(new InlineRun
                                {
                                    Text = chStr,
                                    Width = charWidth,
                                    Height = lineHeightPx,
                                    IsText = true,
                                    Node = textNode,
                                    Color = style.Color,
                                    FontSize = fontSize,
                                    FontFamily = style.FontFamily
                                });
                                inlineCurrentX += charWidth;
                                if (lineHeightPx > inlineMaxHeightInLine) inlineMaxHeightInLine = lineHeightPx;
                            }
                        }
                    }
                }
            }
        }

        if (inlineCurrentLine != null && inlineCurrentLine.Runs.Count > 0)
        {
            inlineCurrentLine.Height = inlineMaxHeightInLine > 0 ? inlineMaxHeightInLine : lineHeight;
            currentY = inlineCurrentLine.Y + inlineCurrentLine.Height;
        }

        currentY += collapsedMarginBottom;
        y = currentY;
    }

    // 修复后的内联布局方法：支持中文等无空格字符的换行
    private void LayoutInlineChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        float lineHeight = style.LineHeight * style.FontSize;
        float baseline = y + lineHeight * 0.85f;
        float currentX = x;
        float currentY = y;
        float maxHeightInLine = lineHeight;

        var currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
        box.Lines = new List<LineBox> { currentLine };

        bool isNowrap = style.WhiteSpace == WhiteSpaceMode.Nowrap;
        var (preserveSpaces, preserveNewlines, allowWrapping) = GetWhiteSpaceBehavior(style.WhiteSpace);
        bool breakAll = style.WordBreak == WordBreakMode.BreakAll;
        bool breakWord = style.WordBreak == WordBreakMode.BreakWord || style.OverflowWrap == OverflowWrapMode.BreakWord || style.OverflowWrap == OverflowWrapMode.Anywhere;

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None) continue;

                // 处理按钮
                if (childElement.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
                {
                    string btnText = GetButtonTextFromElement(childElement);
                    if (string.IsNullOrEmpty(btnText)) btnText = "Button";

                    float btnFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : 13.3333f;
                    float btnLineHeightValue = childStyle.LineHeight > 0 ? childStyle.LineHeight : 1.5f;

                    float textWidth = MeasureTextWidth(btnText, btnFontSize, childStyle.FontFamily);
                    float padLeft = childStyle.PaddingLeft.ToPixels(btnFontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                    float padRight = childStyle.PaddingRight.ToPixels(btnFontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                    float padTop = childStyle.PaddingTop.ToPixels(btnFontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                    float padBottom = childStyle.PaddingBottom.ToPixels(btnFontSize, _rootFontSize, _viewportWidth, _viewportHeight);

                    float borderLeft = childStyle.BorderLeftWidth;
                    float borderRight = childStyle.BorderRightWidth;
                    float borderTop = childStyle.BorderTopWidth;
                    float borderBottom = childStyle.BorderBottomWidth;

                    float totalButtonWidth = textWidth + padLeft + padRight + borderLeft + borderRight;
                    float totalButtonHeight = (btnFontSize * btnLineHeightValue) + padTop + padBottom + borderTop + borderBottom;

                    if (childStyle.Width is PixelLength pw && pw.Value > 0)
                    {
                        if (childStyle.BoxSizing == BoxSizingType.BorderBox)
                            totalButtonWidth = pw.Value;
                        else
                            totalButtonWidth = pw.Value + borderLeft + borderRight;
                    }
                    if (childStyle.Height is PixelLength ph && ph.Value > 0)
                    {
                        if (childStyle.BoxSizing == BoxSizingType.BorderBox)
                            totalButtonHeight = ph.Value;
                        else
                            totalButtonHeight = ph.Value + borderTop + borderBottom;
                    }

                    totalButtonWidth = Math.Max(totalButtonWidth, 4);
                    totalButtonHeight = Math.Max(totalButtonHeight, 4);

                    // 换行判断
                    if (allowWrapping && currentX + totalButtonWidth > x + availableWidth - 0.01f && currentX > x)
                    {
                        currentLine.Height = maxHeightInLine;
                        currentY += maxHeightInLine;
                        baseline = currentY + lineHeight * 0.85f;
                        currentX = x;
                        maxHeightInLine = lineHeight;
                        currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(currentLine);
                    }

                    float btnTop = baseline - totalButtonHeight;
                    float btnBottom = baseline;

                    var buttonBox = new LayoutBox();
                    buttonBox.MarginBox = new SKRect(currentX, btnTop, currentX + totalButtonWidth, btnBottom);
                    buttonBox.BorderBox = new SKRect(currentX, btnTop, currentX + totalButtonWidth, btnBottom);
                    buttonBox.PaddingBox = new SKRect(
                        currentX + borderLeft,
                        btnTop + borderTop,
                        currentX + totalButtonWidth - borderRight,
                        btnBottom - borderBottom
                    );
                    buttonBox.ContentBox = new SKRect(
                        currentX + borderLeft + padLeft,
                        btnTop + borderTop + padTop,
                        currentX + totalButtonWidth - borderRight - padRight,
                        btnBottom - borderBottom - padBottom
                    );

                    buttonBox.LineHeight = btnLineHeightValue * btnFontSize;
                    childElement.LayoutBox = buttonBox;
                    box.Children.Add(buttonBox);
                    buttonBox.Parent = box;

                    currentX += totalButtonWidth + 5;
                    if (totalButtonHeight > maxHeightInLine)
                        maxHeightInLine = totalButtonHeight;
                    continue;
                }

                if (childStyle.Display == DisplayType.Block || childStyle.Display == DisplayType.ListItem)
                {
                    currentLine.Height = maxHeightInLine;
                    currentY += maxHeightInLine;
                    baseline = currentY + lineHeight * 0.85f;
                    currentX = x;
                    maxHeightInLine = lineHeight;
                    currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                    box.Lines.Add(currentLine);

                    var blockChildBox = CreateLayoutBox(childElement, x, currentY, availableWidth, box);
                    if (blockChildBox != null)
                    {
                        box.Children.Add(blockChildBox);
                        blockChildBox.Parent = box;
                        currentY += blockChildBox.MarginBox.Height;
                        baseline = currentY + lineHeight * 0.85f;
                        currentX = x;
                        currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(currentLine);
                    }
                    continue;
                }

                // 处理内联元素（如 span），收集其内部文本
                if (childStyle.Display == DisplayType.Inline || childStyle.Display == DisplayType.InlineBlock)
                {
                    var textSb = new StringBuilder();
                    TextNode? firstTextNode = null;
                    foreach (var c in childElement.Children)
                    {
                        if (c is TextNode tn)
                        {
                            textSb.Append(tn.TextContent);
                            firstTextNode ??= tn;
                        }
                    }
                    string text = textSb.ToString();
                    if (!preserveSpaces) text = text.Trim();
                    if (!string.IsNullOrEmpty(text) && firstTextNode != null)
                    {
                        float inlineFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : style.FontSize;
                        float textWidth = MeasureTextWidth(text, inlineFontSize, childStyle.FontFamily ?? style.FontFamily);
                        float textHeight = inlineFontSize;
                        // 换行判断
                        if (allowWrapping && currentX + textWidth > x + availableWidth - 0.01f && currentX > x)
                        {
                            currentLine.Height = maxHeightInLine;
                            currentY += maxHeightInLine;
                            baseline = currentY + lineHeight * 0.85f;
                            currentX = x;
                            maxHeightInLine = lineHeight;
                            currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                            box.Lines.Add(currentLine);
                        }

                        float oldX = currentX;
                        var run = new InlineRun
                        {
                            Text = text,
                            Width = textWidth,
                            Height = textHeight,
                            Baseline = baseline,
                            IsText = true,
                            Node = firstTextNode,
                            Color = childStyle.Color,
                            FontSize = inlineFontSize,
                            FontFamily = childStyle.FontFamily ?? style.FontFamily
                        };
                        currentLine.Runs.Add(run);
                        currentX += textWidth;

                        if (textHeight > maxHeightInLine)
                            maxHeightInLine = textHeight;

                        // 为内联元素创建 LayoutBox
                        var inlineBox = new LayoutBox
                        {
                            MarginBox = new SKRect(oldX, baseline - textHeight, oldX + textWidth, baseline),
                            ContentBox = new SKRect(oldX, baseline - textHeight, oldX + textWidth, baseline),
                            BorderBox = new SKRect(oldX, baseline - textHeight, oldX + textWidth, baseline),
                            PaddingBox = new SKRect(oldX, baseline - textHeight, oldX + textWidth, baseline)
                        };
                        childElement.LayoutBox = inlineBox;
                        box.Children.Add(inlineBox);
                        inlineBox.Parent = box;
                    }
                    else
                    {
                        // 无文本的内联元素，给出占位大小
                        float childWidth = CalculateInlineElementWidth(childElement, style.FontSize);
                        float childHeight = childStyle.FontSize > 0 ? childStyle.FontSize * 1.2f : lineHeight;
                        if (allowWrapping && currentX + childWidth > x + availableWidth - 0.01f && currentX > x)
                        {
                            currentLine.Height = maxHeightInLine;
                            currentY += maxHeightInLine;
                            baseline = currentY + lineHeight * 0.85f;
                            currentX = x;
                            maxHeightInLine = lineHeight;
                            currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                            box.Lines.Add(currentLine);
                        }
                        var inlineBox = new LayoutBox();
                        inlineBox.MarginBox = new SKRect(currentX, baseline - childHeight, currentX + childWidth, baseline);
                        inlineBox.ContentBox = inlineBox.MarginBox;
                        inlineBox.BorderBox = inlineBox.MarginBox;
                        inlineBox.PaddingBox = inlineBox.MarginBox;
                        childElement.LayoutBox = inlineBox;
                        box.Children.Add(inlineBox);
                        inlineBox.Parent = box;
                        currentLine.Runs.Add(new InlineRun
                        {
                            Text = "",
                            Width = childWidth,
                            Height = childHeight,
                            IsText = false,
                            Node = childElement,
                            Color = childStyle.Color,
                            FontSize = childStyle.FontSize,
                            FontFamily = childStyle.FontFamily
                        });
                        currentX += childWidth;
                        if (childHeight > maxHeightInLine) maxHeightInLine = childHeight;
                    }
                    continue;
                }

                // 其他内联元素（br, wbr等）
                float otherWidth = CalculateInlineElementWidth(childElement, style.FontSize);
                float otherHeight = childStyle.FontSize > 0 ? childStyle.FontSize * 1.2f : lineHeight;
                if (allowWrapping && currentX + otherWidth > x + availableWidth - 0.01f && currentX > x)
                {
                    currentLine.Height = maxHeightInLine;
                    currentY += maxHeightInLine;
                    baseline = currentY + lineHeight * 0.85f;
                    currentX = x;
                    maxHeightInLine = lineHeight;
                    currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                    box.Lines.Add(currentLine);
                }
                var otherBox = new LayoutBox();
                otherBox.MarginBox = new SKRect(currentX, baseline - otherHeight, currentX + otherWidth, baseline);
                otherBox.ContentBox = otherBox.MarginBox;
                otherBox.BorderBox = otherBox.MarginBox;
                otherBox.PaddingBox = otherBox.MarginBox;
                childElement.LayoutBox = otherBox;
                box.Children.Add(otherBox);
                otherBox.Parent = box;
                currentLine.Runs.Add(new InlineRun
                {
                    Text = "",
                    Width = otherWidth,
                    Height = otherHeight,
                    IsText = false,
                    Node = childElement,
                    Color = childStyle.Color,
                    FontSize = childStyle.FontSize,
                    FontFamily = childStyle.FontFamily
                });
                currentX += otherWidth;
                if (otherHeight > maxHeightInLine) maxHeightInLine = otherHeight;
            }
            else if (child is TextNode textNode)
            {
                var text = textNode.TextContent ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                // 白空间处理：根据 white-space 模式处理文本
                List<string> textSegments;
                if (preserveNewlines)
                {
                    // pre/pre-wrap/pre-line: 保留换行符作为段落分隔
                    textSegments = new List<string>();
                    var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (preserveSpaces)
                            textSegments.Add(line);
                        else
                            textSegments.Add(NormalizeWhitespace(line));
                    }
                }
                else
                {
                    textSegments = new List<string> { NormalizeWhitespace(text) };
                }

                bool firstSegment = true;
                foreach (var segment in textSegments)
                {
                    if (string.IsNullOrEmpty(segment) && preserveNewlines && !firstSegment)
                    {
                        // pre/pre-wrap/pre-line 模式下，换行符产生新行
                        if (allowWrapping || currentX > x)
                        {
                            currentLine.Height = maxHeightInLine;
                            currentY += maxHeightInLine;
                            baseline = currentY + lineHeight * 0.85f;
                            currentX = x;
                            maxHeightInLine = lineHeight;
                            currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                            box.Lines.Add(currentLine);
                        }
                        firstSegment = false;
                        continue;
                    }
                    firstSegment = false;
                    if (string.IsNullOrEmpty(segment)) continue;

                    if (breakAll)
                    {
                        // word-break: break-all - 允许在任何字符处断行
                        LayoutTextCharacterByCharacter(segment, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                            ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, false, false, false);
                    }
                    else if (breakWord && segment.Contains(' ') && !preserveSpaces)
                    {
                        // overflow-wrap: break-word / word-break: break-word
                        // 先按空格分词，单词放不下时按字符拆分
                        bool hasLeadingSpace = segment.Length > 0 && segment[0] == ' ';
                        bool hasTrailingSpace = segment.Length > 0 && segment[segment.Length - 1] == ' ';
                        var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        for (int wi = 0; wi < words.Length; wi++)
                        {
                            bool isFirstWord = (wi == 0);
                            bool isLast = (wi == words.Length - 1);
                            string token = words[wi];
                            if (isFirstWord && hasLeadingSpace)
                                token = " " + token;
                            if (isLast)
                            {
                                if (hasTrailingSpace)
                                    token = token + " ";
                            }
                            else
                            {
                                token = token + " ";
                            }
                            float tokenWidth = MeasureTextWidth(token, style.FontSize, style.FontFamily);
                            if (currentX + tokenWidth > x + availableWidth - 0.01f && currentX > x && allowWrapping)
                            {
                                currentLine.Height = maxHeightInLine;
                                currentY += maxHeightInLine;
                                baseline = currentY + lineHeight * 0.85f;
                                currentX = x;
                                maxHeightInLine = lineHeight;
                                currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                                box.Lines.Add(currentLine);
                            }
                            float remainingWidth = x + availableWidth - currentX;
                            if (tokenWidth > remainingWidth && allowWrapping)
                            {
                                // 单词太长，按字符拆分
                                LayoutTextCharacterByCharacter(token, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                                    ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, false, false, true);
                            }
                            else
                            {
                                LayoutTextRun(token, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                                    ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, !allowWrapping);
                            }
                        }
                    }
                    else
                    {
                        // 普通模式：按空格分词 (normal / pre-wrap) 或按字符拆分 (CJK)
                        if (preserveSpaces)
                        {
                            // pre/pre-wrap: 保留原始空白，不分词
                            LayoutTextRun(segment, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                                ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, !allowWrapping);
                        }
                        else
                        {
                            bool hasSpaces = segment.Contains(' ');
                            if (hasSpaces)
                            {
                                bool hasLeadingSpace = segment.Length > 0 && segment[0] == ' ';
                                bool hasTrailingSpace = segment.Length > 0 && segment[segment.Length - 1] == ' ';
                                var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                for (int wi = 0; wi < words.Length; wi++)
                                {
                                    bool isFirstWord = (wi == 0);
                                    bool isLastWord = (wi == words.Length - 1);
                                    string token = words[wi];
                                    if (isFirstWord && hasLeadingSpace)
                                        token = " " + token;
                                    if (isLastWord)
                                    {
                                        if (hasTrailingSpace)
                                            token = token + " ";
                                    }
                                    else
                                    {
                                        token = token + " ";
                                    }
                                    LayoutTextRun(token, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                                        ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, !allowWrapping);
                                }
                            }
                            else
                            {
                                // CJK 字符逐字换行
                                LayoutTextCharacterByCharacter(segment, textNode, style, box, ref currentLine, ref currentX, ref currentY,
                                    ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, !allowWrapping, false, false);
                            }
                        }
                    }
                }
            }
        }

        currentLine.Height = maxHeightInLine;
        ApplyTextAlign(box, style.TextAlign, x, availableWidth);

        float totalHeight = 0;
        foreach (var line in box.Lines)
            totalHeight += line.Height;

        float newContentBottom = y + totalHeight;
        float pdBottom = style.PaddingBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float bdBottom = style.BorderBottomWidth;
        float mgBottom = style.MarginBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);

        box.ContentBox = new SKRect(x, y, x + availableWidth, newContentBottom);
        box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, newContentBottom + pdBottom);
        box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, newContentBottom + pdBottom + bdBottom);
        box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, newContentBottom + pdBottom + bdBottom + mgBottom);
    }

    // 修改后的 LayoutTextRun，增加 box 参数以便访问 Lines 集合
    private void LayoutTextRun(string text, TextNode textNode, ComputedStyle style, LayoutBox box,
        ref LineBox line, ref float currentX, ref float currentY, ref float baseline, ref float maxHeightInLine,
        float x, float availableWidth, float lineHeight, bool noWrap)
    {
        if (string.IsNullOrEmpty(text)) return;
        float fontSize = style.FontSize;
        float textWidth = MeasureTextWidth(text, fontSize, style.FontFamily);
        float textHeight = fontSize;

        // 换行判断：如果当前行已有内容且加上当前 run 会超出宽度，则换行
        // 如果当前行无内容但 run 本身宽度就超过可用宽度，也强制换行（允许单词内换行）
        if (!noWrap && currentX + textWidth > x + availableWidth - 0.01f)
        {
            // 完成当前行
            line.Height = maxHeightInLine;
            currentY += maxHeightInLine;
            baseline = currentY + lineHeight * 0.85f;
            currentX = x;
            maxHeightInLine = lineHeight;
            line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
            box.Lines.Add(line);
        }

        line.Runs.Add(new InlineRun
        {
            Text = text,
            Width = textWidth,
            Height = textHeight,
            IsText = true,
            Node = textNode,
            Color = style.Color,
            FontSize = fontSize,
            FontFamily = style.FontFamily
        });
        currentX += textWidth;
        if (textHeight > maxHeightInLine) maxHeightInLine = textHeight;
    }

    private string NormalizeWhitespace(string text)
    {
        // 将连续空白字符折叠为单个空格（CSS normal 模式）
        var sb = new StringBuilder();
        bool lastWasSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private void LayoutTextCharacterByCharacter(string text, TextNode textNode, ComputedStyle style, LayoutBox box,
        ref LineBox line, ref float currentX, ref float currentY, ref float baseline, ref float maxHeightInLine,
        float x, float availableWidth, float lineHeight, bool noWrap, bool forceBreakAll, bool breakLongWords)
    {
        if (string.IsNullOrEmpty(text)) return;
        var chars = text.ToCharArray();
        var currentLineChars = new List<char>();
        float currentLineWidth = 0;

        foreach (char c in chars)
        {
            string chStr = c.ToString();
            float charWidth = MeasureTextWidth(chStr, style.FontSize, style.FontFamily);

            bool shouldBreak = false;
            if (!noWrap && currentLineChars.Count > 0)
            {
                if (breakLongWords)
                {
                    // overflow-wrap: break-word 模式 - 只在单词内换行
                    shouldBreak = currentX + currentLineWidth + charWidth > x + availableWidth - 0.01f;
                }
                else
                {
                    // word-break: break-all 或 CJK 模式 - 允许在任何字符处断行
                    shouldBreak = currentX + currentLineWidth + charWidth > x + availableWidth - 0.01f;
                }
            }

            if (shouldBreak)
            {
                string runText = new string(currentLineChars.ToArray());
                LayoutTextRun(runText, textNode, style, box, ref line, ref currentX, ref currentY,
                    ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, false);
                currentLineChars.Clear();
                currentLineWidth = 0;
            }

            currentLineChars.Add(c);
            currentLineWidth += charWidth;
        }

        if (currentLineChars.Count > 0)
        {
            string runText = new string(currentLineChars.ToArray());
            LayoutTextRun(runText, textNode, style, box, ref line, ref currentX, ref currentY,
                ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, noWrap);
        }
    }

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (TextMeasurer.Instance != null)
            return TextMeasurer.Instance.MeasureText(text, fontFamily ?? "Arial", fontSize);
        // 后备估算
        float avgCharWidth = fontSize * 0.45f;
        int asciiCount = text.Count(c => c < 128);
        int nonAscii = text.Length - asciiCount;
        return asciiCount * avgCharWidth + nonAscii * (fontSize * 0.7f);
    }

    private (bool preserveSpaces, bool preserveNewlines, bool allowWrapping) GetWhiteSpaceBehavior(WhiteSpaceMode ws)
    {
        return ws switch
        {
            WhiteSpaceMode.Pre => (true, true, false),
            WhiteSpaceMode.PreWrap => (true, true, true),
            WhiteSpaceMode.PreLine => (false, true, true),
            WhiteSpaceMode.Nowrap => (false, false, false),
            _ => (false, false, true)
        };
    }

    private float CalculateInlineElementWidth(Element element, float defaultFontSize)
    {
        var style = element.ComputedStyle;
        if (style == null) return 50;
        if (style.Width is PixelLength pw) return pw.Value;
        if (style.Width is PercentLength pwp) return pwp.Value * defaultFontSize * 10;
        if (style.Width is EmLength em) return em.Value * defaultFontSize;
        if (style.Width is RemLength rem) return rem.Value * _rootFontSize;
        return 50;
    }

    private void ApplyTextAlign(LayoutBox box, TextAlignType textAlign, float x, float availableWidth)
    {
        if (textAlign == TextAlignType.Start || textAlign == TextAlignType.Left) return;
        if (box.Lines == null) return;

        bool isLastLine = false;
        for (int i = 0; i < box.Lines.Count; i++)
        {
            var line = box.Lines[i];
            isLastLine = (i == box.Lines.Count - 1);

            float lineWidth = 0;
            if (line.Runs != null)
                foreach (var run in line.Runs) lineWidth += run.Width;

            float offset = 0;
            if (textAlign == TextAlignType.Center)
                offset = (availableWidth - lineWidth) / 2;
            else if (textAlign == TextAlignType.Right)
                offset = availableWidth - lineWidth;
            else if (textAlign == TextAlignType.Justify)
            {
                if (!isLastLine && line.Runs != null && line.Runs.Count > 1)
                {
                    float extraSpace = availableWidth - lineWidth;
                    float spacePerRun = extraSpace / (line.Runs.Count - 1);
                    float accumulated = 0;
                    for (int j = 0; j < line.Runs.Count; j++)
                    {
                        line.Runs[j].X = accumulated;
                        accumulated += line.Runs[j].Width;
                        if (j < line.Runs.Count - 1)
                            accumulated += spacePerRun;
                    }
                }
            }
            line.TextAlignOffsetX = offset;
        }
    }

    private void LayoutFlexChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;
        var children = new List<Element>();
        foreach (var c in element.Children)
            if (c is Element el && el.ComputedStyle != null && el.ComputedStyle.Display != DisplayType.None)
                children.Add(el);
        children.Sort((a, b) => (a.ComputedStyle?.Order ?? 0).CompareTo(b.ComputedStyle?.Order ?? 0));
        if (children.Count == 0) return;

        bool isRow = style.FlexDirection == FlexDirectionType.Row || style.FlexDirection == FlexDirectionType.RowReverse;
        bool isReverse = style.FlexDirection == FlexDirectionType.RowReverse || style.FlexDirection == FlexDirectionType.ColumnReverse;
        bool isWrap = style.FlexWrap != FlexWrapType.NoWrap;

        float containerWidth = box.ContentBox.Width;
        float containerHeight = box.ContentBox.Height;

        var flexItems = new List<FlexItem>();
        foreach (var child in children)
        {
            var childStyle = child.ComputedStyle;
            if (childStyle == null) continue;
            float flexBasis = CalculateFlexBasis(child, childStyle, containerWidth);
            float flexGrow = childStyle.FlexGrow;
            float flexShrink = childStyle.FlexShrink;
            float minWidth = childStyle.MinWidth is PixelLength minW ? minW.Value : 0;
            float maxWidth = childStyle.MaxWidth is PixelLength maxW ? maxW.Value : float.MaxValue;
            flexItems.Add(new FlexItem
            {
                Element = child,
                Basis = flexBasis,
                Grow = flexGrow,
                Shrink = flexShrink,
                Min = minWidth,
                Max = maxWidth,
                MarginLeft = Length.ToPixelsOrDefault(childStyle.MarginLeft),
                MarginRight = Length.ToPixelsOrDefault(childStyle.MarginRight),
                MarginTop = Length.ToPixelsOrDefault(childStyle.MarginTop),
                MarginBottom = Length.ToPixelsOrDefault(childStyle.MarginBottom)
            });
        }

        float mainAxisSize = isRow ? containerWidth : containerHeight;
        float mainGap = isRow
            ? style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight)
            : style.RowGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float crossGap = isRow
            ? style.RowGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight)
            : style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        var lines = new List<FlexLine>();
        if (isWrap)
            lines = WrapFlexItems(flexItems, mainAxisSize, isRow);
        else
            lines.Add(new FlexLine { Items = flexItems, MainSize = mainAxisSize });

        foreach (var line in lines)
            DistributeFlexSpace(line, isRow);

        float currentCross = isRow ? box.ContentBox.Top : box.ContentBox.Left;
        float availableCross = isRow ? box.ContentBox.Height : box.ContentBox.Width;

        var lineCrossSizes = new Dictionary<FlexLine, float>();
        
        // First pass: determine cross sizes per line (without modifying styles)
        foreach (var line in lines)
        {
            float maxCrossInLine = 0;
            foreach (var item in line.Items)
            {
                var childBox = CreateLayoutBox(item.Element, 0, 0, item.ComputedMainSize, box);
                if (childBox != null)
                {
                    float crossSize = isRow ? childBox.MarginBox.Height : childBox.MarginBox.Width;
                    if (crossSize > maxCrossInLine) maxCrossInLine = crossSize;
                }
            }
            lineCrossSizes[line] = maxCrossInLine;
        }

        // Second pass: position items
        foreach (var line in lines)
        {
            float lineMainStart = isRow ? box.ContentBox.Left : box.ContentBox.Top;
            float lineMainSize = isRow ? box.ContentBox.Width : box.ContentBox.Height;
            float totalGrow = 0;
            float usedMain = 0;
            foreach (var item in line.Items)
            {
                totalGrow += item.Grow;
                usedMain += item.ComputedMainSize + item.MarginLeft + item.MarginRight;
            }
            float remainingSpace = lineMainSize - usedMain;
            float gapTotal = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
            remainingSpace -= gapTotal;
            float offset = 0;
            if (remainingSpace > 0 && totalGrow == 0)
                offset = ApplyJustifyContent(style.JustifyContent, remainingSpace, line.Items.Count, isReverse);
            float mainPos = lineMainStart + offset;
            if (isReverse) mainPos = lineMainStart + lineMainSize - offset;

            float maxCrossInLine = lineCrossSizes[line];

            int itemIndex = 0;
            foreach (var item in line.Items)
            {
                float itemMainSize = item.ComputedMainSize;
                float itemX, itemY;

                // Apply align-items/align-self for cross-axis alignment
                var childStyle = item.Element.ComputedStyle;
                AlignItemsType alignType = style.AlignItems;
                if (childStyle != null && childStyle.AlignSelf != AlignSelfType.Auto)
                    alignType = (AlignItemsType)childStyle.AlignSelf;

                float crossOffset = 0;
                var crossBox = CreateLayoutBox(item.Element, 0, 0, itemMainSize, box);
                float itemCrossSize = crossBox != null ? (isRow ? crossBox.MarginBox.Height : crossBox.MarginBox.Width) : 0;
                float marginBefore = isRow ? item.MarginTop : item.MarginLeft;
                float marginAfter = isRow ? item.MarginBottom : item.MarginRight;
                float itemCrossSpace = maxCrossInLine - itemCrossSize - marginBefore - marginAfter;

                if (itemCrossSpace > 0)
                {
                    crossOffset = alignType switch
                    {
                        AlignItemsType.FlexEnd => itemCrossSpace,
                        AlignItemsType.Center => itemCrossSpace / 2,
                        AlignItemsType.Baseline => 0,
                        AlignItemsType.Stretch => itemCrossSpace,
                        _ => 0
                    };
                }

                if (isRow)
                {
                    itemX = mainPos + item.MarginLeft;
                    itemY = currentCross + marginBefore + crossOffset;
                }
                else
                {
                    itemX = currentCross + marginBefore + crossOffset;
                    itemY = mainPos + item.MarginTop;
                }

                var itemBox = CreateLayoutBox(item.Element, itemX, itemY, itemMainSize, box);
                if (itemBox != null)
                {
                    box.Children.Add(itemBox);
                    itemBox.Parent = box;
                    if (isRow)
                    {
                        mainPos += itemMainSize + item.MarginLeft + item.MarginRight;
                        if (itemIndex < line.Items.Count - 1)
                            mainPos += mainGap;
                    }
                    else
                    {
                        mainPos += item.ComputedMainSize + item.MarginTop + item.MarginBottom;
                        if (itemIndex < line.Items.Count - 1)
                            mainPos += mainGap;
                    }
                }
                itemIndex++;
            }
            if (isRow)
            {
                currentCross += maxCrossInLine + crossGap;
            }
            else
            {
                currentCross += maxCrossInLine + crossGap;
            }
        }
        AdjustBoxHeightFromContent(box);
    }

    private float CalculateFlexBasis(Element element, ComputedStyle style, float containerSize)
    {
        if (style.FlexBasis is PixelLength p) return p.Value;
        if (style.FlexBasis is PercentLength pct) return pct.Value * containerSize;
        if (style.FlexBasis is AutoLength)
        {
            if (style.Width is PixelLength w) return w.Value;
            if (style.Width is PercentLength wp) return wp.Value * containerSize;
            // flex-basis: auto + width: auto → use intrinsic content width
            float intrinsic = MeasureIntrinsicWidth(element, style);
            if (intrinsic > 0) return intrinsic;
        }
        return 0;
    }

    private float MeasureIntrinsicWidth(Element element, ComputedStyle style)
    {
        float totalTextWidth = 0;
        CollectTextWidth(element, style, ref totalTextWidth);
        if (totalTextWidth > 0) return totalTextWidth;

        float childrenMaxRight = 0;
        foreach (var child in element.Children)
        {
            if (child is Element childEl && childEl.LayoutBox != null)
            {
                float right = childEl.LayoutBox.MarginBox.Right;
                if (right > childrenMaxRight) childrenMaxRight = right;
            }
        }
        if (childrenMaxRight > 0) return childrenMaxRight;

        float fallback = MeasureTextWidth(element.TextContent ?? "", style.FontSize, style.FontFamily);
        return fallback > 0 ? fallback : 50;
    }

    private void CollectTextWidth(Element element, ComputedStyle parentStyle, ref float maxWidth)
    {
        foreach (var child in element.Children)
        {
            if (child is TextNode tn)
            {
                string text = tn.TextContent ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    float w = MeasureTextWidth(text, parentStyle.FontSize, parentStyle.FontFamily);
                    if (w > maxWidth) maxWidth = w;
                }
            }
            else if (child is Element childEl && childEl.ComputedStyle != null)
            {
                CollectTextWidth(childEl, childEl.ComputedStyle, ref maxWidth);
            }
        }
    }

    private List<FlexLine> WrapFlexItems(List<FlexItem> items, float mainSize, bool isRow)
    {
        var lines = new List<FlexLine>();
        var currentLine = new FlexLine();
        float currentMain = 0;
        foreach (var item in items)
        {
            float itemSize = item.Basis + item.MarginLeft + item.MarginRight;
            if (currentLine.Items.Count > 0 && currentMain + itemSize > mainSize)
            {
                currentLine.MainSize = currentMain;
                lines.Add(currentLine);
                currentLine = new FlexLine();
                currentMain = 0;
            }
            currentLine.Items.Add(item);
            currentMain += itemSize;
        }
        if (currentLine.Items.Count > 0)
        {
            currentLine.MainSize = currentMain;
            lines.Add(currentLine);
        }
        return lines;
    }

    private float GetPixelLength(Length length, float defaultValue)
    {
        return length is PixelLength pixelLength ? pixelLength.Value : defaultValue;
    }

    private void DistributeFlexSpace(FlexLine line, bool isRow)
    {
        float totalGrow = 0;
        float totalShrink = 0;
        float usedSpace = 0;
        foreach (var item in line.Items)
        {
            totalGrow += item.Grow;
            totalShrink += item.Shrink * item.Basis;
            usedSpace += item.Basis + item.MarginLeft + item.MarginRight;
        }
        float remainingSpace = line.MainSize - usedSpace;
        if (remainingSpace > 0 && totalGrow > 0)
        {
            foreach (var item in line.Items)
            {
                if (item.Grow > 0)
                {
                    float growRatio = item.Grow / totalGrow;
                    item.ComputedMainSize = item.Basis + growRatio * remainingSpace;
                }
                else
                    item.ComputedMainSize = item.Basis;
            }
        }
        else if (remainingSpace < 0 && totalShrink > 0)
        {
            float shrinkFactor = Math.Abs(remainingSpace) / totalShrink;
            foreach (var item in line.Items)
            {
                if (item.Shrink > 0)
                {
                    float shrinkAmount = item.Shrink * item.Basis * shrinkFactor;
                    item.ComputedMainSize = Math.Max(item.Min, item.Basis - shrinkAmount);
                }
                else
                    item.ComputedMainSize = item.Basis;
            }
        }
        else
        {
            foreach (var item in line.Items)
                item.ComputedMainSize = item.Basis;
        }
        foreach (var item in line.Items)
            item.ComputedMainSize = LayoutMath.RoundToDevicePixel(item.ComputedMainSize, _dpiScale);
    }

    private float ApplyJustifyContent(JustifyContentType justifyContent, float remainingSpace, int itemCount, bool isReverse)
    {
        return justifyContent switch
        {
            JustifyContentType.FlexEnd => remainingSpace,
            JustifyContentType.Center => remainingSpace / 2,
            JustifyContentType.SpaceBetween when itemCount > 1 => 0,
            JustifyContentType.SpaceAround => remainingSpace / (itemCount * 2),
            JustifyContentType.SpaceEvenly => remainingSpace / (itemCount + 1),
            _ => 0
        };
    }

    private class FlexItem
    {
        public Element Element { get; set; } = null!;
        public float Basis { get; set; }
        public float Grow { get; set; }
        public float Shrink { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public float MarginLeft { get; set; }
        public float MarginRight { get; set; }
        public float MarginTop { get; set; }
        public float MarginBottom { get; set; }
        public float ComputedMainSize { get; set; }
    }

    private class FlexLine
    {
        public List<FlexItem> Items { get; set; } = new();
        public float MainSize { get; set; }
    }

    private LayoutBox LayoutAbsolute(Element element, float finalHeight, float finalWidth, LayoutBox containingBlock, LayoutBox? parentBox)
    {
        var style = element.ComputedStyle;
        if (style == null) return new LayoutBox();

        float left = float.NaN, right = float.NaN, top = float.NaN, bottom = float.NaN;
        bool isAutoLeft = style.Left is AutoLength || style.Left == null;
        bool isAutoRight = style.Right is AutoLength || style.Right == null;
        bool isAutoTop = style.Top is AutoLength || style.Top == null;
        bool isAutoBottom = style.Bottom is AutoLength || style.Bottom == null;

        if (!isAutoLeft)
        {
            if (style.Left is PixelLength sl) left = sl.Value;
            else if (style.Left is PercentLength spl) left = spl.Value * containingBlock.ContentBox.Width;
            else if (style.Left is EmLength slem) left = slem.Value * style.FontSize;
            else if (style.Left is RemLength slrem) left = slrem.Value * _rootFontSize;
        }
        if (!isAutoRight)
        {
            if (style.Right is PixelLength sr) right = sr.Value;
            else if (style.Right is PercentLength spr) right = spr.Value * containingBlock.ContentBox.Width;
            else if (style.Right is EmLength srem) right = srem.Value * style.FontSize;
            else if (style.Right is RemLength srr) right = srr.Value * _rootFontSize;
        }
        if (!isAutoTop)
        {
            if (style.Top is PixelLength st) top = st.Value;
            else if (style.Top is PercentLength spt) top = spt.Value * containingBlock.ContentBox.Height;
            else if (style.Top is EmLength stem) top = stem.Value * style.FontSize;
            else if (style.Top is RemLength strem) top = strem.Value * _rootFontSize;
        }
        if (!isAutoBottom)
        {
            if (style.Bottom is PixelLength sb) bottom = sb.Value;
            else if (style.Bottom is PercentLength sbp) bottom = sbp.Value * containingBlock.ContentBox.Height;
            else if (style.Bottom is EmLength sbe) bottom = sbe.Value * style.FontSize;
            else if (style.Bottom is RemLength sbr) bottom = sbr.Value * _rootFontSize;
        }

        float marginLeft = Length.ToPixelsOrDefault(style.MarginLeft, 0, _viewportWidth, _viewportHeight);
        float marginTop = Length.ToPixelsOrDefault(style.MarginTop, 0, _viewportWidth, _viewportHeight);
        float marginRight = Length.ToPixelsOrDefault(style.MarginRight, 0, _viewportWidth, _viewportHeight);
        float marginBottom = Length.ToPixelsOrDefault(style.MarginBottom, 0, _viewportWidth, _viewportHeight);

        // Auto margin centering: when both left/right (or top/bottom) are specified and margins are auto
        float finalLeft;
        bool autoLeftMargin = style.MarginLeft is AutoLength;
        bool autoRightMargin = style.MarginRight is AutoLength;

        if (!float.IsNaN(left) && !float.IsNaN(right))
        {
            // Both left and right specified; auto margins center the element
            float cbWidth = containingBlock.ContentBox.Width;
            float totalWidth = finalWidth + marginLeft + marginRight;
            if (!autoLeftMargin && !autoRightMargin)
            {
                // Over-constrained: left wins
                finalLeft = containingBlock.ContentBox.Left + left + marginLeft;
            }
            else if (autoLeftMargin && autoRightMargin)
            {
                // Center: auto margins share remaining space
                float remaining = cbWidth - left - right - finalWidth;
                if (remaining > 0) { marginLeft = remaining / 2; marginRight = remaining / 2; }
                finalLeft = containingBlock.ContentBox.Left + left + marginLeft;
            }
            else if (autoLeftMargin)
            {
                finalLeft = containingBlock.ContentBox.Right - right - finalWidth - marginRight;
            }
            else
            {
                finalLeft = containingBlock.ContentBox.Left + left + marginLeft;
            }
        }
        else if (!float.IsNaN(left))
            finalLeft = containingBlock.ContentBox.Left + left + marginLeft;
        else if (!float.IsNaN(right))
            finalLeft = containingBlock.ContentBox.Right - right - finalWidth - marginRight;
        else
            finalLeft = containingBlock.ContentBox.Left + marginLeft;

        float finalTop;
        bool autoTopMargin = style.MarginTop is AutoLength;
        bool autoBottomMargin = style.MarginBottom is AutoLength;

        if (!float.IsNaN(top) && !float.IsNaN(bottom))
        {
            float cbHeight = containingBlock.ContentBox.Height;
            if (!autoTopMargin && !autoBottomMargin)
            {
                finalTop = containingBlock.ContentBox.Top + top + marginTop;
            }
            else if (autoTopMargin && autoBottomMargin)
            {
                float remaining = cbHeight - top - bottom - finalHeight;
                if (remaining > 0) { marginTop = remaining / 2; marginBottom = remaining / 2; }
                finalTop = containingBlock.ContentBox.Top + top + marginTop;
            }
            else if (autoTopMargin)
            {
                finalTop = containingBlock.ContentBox.Bottom - bottom - finalHeight - marginBottom;
            }
            else
            {
                finalTop = containingBlock.ContentBox.Top + top + marginTop;
            }
        }
        else if (!float.IsNaN(top))
            finalTop = containingBlock.ContentBox.Top + top + marginTop;
        else if (!float.IsNaN(bottom))
            finalTop = containingBlock.ContentBox.Bottom - bottom - finalHeight - marginBottom;
        else
            finalTop = containingBlock.ContentBox.Top + marginTop;

        var box = new LayoutBox();
        box.MarginBox = new SKRect(finalLeft - marginLeft, finalTop - marginTop, finalLeft + finalWidth + marginRight, finalTop + finalHeight + marginBottom);
        box.BorderBox = new SKRect(finalLeft, finalTop, finalLeft + finalWidth, finalTop + finalHeight);
        box.PaddingBox = box.BorderBox;
        box.ContentBox = new SKRect(finalLeft, finalTop, finalLeft + finalWidth, finalTop + finalHeight);

        box.MarginBox = LayoutMath.RoundRect(box.MarginBox, _dpiScale);
        box.BorderBox = LayoutMath.RoundRect(box.BorderBox, _dpiScale);
        box.PaddingBox = LayoutMath.RoundRect(box.PaddingBox, _dpiScale);
        box.ContentBox = LayoutMath.RoundRect(box.ContentBox, _dpiScale);

        return box;
    }

    private void LayoutTable(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        float currentY = y;
        var columns = new List<float> { 100 };
        foreach (var child in element.Children)
        {
            if (child is Element childElement && childElement.TagName == "TR")
            {
                float colX = x;
                foreach (var td in childElement.Children.OfType<Element>())
                {
                    var cellBox = CreateLayoutBox(td, colX, currentY, columns[0], box);
                    if (cellBox != null)
                    {
                        box.Children.Add(cellBox);
                        cellBox.Parent = box;
                    }
                    colX += columns[0];
                }
                currentY += 25;
            }
        }
        box.ContentBox = new SKRect(x, y, x + availableWidth, currentY);
    }

    private void AdjustBoxHeightFromContent(LayoutBox box)
    {
        float paddingBottom = box.Dimensions?.PaddingBottom ?? 0;
        float maxBottom = box.ContentBox.Top;
        foreach (var child in box.Children)
            if (child.MarginBox.Bottom > maxBottom)
                maxBottom = child.MarginBox.Bottom;
        if (box.LineRuns != null && box.LineRuns.Count > 0)
        {
            float lastY = 0;
            foreach (var run in box.LineRuns)
                lastY = Math.Max(lastY, run.Height);
            if (box.ContentBox.Top + lastY > maxBottom)
                maxBottom = box.ContentBox.Top + lastY;
        }
        if (box.Lines != null && box.Lines.Count > 0)
        {
            var lastLine = box.Lines.Last();
            if (lastLine.Y + lastLine.Height > maxBottom)
                maxBottom = lastLine.Y + lastLine.Height;
        }
        if (maxBottom > box.ContentBox.Bottom)
        {
            float borderBottom = box.BorderBox.Bottom - box.PaddingBox.Bottom;
            float marginBottom = box.MarginBox.Bottom - box.BorderBox.Bottom;
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, maxBottom);
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, maxBottom + paddingBottom);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, maxBottom + paddingBottom + borderBottom);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, maxBottom + paddingBottom + borderBottom + marginBottom);
        }
    }

    private void GeneratePseudoElementContent(Element element, LayoutBox box, ComputedStyle style)
    {
        if (element.BeforeStyles != null && element.BeforeStyles.TryGetValue("content", out var beforeContent))
        {
            beforeContent = DecodeCssContent(beforeContent);
            if (!string.IsNullOrEmpty(beforeContent) && beforeContent != "none" && !element.HasGeneratedBefore)
            {
                var beforeNode = new TextNode(beforeContent);
                beforeNode.Parent = element;
                element.Children.Insert(0, beforeNode);
                element.HasGeneratedBefore = true;
            }
        }

        if (element.AfterStyles != null && element.AfterStyles.TryGetValue("content", out var afterContent))
        {
            afterContent = DecodeCssContent(afterContent);
            if (!string.IsNullOrEmpty(afterContent) && afterContent != "none" && !element.HasGeneratedAfter)
            {
                var afterNode = new TextNode(afterContent);
                afterNode.Parent = element;
                element.Children.Add(afterNode);
                element.HasGeneratedAfter = true;
            }
        }
    }

    private static string DecodeCssContent(string content)
    {
        content = content.Trim('"', '\'');
        if (string.IsNullOrEmpty(content)) return content;

        // Decode CSS unicode escapes: \201C -> "
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                // Read hex digits for CSS unicode escape
                int hexStart = i + 1;
                int hexEnd = hexStart;
                while (hexEnd < content.Length && char.IsLetterOrDigit(content[hexEnd]) && hexEnd - hexStart < 6)
                    hexEnd++;
                if (hexEnd > hexStart)
                {
                    var hexStr = content[hexStart..hexEnd];
                    if (int.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
                    {
                        result.Append((char)codePoint);
                        i = hexEnd - 1;
                        continue;
                    }
                }
                result.Append(content[i]);
            }
            else
            {
                result.Append(content[i]);
            }
        }
        return result.ToString();
    }

    private void ApplyTextEllipsis(LayoutBox box, ComputedStyle style)
    {
        if (box.Lines == null) return;

        float availableWidth = box.ContentBox.Width;
        if (availableWidth <= 0) return;

        for (int i = box.Lines.Count - 1; i >= 0; i--)
        {
            var line = box.Lines[i];
            float lineWidth = 0;
            foreach (var run in line.Runs) lineWidth += run.Width;

            if (lineWidth > availableWidth)
            {
                string ellipsis = "\u2026";
                float ellipsisWidth = MeasureTextWidth(ellipsis, style.FontSize, style.FontFamily);
                float currentWidth = 0;

                for (int j = 0; j < line.Runs.Count; j++)
                {
                    var run = line.Runs[j];
                    if (currentWidth + run.Width > availableWidth - ellipsisWidth)
                    {
                        float remainingSpace = availableWidth - currentWidth - ellipsisWidth;
                        if (remainingSpace > 0)
                        {
                            run.Width = remainingSpace;
                            run.Text = TruncateText(run.Text, remainingSpace, run.FontSize ?? style.FontSize, run.FontFamily ?? style.FontFamily);
                        }

                        var ellipsisRun = new InlineRun
                        {
                            Text = ellipsis,
                            Width = ellipsisWidth,
                            Height = run.Height,
                            IsText = true,
                            Node = run.Node,
                            Color = run.Color,
                            FontSize = run.FontSize,
                            FontFamily = run.FontFamily
                        };

                        line.Runs.RemoveRange(j + 1, line.Runs.Count - j - 1);
                        line.Runs.Add(ellipsisRun);
                        break;
                    }
                    currentWidth += run.Width;
                }
                break;
            }
        }
    }

    private string TruncateText(string text, float maxWidth, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return text;

        for (int i = text.Length; i > 0; i--)
        {
            string truncated = text[..i];
            float width = MeasureTextWidth(truncated, fontSize, fontFamily);
            if (width <= maxWidth) return truncated;
        }
        return "";
    }

    private (int colCount, float colWidth, float gapSize) ComputeMultiColumn(ComputedStyle style, float containerWidth)
    {
        int colCount = style.ColumnCount;
        float colWidth = 0;

        if (colCount <= 0 && style.ColumnWidth != null)
        {
            colWidth = style.ColumnWidth.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
            if (colWidth > 0)
            {
                float gap = style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                if (gap == 0) gap = 16;
                colCount = Math.Max(1, (int)((containerWidth + gap) / (colWidth + gap)));
            }
        }

        if (colCount <= 1) return (1, containerWidth, 0);

        float gapSize = style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        if (gapSize == 0) gapSize = 16;

        if (colWidth <= 0)
            colWidth = (containerWidth - gapSize * (colCount - 1)) / colCount;

        return (colCount, Math.Max(0, colWidth), gapSize);
    }

    private void ApplyMultiColumn(Element element, LayoutBox box, ComputedStyle style)
    {
        int colCount = style.ColumnCount;
        float colWidth = 0;

        if (colCount <= 0 && style.ColumnWidth != null)
        {
            colWidth = style.ColumnWidth.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
            if (colWidth > 0)
            {
                float gap = style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
                if (gap == 0) gap = 16;
                colCount = Math.Max(1, (int)((box.ContentBox.Width + gap) / (colWidth + gap)));
            }
        }

        if (colCount <= 1) return;

        float gapSize = style.ColumnGap.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        if (gapSize == 0) gapSize = 16;

        if (colWidth <= 0)
            colWidth = (box.ContentBox.Width - gapSize * (colCount - 1)) / colCount;

        box.IsMultiColumn = true;
        box.ColumnCount = colCount;
        box.ColumnWidth = colWidth;
        box.ColumnGapSize = gapSize;

        if (box.Children.Count == 0) return;

        float containerLeft = box.ContentBox.Left;
        float containerTop = box.ContentBox.Top;
        float totalContentHeight = box.ContentBox.Height;
        if (totalContentHeight <= 0)
        {
            foreach (var child in box.Children)
                if (child.MarginBox.Bottom > containerTop + totalContentHeight)
                    totalContentHeight = child.MarginBox.Bottom - containerTop;
        }
        if (totalContentHeight <= 0) return;

        float idealColumnHeight = totalContentHeight / colCount;

        var columnRanges = new List<(float top, float bottom)>();
        float accumTop = 0;
        for (int c = 0; c < colCount; c++)
        {
            float colHeight = idealColumnHeight;
            if (c == colCount - 1)
                colHeight = totalContentHeight - accumTop;
            columnRanges.Add((accumTop, accumTop + colHeight));
            accumTop += colHeight;
        }

        var columnChildren = new List<List<LayoutBox>>();
        for (int c = 0; c < colCount; c++)
            columnChildren.Add(new List<LayoutBox>());

        float cumHeight = 0;
        int currentCol = 0;
        foreach (var child in box.Children)
        {
            float childHeight = child.MarginBox.Height;
            if (childHeight <= 0) childHeight = child.BorderBox.Height;
            if (childHeight <= 0) continue;

            if (currentCol < colCount - 1 && cumHeight + childHeight > idealColumnHeight + idealColumnHeight * 0.15f && cumHeight > 0)
            {
                currentCol++;
            }

            columnChildren[currentCol].Add(child);
            cumHeight += childHeight;
        }

        for (int c = 0; c < colCount; c++)
        {
            float colX = containerLeft + c * (colWidth + gapSize);
            float colTop = containerTop;
            float currentY = colTop;

            foreach (var child in columnChildren[c])
            {
                float offsetX = colX - child.MarginBox.Left;
                float offsetY = currentY - child.MarginBox.Top;

                child.MarginBox = new SKRect(
                    child.MarginBox.Left + offsetX, currentY,
                    child.MarginBox.Right + offsetX, currentY + child.MarginBox.Height);
                child.BorderBox = new SKRect(
                    child.BorderBox.Left + offsetX, child.BorderBox.Top + offsetY,
                    child.BorderBox.Right + offsetX, child.BorderBox.Bottom + offsetY);
                child.PaddingBox = new SKRect(
                    child.PaddingBox.Left + offsetX, child.PaddingBox.Top + offsetY,
                    child.PaddingBox.Right + offsetX, child.PaddingBox.Bottom + offsetY);
                child.ContentBox = new SKRect(
                    child.ContentBox.Left + offsetX, child.ContentBox.Top + offsetY,
                    child.ContentBox.Right + offsetX, child.ContentBox.Bottom + offsetY);

                if (child.LineRuns != null)
                {
                    var newRuns = new List<InlineRun>();
                    foreach (var run in child.LineRuns)
                    {
                        newRuns.Add(new InlineRun
                        {
                            Text = run.Text,
                            X = run.X + offsetX,
                            Width = run.Width,
                            Height = run.Height,
                            Baseline = run.Baseline + offsetY,
                            Node = run.Node,
                            IsText = run.IsText,
                            Color = run.Color,
                            FontSize = run.FontSize,
                            FontFamily = run.FontFamily
                        });
                    }
                    child.LineRuns = newRuns;
                }

                if (child.Lines != null)
                {
                    var newLines = new List<LineBox>();
                    foreach (var line in child.Lines)
                    {
                        var newLine = new LineBox
                        {
                            X = line.X + offsetX,
                            Y = line.Y + offsetY,
                            Width = line.Width,
                            Height = line.Height,
                            Baseline = line.Baseline + offsetY,
                            TextAlignOffsetX = line.TextAlignOffsetX
                        };
                        if (line.Runs != null)
                        {
                            foreach (var run in line.Runs)
                            {
                                newLine.Runs.Add(new InlineRun
                                {
                                    Text = run.Text,
                                    X = run.X + offsetX,
                                    Width = run.Width,
                                    Height = run.Height,
                                    Baseline = run.Baseline + offsetY,
                                    Node = run.Node,
                                    IsText = run.IsText,
                                    Color = run.Color,
                                    FontSize = run.FontSize,
                                    FontFamily = run.FontFamily
                                });
                            }
                        }
                        newLines.Add(newLine);
                    }
                    child.Lines = newLines;
                }

                currentY = child.MarginBox.Bottom;
            }
        }

        float maxColBottom = containerTop;
        foreach (var child in box.Children)
            if (child.MarginBox.Bottom > maxColBottom)
                maxColBottom = child.MarginBox.Bottom;

        float tablePaddingBottom = style.PaddingBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float tableBorderBottom = style.BorderBottomWidth;
        float tableMarginBottom = style.MarginBottom.ToPixels(style.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float totalTableWidth = colCount * colWidth + (colCount - 1) * gapSize;

        box.ContentBox = new SKRect(containerLeft, box.ContentBox.Top, containerLeft + totalTableWidth, maxColBottom);
        box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Left + totalTableWidth, maxColBottom + tablePaddingBottom);
        box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Left + totalTableWidth, maxColBottom + tablePaddingBottom + tableBorderBottom);
        box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Left + totalTableWidth, maxColBottom + tablePaddingBottom + tableBorderBottom + tableMarginBottom);
    }
}