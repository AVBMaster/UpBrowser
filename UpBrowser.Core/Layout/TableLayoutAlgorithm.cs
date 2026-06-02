using System;
using System.Collections.Generic;
using System.Linq;
using UpBrowser.Core.Dom;
using SkiaSharp;

namespace UpBrowser.Core.Layout;

public static class TableLayoutAlgorithm
{
    public static void LayoutTable(Element tableElement, LayoutBox box, float availableWidth)
    {
        var rows = CollectRows(tableElement);
        if (rows.Count == 0) return;

        int colCount = 0;
        foreach (var row in rows)
        {
            int rowCols = 0;
            foreach (var cell in row)
            {
                int colspan = GetColspan(cell);
                rowCols += colspan;
            }
            colCount = Math.Max(colCount, rowCols);
        }

        if (colCount == 0) return;

        var style = tableElement.ComputedStyle;
        bool isFixed = style?.TableLayout == "fixed";

        float[] colWidths;
        if (isFixed)
            colWidths = CalculateFixedColumnWidths(rows, availableWidth, colCount);
        else
            colWidths = CalculateAutoColumnWidths(rows, availableWidth, colCount);

        float currentY = box.ContentBox.Top;

        bool collapse = style?.BorderCollapse == true;
        float borderSpacing = collapse ? 0 : (style?.BorderSpacing ?? 0);

        foreach (var row in rows)
        {
            float currentX = box.ContentBox.Left;
            float maxHeight = 0;

            int colIdx = 0;
            foreach (var cell in row)
            {
                if (colIdx >= colCount) break;

                int colspan = GetColspan(cell);

                float cellWidth = 0;
                for (int c = 0; c < colspan && (colIdx + c) < colCount; c++)
                    cellWidth += colWidths[colIdx + c];
                cellWidth += borderSpacing * (colspan - 1);

                var cellBox = CreateCellBox(cell, currentX, currentY, cellWidth);
                if (cellBox != null)
                {
                    box.Children.Add(cellBox);
                    cellBox.Parent = box;
                    if (cellBox.MarginBox.Height > maxHeight)
                        maxHeight = cellBox.MarginBox.Height;
                }

                currentX += cellWidth + borderSpacing;
                colIdx += colspan;
            }

            currentY += maxHeight + borderSpacing;
        }

        box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top,
            box.ContentBox.Left + availableWidth, currentY);
        box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top,
            box.PaddingBox.Left + availableWidth, currentY + (style?.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0));
        box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top,
            box.BorderBox.Left + availableWidth,
            currentY + (style?.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0) + style?.BorderBottomWidth ?? 0);
        box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top,
            box.MarginBox.Left + availableWidth,
            currentY + (style?.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0) + (style?.BorderBottomWidth ?? 0) + (style?.MarginBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0));
    }

    private static List<List<Element>> CollectRows(Element table)
    {
        var rows = new List<List<Element>>();

        foreach (var child in table.Children)
        {
            if (child is not Element el) continue;
            var tag = el.TagName;

            if (tag == "TR")
            {
                var cells = new List<Element>();
                foreach (var cell in el.Children)
                {
                    if (cell is Element cellEl && (cellEl.TagName == "TD" || cellEl.TagName == "TH"))
                        cells.Add(cellEl);
                }
                if (cells.Count > 0) rows.Add(cells);
            }
            else if (tag == "THEAD" || tag == "TBODY" || tag == "TFOOT")
            {
                foreach (var sectionChild in el.Children)
                {
                    if (sectionChild is Element sectionEl && sectionEl.TagName == "TR")
                    {
                        var cells = new List<Element>();
                        foreach (var cell in sectionEl.Children)
                        {
                            if (cell is Element cellEl && (cellEl.TagName == "TD" || cellEl.TagName == "TH"))
                                cells.Add(cellEl);
                        }
                        if (cells.Count > 0) rows.Add(cells);
                    }
                }
            }
        }

        return rows;
    }

    private static int GetColspan(Element cell)
    {
        var colspanStr = cell.GetAttribute("colspan");
        if (int.TryParse(colspanStr, out var colspan) && colspan > 0) return colspan;
        return 1;
    }

    private static float[] CalculateFixedColumnWidths(List<List<Element>> rows, float availableWidth, int colCount)
    {
        var widths = new float[colCount];
        float perCol = availableWidth / colCount;
        for (int i = 0; i < colCount; i++) widths[i] = perCol;
        return widths;
    }

    private static float[] CalculateAutoColumnWidths(List<List<Element>> rows, float availableWidth, int colCount)
    {
        var widths = new float[colCount];
        var maxContentWidths = new float[colCount];

        foreach (var row in rows)
        {
            int colIdx = 0;
            foreach (var cell in row)
            {
                if (colIdx >= colCount) break;
                int colspan = GetColspan(cell);

                float contentWidth = MeasureCellContent(cell);
                float perColWidth = contentWidth / colspan;

                for (int c = 0; c < colspan && (colIdx + c) < colCount; c++)
                    maxContentWidths[colIdx + c] = Math.Max(maxContentWidths[colIdx + c], perColWidth);

                colIdx += colspan;
            }
        }

        float totalContent = maxContentWidths.Sum();
        if (totalContent <= 0)
        {
            float perCol = availableWidth / colCount;
            for (int i = 0; i < colCount; i++) widths[i] = perCol;
        }
        else
        {
            for (int i = 0; i < colCount; i++)
                widths[i] = maxContentWidths[i] / totalContent * availableWidth;
        }

        return widths;
    }

    private static float MeasureCellContent(Element cell)
    {
        float maxWidth = 0;
        foreach (var child in cell.Children)
        {
            if (child is Element el)
            {
                var childStyle = el.ComputedStyle;
                if (childStyle != null)
                {
                    float fontSize = childStyle.FontSize;
                    string text = el.TextContent ?? "";
                    float textWidth = text.Length * fontSize * 0.6f;
                    maxWidth = Math.Max(maxWidth, textWidth);
                }
            }
            else if (child is TextNode tn)
            {
                string text = tn.TextContent ?? "";
                var parentStyle = cell.ComputedStyle;
                float fontSize = parentStyle?.FontSize ?? 16;
                float textWidth = text.Length * fontSize * 0.6f;
                maxWidth = Math.Max(maxWidth, textWidth);
            }
        }
        return maxWidth;
    }

    private static float MeasureCellContentHeight(Element cell, float contentWidth)
    {
        var cellStyle = cell.ComputedStyle;
        float defaultFontSize = cellStyle?.FontSize ?? 16;
        float defaultLineHeight = (cellStyle?.LineHeight > 0 ? cellStyle.LineHeight : 1.5f) * defaultFontSize;
        float totalHeight = 0;

        foreach (var child in cell.Children)
        {
            if (child is Element el)
            {
                var childStyle = el.ComputedStyle;
                if (childStyle != null)
                {
                    float fontSize = childStyle.FontSize;
                    float lineHeight = (childStyle.LineHeight > 0 ? childStyle.LineHeight : 1.5f) * fontSize;
                    string text = el.TextContent ?? "";
                    if (!string.IsNullOrEmpty(text) && contentWidth > 0)
                    {
                        float textWidth = text.Length * fontSize * 0.6f;
                        int lines = Math.Max(1, (int)Math.Ceiling(textWidth / contentWidth));
                        totalHeight += lines * lineHeight;
                    }
                    else
                    {
                        totalHeight += lineHeight;
                    }
                }
            }
            else if (child is TextNode tn)
            {
                string text = tn.TextContent ?? "";
                if (!string.IsNullOrEmpty(text) && contentWidth > 0)
                {
                    float textWidth = text.Length * defaultFontSize * 0.6f;
                    int lines = Math.Max(1, (int)Math.Ceiling(textWidth / contentWidth));
                    totalHeight += lines * defaultLineHeight;
                }
                else
                {
                    totalHeight += defaultLineHeight;
                }
            }
        }

        if (totalHeight <= 0)
            totalHeight = defaultLineHeight;

        return totalHeight;
    }

    private static LayoutBox? CreateCellBox(Element cell, float x, float y, float width)
    {
        var style = cell.ComputedStyle;
        if (style == null) return null;

        float paddingTop = style.PaddingTop.ToPixels(style.FontSize, 16, 0, 0);
        float paddingBottom = style.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0);
        float paddingLeft = style.PaddingLeft.ToPixels(style.FontSize, 16, 0, 0);
        float paddingRight = style.PaddingRight.ToPixels(style.FontSize, 16, 0, 0);

        float contentWidth = Math.Max(0, width - paddingLeft - paddingRight);
        float contentHeight = MeasureCellContentHeight(cell, contentWidth);

        var box = new LayoutBox();
        box.ContentBox = new SKRect(x + paddingLeft, y + paddingTop, x + paddingLeft + contentWidth, y + paddingTop + contentHeight);
        box.PaddingBox = new SKRect(x, y + paddingTop, x + width, y + paddingTop + contentHeight + paddingBottom);
        box.BorderBox = new SKRect(x, y, x + width, y + paddingTop + contentHeight + paddingBottom);
        box.MarginBox = new SKRect(x, y, x + width, y + paddingTop + contentHeight + paddingBottom);

        cell.LayoutBox = box;
        return box;
    }
}
