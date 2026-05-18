using UpBrowser.Core.Dom;
using SkiaSharp;
using System.Text;

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

    private string GetButtonTextFromElement(Element button)
    {
        var textBuilder = new StringBuilder();
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
            return result;

        var valueAttr = button.GetAttribute("value");
        return !string.IsNullOrEmpty(valueAttr) ? valueAttr : "Button";
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
        float parentHeight = parentBox?.ContentBox.Height ?? _viewportHeight;
        float elementHeight = CalculateHeight(element, availableWidth, parentHeight, style);

        bool isAutoMarginLeft = style.MarginLeft is AutoLength;
        bool isAutoMarginRight = style.MarginRight is AutoLength;
        float marginLeft = isAutoMarginLeft ? 0 : Length.ToPixelsOrDefault(style.MarginLeft);
        float marginRight = isAutoMarginRight ? 0 : Length.ToPixelsOrDefault(style.MarginRight);
        float marginTop = Length.ToPixelsOrDefault(style.MarginTop);
        float marginBottom = Length.ToPixelsOrDefault(style.MarginBottom);

        float borderLeft = style.BorderLeftWidth;
        float borderRight = style.BorderRightWidth;
        float borderTop = style.BorderTopWidth;
        float borderBottom = style.BorderBottomWidth;

        float paddingLeft = Length.ToPixelsOrDefault(style.PaddingLeft);
        float paddingRight = Length.ToPixelsOrDefault(style.PaddingRight);
        float paddingTop = Length.ToPixelsOrDefault(style.PaddingTop);
        float paddingBottom = Length.ToPixelsOrDefault(style.PaddingBottom);

        float contentWidth = width;
        float contentHeight = float.IsNaN(elementHeight) ? 0 : elementHeight;

        if (style.BoxSizing == BoxSizingType.BorderBox)
        {
            contentWidth = Math.Max(0, width - borderLeft - borderRight - paddingLeft - paddingRight);
            contentHeight = float.IsNaN(elementHeight) ? 0 : Math.Max(0, elementHeight - borderTop - borderBottom - paddingTop - paddingBottom);
        }

        float totalHorizontal = marginLeft + borderLeft + paddingLeft + paddingRight + borderRight + marginRight;
        if (parentBox == null && contentWidth + totalHorizontal > availableWidth)
        {
            contentWidth = Math.Max(0, availableWidth - totalHorizontal);
        }

        // Auto-margin horizontal centering for block-level elements
        bool isBlockLevel = style.Display != DisplayType.Inline && style.Display != DisplayType.InlineFlex &&
                            style.Display != DisplayType.InlineBlock && style.Position != PositionType.Absolute;
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
                {
                    marginLeft = remaining;
                }
                else
                {
                    marginRight = remaining;
                }
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

        box.LineHeight = style.LineHeight * style.FontSize;
        element.LayoutBox = box;
        box.ZIndex = style.ZIndex;

        float childX = box.ContentBox.Left;
        float childY = box.ContentBox.Top;
        float childPaddingLeft = Length.ToPixelsOrDefault(style.PaddingLeft);
        float childPaddingRight = Length.ToPixelsOrDefault(style.PaddingRight);
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

        // Shrink-to-fit for inline/inline-block elements with auto width
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
                {
                    if (child.MarginBox.Right > childrenMaxRight)
                        childrenMaxRight = child.MarginBox.Right;
                }
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
                float newContentWidth = Math.Min(contentWidthFromContent + paddingLeft + paddingRight, availableWidth);
                float oldRight = box.ContentBox.Right;
                float newRight = box.ContentBox.Left + newContentWidth;
                float diff = oldRight - newRight;

                if (diff > 0)
                {
                    box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + newContentWidth, box.ContentBox.Bottom);
                    box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right - diff, box.PaddingBox.Bottom);
                    box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right - diff, box.BorderBox.Bottom);
                    box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right - diff, box.MarginBox.Bottom);
                }
            }
        }

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

    private float CalculateHeight(Element element, float availableWidth, float parentHeight, ComputedStyle style)
    {
        if (style.Height is PixelLength h)
            return h.Value;
        if (style.Height is PercentLength hp)
            return hp.Value * (parentHeight > 0 ? parentHeight : _viewportHeight);
        if (style.Height is AutoLength || style.Height is null)
            return float.NaN;

        return 0;
    }

    private void LayoutBlockChildren(Element element, LayoutBox box, float x, ref float y, float availableWidth)
    {
        float currentY = y;
        var style = element.ComputedStyle;
        float fontSize = style?.FontSize ?? 16f;
        float lineHeight = (style?.LineHeight ?? 1.2f) * fontSize;

        var floatLeftElements = new List<Element>();
        var floatRightElements = new List<Element>();
        var normalFlowElements = new List<Node>();

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

        float collapsedMarginBottom = 0;
        foreach (var item in normalFlowElements)
        {
            if (item is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null) continue;

                float marginTop = Length.ToPixelsOrDefault(childStyle.MarginTop);
                float marginBottom = Length.ToPixelsOrDefault(childStyle.MarginBottom);

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
                    string text = textSb.ToString().Trim();
                    if (!string.IsNullOrEmpty(text) && firstTextNode != null)
                    {
                        float inlineFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : fontSize;
                        float textWidth = text.Length * inlineFontSize * 0.55f;

                        box.LineRuns ??= new List<InlineRun>();
                        box.LineRuns.Add(new InlineRun
                        {
                            Text = text,
                            Width = textWidth,
                            Height = inlineFontSize,
                            IsText = true,
                            Node = firstTextNode,
                            Color = childStyle.Color.Alpha > 0 ? childStyle.Color : style.Color,
                            FontSize = inlineFontSize,
                            FontFamily = childStyle.FontFamily ?? style.FontFamily
                        });
                    }
                    continue;
                }

                if (childStyle.Clear != ClearType.None)
                {
                    float clearY = currentY;
                    if (childStyle.Clear == ClearType.Left || childStyle.Clear == ClearType.Both)
                    {
                        foreach (var floatElem in floatLeftElements)
                        {
                            if (floatElem.LayoutBox != null && floatElem.LayoutBox.MarginBox.Bottom > clearY)
                                clearY = floatElem.LayoutBox.MarginBox.Bottom;
                        }
                    }
                    if (childStyle.Clear == ClearType.Right || childStyle.Clear == ClearType.Both)
                    {
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

        float floatY = y;
        foreach (var floatElem in floatLeftElements)
        {
            var childStyle = floatElem.ComputedStyle;
            if (childStyle == null) continue;

            float marginTop = Length.ToPixelsOrDefault(childStyle.MarginTop);
            float marginLeft = Length.ToPixelsOrDefault(childStyle.MarginLeft);

            float elemWidth = 0;
            if (childStyle.Width is PixelLength w) elemWidth = w.Value;
            else if (childStyle.Width is PercentLength wp) elemWidth = wp.Value * availableWidth;
            else elemWidth = 100;

            float elemX = x + marginLeft;
            var childBox = CreateLayoutBox(floatElem, elemX, floatY, elemWidth, box);
            if (childBox != null)
            {
                box.Children.Add(childBox);
                childBox.Parent = box;
                floatY = Math.Max(floatY + childBox.MarginBox.Height, floatY);
            }
        }

        float floatRightY = y;
        foreach (var floatElem in floatRightElements)
        {
            var childStyle = floatElem.ComputedStyle;
            if (childStyle == null) continue;

            float marginTop = Length.ToPixelsOrDefault(childStyle.MarginTop);
            float marginRight = Length.ToPixelsOrDefault(childStyle.MarginRight);
            floatRightY += marginTop;

            float elemWidth = 0;
            if (childStyle.Width is PixelLength w) elemWidth = w.Value;
            else if (childStyle.Width is PercentLength wp) elemWidth = wp.Value * availableWidth;
            else elemWidth = 100;

            float elemX = x + availableWidth - elemWidth - marginRight;
            var childBox = CreateLayoutBox(floatElem, elemX, floatRightY, elemWidth, box);
            if (childBox != null)
            {
                box.Children.Add(childBox);
                childBox.Parent = box;
                floatRightY = Math.Max(floatRightY + childBox.MarginBox.Height, floatRightY);
            }
        }

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

        var currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
        box.Lines = new List<LineBox> { currentLine };

        bool isNowrap = style.WhiteSpace == WhiteSpaceMode.Nowrap;

        foreach (var child in element.Children)
        {
            if (child is Element childElement)
            {
                var childStyle = childElement.ComputedStyle;
                if (childStyle == null || childStyle.Display == DisplayType.None) continue;

                // UpBrowser.Core\Layout\LayoutEngine.cs
                // 在 LayoutInlineChildren 方法中替换按钮处理部分：

                if (childElement.TagName.Equals("BUTTON", StringComparison.OrdinalIgnoreCase))
                {
                    string btnText = GetButtonTextFromElement(childElement);
                    if (string.IsNullOrEmpty(btnText)) btnText = "Button";

                    float btnFontSize = childStyle.FontSize > 0 ? childStyle.FontSize : 13.3333f;
                    float btnLineHeightValue = childStyle.LineHeight > 0 ? childStyle.LineHeight : 1.2f;

                    // 测量文本宽度
                    float textWidth = MeasureTextWidth(btnText, btnFontSize, childStyle.FontFamily);

                    // 正确获取 padding 值（处理 PixelLength 类型）
                    float padTop = GetPixelLength(childStyle.PaddingTop, 4);
                    float padBottom = GetPixelLength(childStyle.PaddingBottom, 4);
                    float padLeft = GetPixelLength(childStyle.PaddingLeft, 10);
                    float padRight = GetPixelLength(childStyle.PaddingRight, 10);

                    // 正确获取 border 值
                    float borderTop = childStyle.BorderTopWidth > 0 ? childStyle.BorderTopWidth : 2;
                    float borderBottom = childStyle.BorderBottomWidth > 0 ? childStyle.BorderBottomWidth : 2;
                    float borderLeft = childStyle.BorderLeftWidth > 0 ? childStyle.BorderLeftWidth : 2;
                    float borderRight = childStyle.BorderRightWidth > 0 ? childStyle.BorderRightWidth : 2;

                    // 计算内容尺寸
                    float contentWidth = textWidth + padLeft + padRight;
                    // 内容高度 = 文字行高 + 上下内边距
                    float contentHeight = (btnFontSize * btnLineHeightValue) + padTop + padBottom;

                    // 总尺寸（包含边框）
                    float totalButtonWidth = contentWidth + borderLeft + borderRight;
                    float totalButtonHeight = contentHeight + borderTop + borderBottom;

                    // 应用自定义尺寸
                    if (childStyle.Width is PixelLength pw && pw.Value > 0)
                        totalButtonWidth = Math.Max(totalButtonWidth, pw.Value);
                    if (childStyle.Height is PixelLength ph && ph.Value > 0)
                        totalButtonHeight = Math.Max(totalButtonHeight, ph.Value);

                    // 确保最小值（防止按钮太小）
                    totalButtonWidth = Math.Max(totalButtonWidth, 75);
                    totalButtonHeight = Math.Max(totalButtonHeight, 28);

                    // 换行处理
                    if (!isNowrap && currentX + totalButtonWidth > x + availableWidth - 10 && currentX > x)
                    {
                        currentLine.Height = maxHeightInLine;
                        currentY += maxHeightInLine;
                        baseline = currentY + lineHeight * 0.85f;
                        currentX = x;
                        maxHeightInLine = lineHeight;
                        currentLine = new LineBox { Y = currentY, Baseline = baseline, Height = lineHeight };
                        box.Lines.Add(currentLine);
                    }

                    // 按钮底部对齐基线
                    float btnTop = baseline - totalButtonHeight;
                    float btnBottom = baseline;

                    // 创建布局盒模型
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

                    // 更新位置，按钮之间有间距
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

                float childWidth = CalculateInlineElementWidth(childElement, style.FontSize);
                float childHeight = childStyle.FontSize > 0 ? childStyle.FontSize * 1.2f : lineHeight;

                if (!isNowrap && currentX + childWidth > x + availableWidth && currentX > x)
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
            else if (child is TextNode textNode)
            {
                var text = textNode.TextContent ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (style.WhiteSpace == WhiteSpaceMode.Nowrap || style.WhiteSpace == WhiteSpaceMode.Pre)
                {
                    LayoutTextRun(text, textNode, style, currentLine, ref currentX, ref currentY, ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, true);
                }
                else
                {
                    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
                    {
                        LayoutTextRun(word + " ", textNode, style, currentLine, ref currentX, ref currentY, ref baseline, ref maxHeightInLine, x, availableWidth, lineHeight, false);
                    }
                }
            }
        }

        currentLine.Height = maxHeightInLine;
        ApplyTextAlign(box, style.TextAlign, x, availableWidth);

        float totalHeight = 0;
        foreach (var line in box.Lines)
        {
            totalHeight += line.Height;
        }

        float newContentBottom = y + totalHeight;
        float pdBottom = Length.ToPixelsOrDefault(style.PaddingBottom);
        float bdBottom = style.BorderBottomWidth;
        float mgBottom = Length.ToPixelsOrDefault(style.MarginBottom);

        box.ContentBox = new SKRect(x, y, x + availableWidth, newContentBottom);
        box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, newContentBottom + pdBottom);
        box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, newContentBottom + pdBottom + bdBottom);
        box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, newContentBottom + pdBottom + bdBottom + mgBottom);
    }
    private void LayoutTextRun(string text, TextNode textNode, ComputedStyle style, LineBox line,
        ref float currentX, ref float currentY, ref float baseline, ref float maxHeightInLine,
        float x, float availableWidth, float lineHeight, bool noWrap)
    {
        if (string.IsNullOrEmpty(text)) return;

        float fontSize = style.FontSize;
        float textWidth = MeasureTextWidth(text, fontSize, style.FontFamily);
        float textHeight = fontSize;

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
        if (string.IsNullOrEmpty(text)) return 0;

        if (TextMeasurer.Instance != null)
        {
            return TextMeasurer.Instance.MeasureText(text, fontFamily ?? "Arial", fontSize);
        }

        float avgCharWidth = fontSize * 0.55f;
        return text.Length * avgCharWidth;
    }

    private float CalculateInlineElementWidth(Element element, float defaultFontSize)
    {
        var style = element.ComputedStyle;
        if (style == null) return 50;

        float width = 50;
        if (style.Width is PixelLength pw)
            width = pw.Value;
        else if (style.Width is PercentLength pwp)
            width = pwp.Value * 100;
        return width;
    }

    private void ApplyTextAlign(LayoutBox box, TextAlignType textAlign, float x, float availableWidth)
    {
        if (textAlign == TextAlignType.Start || textAlign == TextAlignType.Left) return;

        foreach (var line in box.Lines)
        {
            float lineWidth = 0;
            if (line.Runs != null)
                foreach (var run in line.Runs) lineWidth += run.Width;
            float offset = 0;
            if (textAlign == TextAlignType.Center)
                offset = (availableWidth - lineWidth) / 2;
            else if (textAlign == TextAlignType.Right)
                offset = availableWidth - lineWidth;

            line.TextAlignOffsetX = offset;
        }
    }

    // ================= Flexbox 布局 =================
    private void LayoutFlexChildren(Element element, LayoutBox box, float x, float y, float availableWidth)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        var children = new List<Element>(element.Children.Count);
        foreach (var c in element.Children)
        {
            if (c is Element el && el.ComputedStyle != null && el.ComputedStyle.Display != DisplayType.None)
                children.Add(el);
        }

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

        var lines = new List<FlexLine>();
        if (isWrap)
            lines = WrapFlexItems(flexItems, mainAxisSize, isRow);
        else
            lines.Add(new FlexLine { Items = flexItems, MainSize = mainAxisSize });

        foreach (var line in lines)
        {
            DistributeFlexSpace(line, isRow);
        }

        float currentCross = isRow ? box.ContentBox.Top : box.ContentBox.Left;
        float maxCrossInLine = 0;

        foreach (var line in lines)
        {
            float lineMainStart = isRow ? box.ContentBox.Left : box.ContentBox.Top;
            float lineCrossStart = isRow ? box.ContentBox.Top : box.ContentBox.Left;
            float lineMainSize = isRow ? box.ContentBox.Width : box.ContentBox.Height;

            float totalGrow = 0;
            float usedMain = 0;
            foreach (var item in line.Items)
            {
                totalGrow += item.Grow;
                usedMain += item.ComputedMainSize + item.MarginLeft + item.MarginRight;
            }
            float remainingSpace = lineMainSize - usedMain;

            float offset = 0;
            if (remainingSpace > 0 && totalGrow == 0)
                offset = ApplyJustifyContent(style.JustifyContent, remainingSpace, line.Items.Count, isReverse);

            float mainPos = lineMainStart + offset;
            if (isReverse) mainPos = lineMainStart + lineMainSize - offset;

            foreach (var item in line.Items)
            {
                float itemMainSize = item.ComputedMainSize;
                float itemX, itemY;

                if (isRow)
                {
                    itemX = mainPos + item.MarginLeft;
                    itemY = currentCross + item.MarginTop;
                }
                else
                {
                    itemX = currentCross + item.MarginLeft;
                    itemY = mainPos + item.MarginTop;
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

            if (isRow)
            {
                currentCross += maxCrossInLine;
                maxCrossInLine = 0;
            }
        }

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

    /// <summary>
    /// 安全地从 Length 对象获取像素值
    /// </summary>
    private float GetPixelLength(Length length, float defaultValue)
    {
        if (length is PixelLength pixelLength)
            return pixelLength.Value;

        // 对于 AutoLength 或其他类型，返回默认值
        return defaultValue;
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
                {
                    item.ComputedMainSize = item.Basis;
                }
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
                {
                    item.ComputedMainSize = item.Basis;
                }
            }
        }
        else
        {
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

    // ================= 绝对定位 =================
    private void LayoutAbsolute(Element element, LayoutBox box, LayoutBox? parentBox, float viewportWidth = 0, float viewportHeight = 0)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        LayoutBox? containingBlock = parentBox;
        if (style.Position == PositionType.Fixed)
        {
            float vpWidth = viewportWidth > 0 ? viewportWidth : _viewportWidth;
            float vpHeight = viewportHeight > 0 ? viewportHeight : _viewportHeight;
            containingBlock = new LayoutBox
            {
                ContentBox = new SKRect(0, 0, vpWidth, vpHeight)
            };
        }

        if (containingBlock == null) return;

        float marginLeft = Length.ToPixelsOrDefault(style.MarginLeft);
        float marginRight = Length.ToPixelsOrDefault(style.MarginRight);
        float marginTop = Length.ToPixelsOrDefault(style.MarginTop);
        float marginBottom = Length.ToPixelsOrDefault(style.MarginBottom);

        float width = box.ContentBox.Width;
        if (style.Width is PixelLength wl) width = wl.Value;
        else if (style.Width is PercentLength wp) width = wp.Value * containingBlock.ContentBox.Width;

        float height = box.ContentBox.Height;
        if (style.Height is PixelLength hl) height = hl.Value;
        else if (style.Height is PercentLength hp) height = hp.Value * containingBlock.ContentBox.Height;

        float cbLeft = containingBlock.ContentBox.Left;
        float cbTop = containingBlock.ContentBox.Top;
        float cbRight = containingBlock.ContentBox.Right;
        float cbBottom = containingBlock.ContentBox.Bottom;

        float left = 0, top = 0;

        if (style.Left is PixelLength sl) left = sl.Value + marginLeft;
        else if (style.Left is PercentLength spl) left = spl.Value * containingBlock.ContentBox.Width + marginLeft;

        if (style.Top is PixelLength st) top = st.Value + marginTop;
        else if (style.Top is PercentLength spt) top = spt.Value * containingBlock.ContentBox.Height + marginTop;

        if (style.Right is PixelLength sr && style.Left == null)
        {
            float rightOffset = sr.Value + marginRight;
            left = cbRight - rightOffset - width;
        }

        if (style.Bottom is PixelLength sb && style.Top == null)
        {
            float bottomOffset = sb.Value + marginBottom;
            top = cbBottom - bottomOffset - height;
        }

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

    // ================= 表格布局 =================
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

    // ================= 辅助方法 =================
    private void AdjustBoxHeightFromContent(LayoutBox box)
    {
        float paddingBottom = box.Dimensions?.PaddingBottom ?? 0;
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
                    lastY = Math.Max(lastY, run.Height);
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
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, maxBottom + paddingBottom);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, maxBottom + paddingBottom + borderBottom);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, maxBottom + paddingBottom + borderBottom + marginBottom);
        }
    }
}