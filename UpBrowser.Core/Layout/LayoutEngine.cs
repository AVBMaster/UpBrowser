using UpBrowser.Core.Dom;
using SkiaSharp;

namespace UpBrowser.Core.Layout;

public class LayoutEngine
{
    private float _viewportWidth;
    private float _viewportHeight;
    private float _contentHeight;
    private float _rootFontSize = 16;

    public void Layout(Document document, float width, float height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
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
            {
                ClearLayoutBoxes(childElement);
            }
        }
    }

    private void CalculateContentHeight(LayoutBox box)
    {
        foreach (var child in box.Children)
        {
            CalculateContentHeight(child);
        }

        if (box.MarginBox.Bottom > _contentHeight)
        {
            _contentHeight = box.MarginBox.Bottom;
        }
    }

    private LayoutBox? CreateLayoutBox(Element element, float x, float y, float availableWidth, LayoutBox? parentBox)
    {
        var style = element.ComputedStyle;
        if (style == null) return null;

        if (style.Display == DisplayType.None)
            return null;

        var box = new LayoutBox();
        box.Dimensions = BoxDimensions.FromStyle(style);

        float width = CalculateWidth(element, availableWidth);
        float height = CalculateHeight(element, availableWidth, style);

        float marginLeft = style.MarginLeft is PixelLength ml ? ml.Value : 0;
        float marginRight = style.MarginRight is PixelLength mr ? mr.Value : 0;
        float marginTop = style.MarginTop is PixelLength mt ? mt.Value : 0;
        float marginBottom = style.MarginBottom is PixelLength mb ? mb.Value : 0;

        float borderLeft = style.BorderLeftWidth;
        float borderRight = style.BorderRightWidth;
        float borderTop = style.BorderTopWidth;
        float borderBottom = style.BorderBottomWidth;

        float paddingLeft = style.PaddingLeft is PixelLength pl ? pl.Value : 0;
        float paddingRight = style.PaddingRight is PixelLength pr ? pr.Value : 0;
        float paddingTop = style.PaddingTop is PixelLength pt ? pt.Value : 0;
        float paddingBottom = style.PaddingBottom is PixelLength pb ? pb.Value : 0;

        float contentWidth = width;
        float contentHeight = float.IsNaN(height) ? 0 : height;

        if (style.BoxSizing == BoxSizingType.BorderBox)
        {
            contentWidth = Math.Max(0, width - borderLeft - borderRight - paddingLeft - paddingRight);
            contentHeight = float.IsNaN(height) ? 0 : Math.Max(0, height - borderTop - borderBottom - paddingTop - paddingBottom);
        }

        float totalHorizontal = marginLeft + borderLeft + paddingLeft + paddingRight + borderRight + marginRight;
        if (parentBox == null && contentWidth + totalHorizontal > availableWidth)
        {
            contentWidth = Math.Max(0, availableWidth - totalHorizontal);
        }

        float totalWidth = marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight + marginRight;
        float totalHeight = marginTop + borderTop + paddingTop + contentHeight + paddingBottom + borderBottom + marginBottom;

        box.MarginBox = new SKRect(x + marginLeft, y + marginTop, x + marginLeft + totalWidth - marginRight, y + marginTop + totalHeight - marginBottom);
        box.BorderBox = new SKRect(x + marginLeft + borderLeft, y + marginTop + borderTop, x + marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight, y + marginTop + borderTop + contentHeight + paddingBottom + borderBottom);
        box.PaddingBox = new SKRect(x + marginLeft + borderLeft + paddingLeft, y + marginTop + borderTop + paddingTop, x + marginLeft + borderLeft + paddingLeft + contentWidth, y + marginTop + borderTop + paddingTop + contentHeight);
        box.ContentBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Left + contentWidth, box.PaddingBox.Top + contentHeight);

        box.LineHeight = style.LineHeight * style.FontSize;
        element.LayoutBox = box;
        box.ZIndex = style.ZIndex;

        float childX = box.ContentBox.Left;
        float childY = box.ContentBox.Top;
        float childPaddingLeft = style.PaddingLeft is PixelLength cpl ? cpl.Value : 0;
        float childPaddingRight = style.PaddingRight is PixelLength cpr ? cpr.Value : 0;
        float childAvailableWidth = Math.Max(0, box.ContentBox.Width - childPaddingLeft - childPaddingRight);

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
                LayoutTable(element, box, childX, childY, childAvailableWidth);
                break;
            case DisplayType.ListItem:
                LayoutBlockChildren(element, box, childX, ref childY, childAvailableWidth);
                break;
            default:
                LayoutBlockChildren(element, box, childX, ref childY, childAvailableWidth);
                break;
        }

        AdjustBoxHeightFromContent(box);

        if (style.Position == PositionType.Absolute)
        {
            LayoutAbsolute(element, box, parentBox);
        }

        return box;
    }

    private float CalculateWidth(Element element, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return availableWidth;

        if (style.Width is PixelLength w)
            return w.Value;
        if (style.Width is PercentLength wp)
            return wp.Value * availableWidth;
        if (style.Width is AutoLength)
            return availableWidth;

        return availableWidth;
    }

    private float CalculateHeight(Element element, float availableWidth, ComputedStyle style)
    {
        if (style.Height is PixelLength h)
            return h.Value;
        if (style.Height is PercentLength hp)
            return hp.Value * availableWidth;
        if (style.Height is AutoLength || style.Height is null)
            return float.NaN; // Auto height - will be calculated from content

        return 0;
    }

    private void LayoutBlockChildren(Element element, LayoutBox box, float x, ref float y, float availableWidth)
    {
        float currentY = y;
        var style = element.ComputedStyle;
        float fontSize = style?.FontSize ?? 16f;
        float lineHeight = (style?.LineHeight ?? 1.2f) * fontSize;

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null) continue;
                if (childStyle.Display == DisplayType.None) continue;
                if (childStyle.Visibility == VisibilityType.Hidden) continue;

                if (childStyle.Position == PositionType.Absolute)
                {
                    var childBox = CreateLayoutBox(childElement, 0, 0, availableWidth, box);
                    if (childBox != null) box.Children.Add(childBox);
                    continue;
                }

                float marginTop = childStyle.MarginTop is PixelLength mt ? mt.Value : 0;
                float marginBottom = childStyle.MarginBottom is PixelLength mb ? mb.Value : 0;

                currentY += marginTop;

                var childBox2 = CreateLayoutBox(childElement, x, currentY, availableWidth, box);
                if (childBox2 != null)
                {
                    box.Children.Add(childBox2);
                    childBox2.Parent = box;
                    currentY += childBox2.MarginBox.Height - marginTop;
                    currentY += marginBottom;
                }
            }
            else if (child is TextNode textNode)
            {
                if (!textNode.IsWhitespaceOnly && style != null)
                {
                    var text = textNode.TextContent ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        float textWidth = text.Length * fontSize * 0.55f;
                        
                        box.LineRuns ??= new List<InlineRun>();
                        box.LineRuns.Add(new InlineRun
                        {
                            Text = text,
                            Width = textWidth,
                            Height = fontSize,
                            IsText = true,
                            Node = textNode,
                            Color = style.Color,
                            FontSize = fontSize,
                            FontFamily = style.FontFamily
                        });
                    }
                }
            }
        }
    }

    private void LayoutInlineChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        float currentX = x;
        float lineY = y;
        float lineHeight = style.LineHeight * style.FontSize;
        float baseline = lineY + lineHeight * 0.85f;

        var line = new LineBox { Y = lineY, Baseline = baseline, Height = lineHeight };
        box.Lines = new List<LineBox> { line };

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None) continue;

                if (childStyle.Display == DisplayType.Block || childStyle.Display == DisplayType.ListItem)
                {
                    line = new LineBox { Y = lineY, Baseline = lineY + lineHeight * 0.85f, Height = lineHeight };
                    box.Lines.Add(line);
                    var childBox = CreateLayoutBox(childElement, x, lineY, availableWidth, box);
                    if (childBox != null)
                    {
                        box.Children.Add(childBox);
                        childBox.Parent = box;
                        lineY += childBox.MarginBox.Height;
                        lineHeight = lineHeight;
                        baseline = lineY + lineHeight * 0.85f;
                        line = new LineBox { Y = lineY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(line);
                    }
                    continue;
                }

                var childFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : 16f;
                var childBox2 = CreateLayoutBox(childElement, currentX, baseline - childFontSize, availableWidth - (currentX - x), box);
                if (childBox2 != null)
                {
                    float childWidth = childBox2.MarginBox.Width;
                    float childHeight = childBox2.MarginBox.Height;

                    if (currentX + childWidth > x + availableWidth && currentX > x)
                    {
                        lineY += lineHeight;
                        baseline = lineY + lineHeight * 0.85f;
                        currentX = x;
                        line = new LineBox { Y = lineY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(line);
                    }

                    childBox2.MarginBox = new SKRect(currentX, baseline - childHeight, currentX + childWidth, baseline);
                    childBox2.ContentBox = childBox2.MarginBox;
                    childElement.LayoutBox = childBox2;

                    box.Children.Add(childBox2);
                    childBox2.Parent = box;

                    currentX += childWidth;
                    if (childHeight > lineHeight) lineHeight = childHeight;
                }
            }
            else if (child is TextNode textNode)
            {
                var text = textNode.TextContent ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                float fontSize = style.FontSize;
                float textWidth = text.Length * fontSize * 0.55f;

                if (currentX + textWidth > x + availableWidth && currentX > x)
                {
                    lineY += lineHeight;
                    baseline = lineY + lineHeight * 0.85f;
                    currentX = x;
                    line = new LineBox { Y = lineY, Baseline = baseline, Height = lineHeight };
                    box.Lines.Add(line);
                }

                var run = new InlineRun
                {
                    Text = text,
                    Width = textWidth,
                    Height = fontSize,
                    IsText = true,
                    Color = style.Color,
                    FontSize = fontSize,
                    FontFamily = style.FontFamily,
                    Node = textNode
                };
                line.Runs.Add(run);

                currentX += textWidth;
            }
        }

        box.ContentBox = new SKRect(x, y, currentX, lineY + lineHeight);
    }

private void LayoutFlexChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        float containerPaddingLeft = style.PaddingLeft is PixelLength cpl ? cpl.Value : 0;
        float containerPaddingRight = style.PaddingRight is PixelLength cpr ? cpr.Value : 0;
        float flexContentWidth = box.ContentBox.Width - containerPaddingLeft - containerPaddingRight;

        var items = new List<(Element element, LayoutBox box)>();

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None) continue;

                var childBox = CreateLayoutBox(childElement, 0, 0, flexContentWidth, box);
                if (childBox != null)
                {
                    items.Add((childElement, childBox));
                }
            }
        }

        if (items.Count == 0) return;

        bool isRow = style.FlexDirection == FlexDirectionType.Row || style.FlexDirection == FlexDirectionType.RowReverse;
        float offsetX = x + containerPaddingLeft;
        float offsetY = y;
        
        float totalFlex = 0;
        foreach (var (elem, _) in items)
        {
            var childStyle = elem.ComputedStyle;
            if (childStyle?.FlexGrow > 0 || childStyle?.FlexShrink > 0)
            {
                totalFlex += childStyle.FlexGrow > 0 ? childStyle.FlexGrow : 1;
            }
            else
            {
                totalFlex += 1;
            }
        }
        
        float itemSpacing = style.JustifyContent == JustifyContentType.SpaceBetween ? 10 : 0;
        
        foreach (var (elem, childBox) in items)
        {
            var childStyle = elem.ComputedStyle;
            float flexGrow = childStyle?.FlexGrow > 0 ? childStyle.FlexGrow : 1;
            float itemWidth = (flexContentWidth / items.Count);
            
            float marginLeft = childStyle?.MarginLeft is PixelLength ml ? ml.Value : 0;
            float marginRight = childStyle?.MarginRight is PixelLength mr ? mr.Value : 0;
            float paddingLeft = childStyle?.PaddingLeft is PixelLength pl ? pl.Value : 0;
            float paddingRight = childStyle?.PaddingRight is PixelLength pr ? pr.Value : 0;
            float borderLeft = childStyle?.BorderLeftWidth ?? 0;
            float borderRight = childStyle?.BorderRightWidth ?? 0;
            
            float contentWidth = itemWidth - marginLeft - marginRight - paddingLeft - paddingRight - borderLeft - borderRight;
            contentWidth = Math.Max(0, contentWidth);
            
            float h = childBox.MarginBox.Height;
            float totalW = marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight + marginRight;

            childBox.MarginBox = new SKRect(offsetX, offsetY, offsetX + totalW, offsetY + h);
            childBox.BorderBox = new SKRect(offsetX + marginLeft, offsetY + 0, offsetX + marginLeft + borderLeft + paddingLeft + contentWidth + paddingRight + borderRight, offsetY + h);
            childBox.PaddingBox = new SKRect(offsetX + marginLeft + borderLeft, offsetY + 0, offsetX + marginLeft + borderLeft + paddingLeft + contentWidth, offsetY + h);
            childBox.ContentBox = new SKRect(offsetX + marginLeft + borderLeft + paddingLeft, offsetY + 0, offsetX + marginLeft + borderLeft + paddingLeft + contentWidth, offsetY + h);
            
            elem.LayoutBox = childBox;
            box.Children.Add(childBox);
            childBox.Parent = box;

            offsetX += totalW + itemSpacing;
        }
    }

    private void LayoutAbsolute(Element element, LayoutBox box, LayoutBox? parentBox)
    {
        var style = element.ComputedStyle;
        if (style == null || parentBox == null) return;

        float left = style.Left is PixelLength l ? l.Value : 0;
        float top = style.Top is PixelLength t ? t.Value : 0;

        float width = box.ContentBox.Width;
        if (style.Width is PixelLength wl)
            width = wl.Value;
        else if (style.Width is PercentLength wp)
            width = wp.Value * parentBox.ContentBox.Width;

        float cbLeft = parentBox.ContentBox.Left;
        float cbTop = parentBox.ContentBox.Top;

        box.MarginBox = new SKRect(cbLeft + left, cbTop + top, cbLeft + left + width, cbTop + top + box.ContentBox.Height);
        box.BorderBox = box.MarginBox;
        box.PaddingBox = box.MarginBox;
        box.ContentBox = new SKRect(cbLeft + left, cbTop + top, cbLeft + left + width, cbTop + top + box.ContentBox.Height);
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
        float maxBottom = box.ContentBox.Top;

        foreach (var child in box.Children)
        {
            if (child.MarginBox.Bottom > maxBottom)
                maxBottom = child.MarginBox.Bottom;
        }

        if (box.LineRuns != null && box.LineRuns.Count > 0)
        {
            float lastY = 0;
            foreach (var run in box.LineRuns)
            {
                if (run.Width > 0)
                {
                    lastY = Math.Max(lastY, run.Height);
                }
            }
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
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, maxBottom);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, maxBottom + borderBottom);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, maxBottom + borderBottom + marginBottom);
        }
    }
}