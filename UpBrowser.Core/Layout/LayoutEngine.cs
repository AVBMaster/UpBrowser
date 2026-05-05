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

        // 收集浮动和非浮动子元素
        var floatLeftElements = new List<Element>();
        var floatRightElements = new List<Element>();
        var normalFlowElements = new List<object>();

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

                if (childStyle.Float == FloatType.Left)
                {
                    floatLeftElements.Add(childElement);
                }
                else if (childStyle.Float == FloatType.Right)
                {
                    floatRightElements.Add(childElement);
                }
                else
                {
                    normalFlowElements.Add(childElement);
                }
            }
            else if (child is TextNode textNode)
            {
                normalFlowElements.Add(textNode);
            }
        }

        // 布局正常流元素
        float collapsedMarginBottom = 0;
        foreach (var item in normalFlowElements)
        {
            if (item is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null) continue;

                float marginTop = childStyle.MarginTop is PixelLength mt ? mt.Value : 0;
                float marginBottom = childStyle.MarginBottom is PixelLength mb ? mb.Value : 0;

                // Margin折叠：取相邻margin中的最大值
                float actualMarginTop = Math.Max(marginTop, collapsedMarginBottom);
                currentY += actualMarginTop;
                collapsedMarginBottom = 0;

                // 检查是否需要清除浮动
                if (childStyle.Clear != ClearType.None)
                {
                    float clearY = currentY;
                    if (childStyle.Clear == ClearType.Left || childStyle.Clear == ClearType.Both)
                    {
                        // 找到左侧浮动元素的最大底部位置
                        foreach (var floatElem in floatLeftElements)
                        {
                            if (floatElem.LayoutBox != null && floatElem.LayoutBox.MarginBox.Bottom > clearY)
                                clearY = floatElem.LayoutBox.MarginBox.Bottom;
                        }
                    }
                    if (childStyle.Clear == ClearType.Right || childStyle.Clear == ClearType.Both)
                    {
                        // 找到右侧浮动元素的最大底部位置
                        foreach (var floatElem in floatRightElements)
                        {
                            if (floatElem.LayoutBox != null && floatElem.LayoutBox.MarginBox.Bottom > clearY)
                                clearY = floatElem.LayoutBox.MarginBox.Bottom;
                        }
                    }
                    currentY = clearY;
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

        // 布局左侧浮动元素
        float floatY = y;
        foreach (var floatElem in floatLeftElements)
        {
            var childStyle = floatElem.ComputedStyle;
            if (childStyle == null) continue;

            float marginTop = childStyle.MarginTop is PixelLength mt ? mt.Value : 0;
            float marginLeft = childStyle.MarginLeft is PixelLength ml ? ml.Value : 0;
            float marginRight = childStyle.MarginRight is PixelLength mr ? mr.Value : 0;

            floatY += marginTop;

            float elemWidth = 0;
            if (childStyle.Width is PixelLength w) elemWidth = w.Value;
            else if (childStyle.Width is PercentLength wp) elemWidth = wp.Value * availableWidth;
            else elemWidth = 100; // 默认宽度

            float elemX = x + marginLeft;

            var childBox = CreateLayoutBox(floatElem, elemX, floatY, elemWidth, box);
            if (childBox != null)
            {
                box.Children.Add(childBox);
                childBox.Parent = box;
                floatY = Math.Max(floatY + childBox.MarginBox.Height, floatY);
            }
        }

        // 布局右侧浮动元素
        float floatRightY = y;
        foreach (var floatElem in floatRightElements)
        {
            var childStyle = floatElem.ComputedStyle;
            if (childStyle == null) continue;

            float marginTop = childStyle.MarginTop is PixelLength mt ? mt.Value : 0;
            float marginLeft = childStyle.MarginLeft is PixelLength ml ? ml.Value : 0;
            float marginRight = childStyle.MarginRight is PixelLength mr ? mr.Value : 0;

            floatRightY += marginTop;

            float elemWidth = 0;
            if (childStyle.Width is PixelLength w) elemWidth = w.Value;
            else if (childStyle.Width is PercentLength wp) elemWidth = wp.Value * availableWidth;
            else elemWidth = 100; // 默认宽度

            float elemX = x + availableWidth - elemWidth - marginRight;

            var childBox = CreateLayoutBox(floatElem, elemX, floatRightY, elemWidth, box);
            if (childBox != null)
            {
                box.Children.Add(childBox);
                childBox.Parent = box;
                floatRightY = Math.Max(floatRightY + childBox.MarginBox.Height, floatRightY);
            }
        }

        // 应用最后一个元素的折叠margin-bottom
        currentY += collapsedMarginBottom;
        y = currentY;
    }

    private void LayoutInlineChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        float lineHeight = style.LineHeight * style.FontSize;
        float baseline = y + lineHeight * 0.85f;
        float currentX = x;
        float currentY = y;
        float maxHeightInLine = lineHeight;

        var line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
        box.Lines = new List<LineBox> { line };

        bool isNowrap = style.WhiteSpace == WhiteSpaceMode.Nowrap;

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None) continue;

                // 块级子元素在inline上下文中，需要换行
                if (childStyle.Display == DisplayType.Block || childStyle.Display == DisplayType.ListItem)
                {
                    // 完成当前行
                    line.Height = maxHeightInLine;
                    currentY += maxHeightInLine;
                    baseline = currentY + lineHeight * 0.85f;
                    currentX = x;
                    maxHeightInLine = lineHeight;

                    line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                    box.Lines.Add(line);

                    // 布局块级子元素
                    var blockChildBox = CreateLayoutBox(childElement, x, currentY, availableWidth, box);
                    if (blockChildBox != null)
                    {
                        box.Children.Add(blockChildBox);
                        blockChildBox.Parent = box;
                        currentY += blockChildBox.MarginBox.Height;
                        maxHeightInLine = lineHeight;
                        baseline = currentY + lineHeight * 0.85f;
                        currentX = x;

                        line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(line);
                    }
                    continue;
                }

                // 内联元素
                float childWidth = CalculateInlineElementWidth(childElement, style.FontSize);
                float childHeight = childStyle.FontSize > 0 ? childStyle.FontSize * 1.2f : lineHeight;

                // 检查是否需要换行（nowrap时不换行）
                if (!isNowrap && currentX + childWidth > x + availableWidth && currentX > x)
                {
                    line.Height = maxHeightInLine;
                    currentY += maxHeightInLine;
                    baseline = currentY + lineHeight * 0.85f;
                    currentX = x;
                    maxHeightInLine = lineHeight;

                    line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                    box.Lines.Add(line);
                }

                var childBox = new LayoutBox();
                childBox.MarginBox = new SKRect(currentX, baseline - childHeight, currentX + childWidth, baseline);
                childBox.ContentBox = childBox.MarginBox;
                childBox.BorderBox = childBox.MarginBox;
                childBox.PaddingBox = childBox.MarginBox;
                childElement.LayoutBox = childBox;

                box.Children.Add(childBox);
                childBox.Parent = box;

                line.Runs.Add(new InlineRun
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
            else if (child is TextNode textNode)
            {
                var text = textNode.TextContent ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                // 处理white-space
                if (style.WhiteSpace == WhiteSpaceMode.Nowrap || style.WhiteSpace == WhiteSpaceMode.Pre)
                {
                    // 不换行
                    LayoutTextRun(text, textNode, style, line, ref currentX, ref currentY, ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, true);
                }
                else
                {
                    // 正常换行：按单词分割
                    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
                    {
                        LayoutTextRun(word + " ", textNode, style, line, ref currentX, ref currentY, ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, false);
                    }
                }
            }
        }

        line.Height = maxHeightInLine;

        // 应用text-align
        ApplyTextAlign(box, style.TextAlign, x, availableWidth);

        // 计算总高度
        float totalHeight = 0;
        foreach (var l in box.Lines)
        {
            totalHeight += l.Height;
        }

        box.ContentBox = new SKRect(x, y, x + availableWidth, y + totalHeight);
    }

    private void LayoutTextRun(string text, TextNode textNode, ComputedStyle style, LineBox line, 
        ref float currentX, ref float currentY, ref float baseline, ref float maxHeightInLine, 
        float x, float availableWidth, float lineHeight, bool noWrap)
    {
        if (string.IsNullOrEmpty(text)) return;

        float fontSize = style.FontSize;
        float textWidth = MeasureTextWidth(text, fontSize, style.FontFamily);
        float textHeight = fontSize;

        // 检查是否需要换行（noWrap时不换行）
        if (!noWrap && currentX + textWidth > x + availableWidth && currentX > x)
        {
            line.Height = maxHeightInLine;
            currentY += maxHeightInLine;
            baseline = currentY + lineHeight * 0.85f;
            currentX = x;
            maxHeightInLine = lineHeight;

            line = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
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

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        // 简单的文本宽度估算
        // 实际应用中应该使用SKPaint.MeasureText，但这里避免依赖SkiaSharp
        float avgCharWidth = fontSize * 0.55f;
        return text.Length * avgCharWidth;
    }

    private float CalculateInlineElementWidth(Element element, float defaultFontSize)
    {
        var style = element.ComputedStyle;
        if (style == null) return 50;

        float width = 50; // 默认宽度
        if (style.Width is PixelLength pw)
            width = pw.Value;
        else if (style.Width is PercentLength pwp)
            width = pwp.Value * 100; // 百分比相对于父容器，这里简化

        return width;
    }

    private void ApplyTextAlign(LayoutBox box, TextAlignType textAlign, float x, float availableWidth)
    {
        if (textAlign == TextAlignType.Start || textAlign == TextAlignType.Left) return;

        foreach (var line in box.Lines)
        {
            float lineWidth = 0;
            foreach (var run in line.Runs)
            {
                lineWidth += run.Width;
            }

            float offset = 0;
            if (textAlign == TextAlignType.Center)
            {
                offset = (availableWidth - lineWidth) / 2;
            }
            else if (textAlign == TextAlignType.Right)
            {
                offset = availableWidth - lineWidth;
            }
            // Justify 需要特殊处理，这里简化

            if (offset > 0)
            {
                // 调整这一行所有run的X位置
                // 注意：这里需要调整LayoutBox的位置，但当前架构下比较复杂
                // 简化：暂时不实现
            }
        }
    }

    private void LayoutFlexChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        var children = element.Children.OfType<Element>()
            .Where(e => e.ComputedStyle != null && e.ComputedStyle.Display != DisplayType.None)
            .ToList();

        if (children.Count == 0) return;

        bool isRow = style.FlexDirection == FlexDirectionType.Row || style.FlexDirection == FlexDirectionType.RowReverse;
        bool isReverse = style.FlexDirection == FlexDirectionType.RowReverse || style.FlexDirection == FlexDirectionType.ColumnReverse;
        bool isWrap = style.FlexWrap != FlexWrapType.NoWrap;

        float containerWidth = box.ContentBox.Width;
        float containerHeight = box.ContentBox.Height;

        // 收集flex项信息
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
                MarginLeft = childStyle.MarginLeft is PixelLength ml ? ml.Value : 0,
                MarginRight = childStyle.MarginRight is PixelLength mr ? mr.Value : 0,
                MarginTop = childStyle.MarginTop is PixelLength mt ? mt.Value : 0,
                MarginBottom = childStyle.MarginBottom is PixelLength mb ? mb.Value : 0
            });
        }

        if (flexItems.Count == 0) return;

        // 确定主轴尺寸
        float mainAxisSize = isRow ? containerWidth : containerHeight;
        float crossAxisSize = isRow ? containerHeight : containerWidth;

        // 处理换行
        var lines = new List<FlexLine>();
        if (isWrap)
        {
            lines = WrapFlexItems(flexItems, mainAxisSize, isRow);
        }
        else
        {
            lines.Add(new FlexLine { Items = flexItems, MainSize = mainAxisSize });
        }

        // 计算每行的主轴上的空间分配
        foreach (var line in lines)
        {
            DistributeFlexSpace(line, isRow);
        }

        // 布局所有flex项
        float currentMain = x;
        float currentCross = y;
        float maxCrossInLine = 0;

        foreach (var line in lines)
        {
            float lineMainStart = isRow ? box.ContentBox.Left : box.ContentBox.Top;
            float lineCrossStart = isRow ? box.ContentBox.Top : box.ContentBox.Left;
            float lineMainSize = isRow ? box.ContentBox.Width : box.ContentBox.Height;

            // 应用justify-content
            float totalGrow = line.Items.Sum(item => item.Grow);
            float usedMain = line.Items.Sum(item => item.ComputedMainSize + item.MarginLeft + item.MarginRight);
            float remainingSpace = lineMainSize - usedMain;

            float offset = 0;
            if (remainingSpace > 0 && totalGrow == 0)
            {
                offset = ApplyJustifyContent(style.JustifyContent, remainingSpace, line.Items.Count, isReverse);
            }

            float mainPos = lineMainStart + offset;
            if (isReverse) mainPos = lineMainStart + lineMainSize - offset;

            foreach (var item in line.Items)
            {
                float itemMainSize = item.ComputedMainSize;
                float itemCrossSize = 0;
                float itemX, itemY;

                if (isRow)
                {
                    itemX = mainPos + item.MarginLeft;
                    itemY = lineCrossStart + item.MarginTop;
                    itemCrossSize = crossAxisSize - item.MarginTop - item.MarginBottom;
                }
                else
                {
                    itemX = lineCrossStart + item.MarginLeft;
                    itemY = mainPos + item.MarginTop;
                    itemMainSize = item.ComputedMainSize;
                }

                var childBox = CreateLayoutBox(item.Element, itemX, itemY, itemMainSize, box);
                if (childBox != null)
                {
                    box.Children.Add(childBox);
                    childBox.Parent = box;

                    if (isRow)
                    {
                        mainPos += itemMainSize + item.MarginLeft + item.MarginRight;
                        if (childBox.MarginBox.Height > maxCrossInLine)
                            maxCrossInLine = childBox.MarginBox.Height;
                    }
                    else
                    {
                        mainPos += item.ComputedMainSize + item.MarginTop + item.MarginBottom;
                    }
                }
            }

            // 移动到下一行
            if (isRow)
            {
                currentCross += maxCrossInLine;
                maxCrossInLine = 0;
            }
        }

        // 更新容器高度
        AdjustBoxHeightFromContent(box);
    }

    private float CalculateFlexBasis(Element element, ComputedStyle style, float containerSize)
    {
        if (style.FlexBasis is PixelLength p)
            return p.Value;
        if (style.FlexBasis is PercentLength pct)
            return pct.Value * containerSize;
        if (style.FlexBasis is AutoLength)
        {
            if (style.Width is PixelLength w)
                return w.Value;
            if (style.Width is PercentLength wp)
                return wp.Value * containerSize;
        }
        return 0;
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

    private void DistributeFlexSpace(FlexLine line, bool isRow)
    {
        float totalGrow = line.Items.Sum(item => item.Grow);
        float totalShrink = line.Items.Sum(item => item.Shrink * item.Basis);
        float usedSpace = line.Items.Sum(item => item.Basis + item.MarginLeft + item.MarginRight);

        float remainingSpace = line.MainSize - usedSpace;

        if (remainingSpace > 0 && totalGrow > 0)
        {
            // 分配剩余空间（增长）
            foreach (var item in line.Items)
            {
                if (item.Grow > 0)
                {
                    float growRatio = item.Grow / totalGrow;
                    item.ComputedMainSize = item.Basis + growRatio * remainingSpace;
                }
                else
                {
                    item.ComputedMainSize = item.Basis;
                }
            }
        }
        else if (remainingSpace < 0 && totalShrink > 0)
        {
            // 收缩（处理溢出）
            float shrinkFactor = Math.Abs(remainingSpace) / totalShrink;
            foreach (var item in line.Items)
            {
                if (item.Shrink > 0)
                {
                    float shrinkAmount = item.Shrink * item.Basis * shrinkFactor;
                    item.ComputedMainSize = Math.Max(item.Min, item.Basis - shrinkAmount);
                }
                else
                {
                    item.ComputedMainSize = item.Basis;
                }
            }
        }
        else
        {
            // 不增长也不收缩
            foreach (var item in line.Items)
            {
                item.ComputedMainSize = item.Basis;
            }
        }
    }

    private float ApplyJustifyContent(JustifyContentType justifyContent, float remainingSpace, int itemCount, bool isReverse)
    {
        return justifyContent switch
        {
            JustifyContentType.FlexEnd => remainingSpace,
            JustifyContentType.Center => remainingSpace / 2,
            JustifyContentType.SpaceBetween when itemCount > 1 => 0, // 空间分配在items之间
            JustifyContentType.SpaceAround => remainingSpace / (itemCount * 2),
            JustifyContentType.SpaceEvenly => remainingSpace / (itemCount + 1),
            _ => 0 // FlexStart
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

    private void LayoutAbsolute(Element element, LayoutBox box, LayoutBox? parentBox, float viewportWidth = 0, float viewportHeight = 0)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        // 确定包含块
        LayoutBox? containingBlock = parentBox;
        if (style.Position == PositionType.Fixed)
        {
            // 固定定位的包含块是视口
            // 使用视口尺寸创建包含块
            float vpWidth = viewportWidth > 0 ? viewportWidth : _viewportWidth;
            float vpHeight = viewportHeight > 0 ? viewportHeight : _viewportHeight;
            containingBlock = new LayoutBox
            {
                ContentBox = new SKRect(0, 0, vpWidth, vpHeight)
            };
        }
        else if (style.Position == PositionType.Absolute)
        {
            // 绝对定位的包含块是最近的position值不为static的祖先元素
            // 简化：假设parentBox就是包含块
        }

        if (containingBlock == null) return;

        float marginLeft = style.MarginLeft is PixelLength ml ? ml.Value : 0;
        float marginRight = style.MarginRight is PixelLength mr ? mr.Value : 0;
        float marginTop = style.MarginTop is PixelLength mt ? mt.Value : 0;
        float marginBottom = style.MarginBottom is PixelLength mb ? mb.Value : 0;

        // 计算宽度
        float width = box.ContentBox.Width;
        if (style.Width is PixelLength wl)
            width = wl.Value;
        else if (style.Width is PercentLength wp)
            width = wp.Value * containingBlock.ContentBox.Width;

        // 计算高度
        float height = box.ContentBox.Height;
        if (style.Height is PixelLength hl)
            height = hl.Value;
        else if (style.Height is PercentLength hp)
            height = hp.Value * containingBlock.ContentBox.Height;

        // 确定位置
        float cbLeft = containingBlock.ContentBox.Left;
        float cbTop = containingBlock.ContentBox.Top;
        float cbRight = containingBlock.ContentBox.Right;
        float cbBottom = containingBlock.ContentBox.Bottom;

        float left = 0;
        float top = 0;

        // 处理left
        if (style.Left is PixelLength sl)
            left = sl.Value + marginLeft;
        else if (style.Left is PercentLength spl)
            left = spl.Value * containingBlock.ContentBox.Width + marginLeft;

        // 处理top
        if (style.Top is PixelLength st)
            top = st.Value + marginTop;
        else if (style.Top is PercentLength spt)
            top = spt.Value * containingBlock.ContentBox.Height + marginTop;

        // 处理right（如果left未指定）
        if (style.Right is PixelLength sr && style.Left == null)
        {
            float rightOffset = sr.Value + marginRight;
            left = cbRight - rightOffset - width;
        }

        // 处理bottom（如果top未指定）
        if (style.Bottom is PixelLength sb && style.Top == null)
        {
            float bottomOffset = sb.Value + marginBottom;
            top = cbBottom - bottomOffset - height;
        }

        // 设置布局盒的位置和尺寸
        box.MarginBox = new SKRect(cbLeft + left, cbTop + top, 
                                  cbLeft + left + width + marginRight, 
                                  cbTop + top + height + marginBottom);
        box.BorderBox = new SKRect(cbLeft + left + marginLeft, cbTop + top + marginTop,
                                  cbLeft + left + marginLeft + width, 
                                  cbTop + top + marginTop + height);
        box.PaddingBox = box.BorderBox;
        box.ContentBox = new SKRect(cbLeft + left + marginLeft, cbTop + top + marginTop,
                                    cbLeft + left + marginLeft + width, 
                                    cbTop + top + marginTop + height);
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