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

        Element? captionElement = null;
        foreach (var child in tableElement.Children)
        {
            if (child is Element el && el.TagName == "CAPTION")
            {
                captionElement = el;
                break;
            }
        }

        float captionHeight = 0;
        if (captionElement != null)
        {
            var captionStyle = captionElement.ComputedStyle;
            if (captionStyle != null)
            {
                var captionBox = new LayoutBox
                {
                    ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + availableWidth, box.ContentBox.Top),
                    BorderBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + availableWidth, box.ContentBox.Top),
                    PaddingBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + availableWidth, box.ContentBox.Top),
                    MarginBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Left + availableWidth, box.ContentBox.Top)
                };
                box.Children.Add(captionBox);
                captionBox.Parent = box;
                captionElement.LayoutBox = captionBox;

                float captionPadTop = captionStyle.PaddingTop.ToPixels(captionStyle.FontSize, 16, 0, 0);
                float captionPadBottom = captionStyle.PaddingBottom.ToPixels(captionStyle.FontSize, 16, 0, 0);
                float captionMarginTop = captionStyle.MarginTop.ToPixels(captionStyle.FontSize, 16, 0, 0);
                float captionMarginBottom = captionStyle.MarginBottom.ToPixels(captionStyle.FontSize, 16, 0, 0);
                float captionLineHeight = (captionStyle.LineHeight > 0 ? captionStyle.LineHeight : 1.2f) * captionStyle.FontSize;

                captionHeight = captionMarginTop + captionPadTop + captionLineHeight + captionPadBottom + captionMarginBottom;

                captionBox.MarginBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top + captionMarginTop, box.ContentBox.Left + availableWidth, box.ContentBox.Top + captionHeight - captionMarginBottom);
                captionBox.BorderBox = new SKRect(box.ContentBox.Left, captionBox.MarginBox.Top + captionMarginTop, box.ContentBox.Left + availableWidth, captionBox.MarginBox.Bottom - captionMarginBottom);
                captionBox.PaddingBox = new SKRect(captionBox.BorderBox.Left + (captionStyle.BorderLeftWidth), captionBox.BorderBox.Top + (captionStyle.BorderTopWidth),
                    captionBox.BorderBox.Right - (captionStyle.BorderRightWidth), captionBox.BorderBox.Bottom - (captionStyle.BorderBottomWidth));
                captionBox.ContentBox = new SKRect(captionBox.PaddingBox.Left + captionPadTop, captionBox.PaddingBox.Top + captionPadTop,
                    captionBox.PaddingBox.Right - captionPadBottom, captionBox.PaddingBox.Bottom - captionPadBottom);
                captionBox.LineHeight = captionLineHeight;
            }
        }

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

        bool collapse = style?.BorderCollapse == true;
        float borderSpacing = collapse ? 0 : (style?.BorderSpacing ?? 0);

        // In collapse mode, suppress the table's own border (cells' outer borders form the grid).
        // This prevents double-border artifacts from DrawElementBorder drawing both the table's
        // and cells' borders at nearly-identical positions after AlignRectToDevice.
        if (collapse && style != null)
        {
            style.BorderTopWidth = 0;
            style.BorderBottomWidth = 0;
            style.BorderLeftWidth = 0;
            style.BorderRightWidth = 0;
        }

        float[] colWidths;
        if (isFixed)
            colWidths = CalculateFixedColumnWidths(rows, availableWidth, colCount);
        else
            colWidths = CalculateAutoColumnWidths(rows, availableWidth, colCount, collapse);

        float totalColWidth = colWidths.Sum() + (colCount > 1 ? borderSpacing * (colCount - 1) : 0);

        float tableWidth = Math.Min(availableWidth, totalColWidth);

        if (captionElement?.LayoutBox is { } capBox)
        {
            float left = capBox.BorderBox.Left;
            // Position caption inside the table's content box, above the rows.
            // The table's border is suppressed in collapse mode (cells form the outer grid),
            // so the caption appears visually above the bordered grid with no border around it.
            // The table's margin-top provides spacing from content above the table.
            capBox.MarginBox = new SKRect(left, capBox.MarginBox.Top, left + tableWidth, capBox.MarginBox.Bottom);
            capBox.BorderBox = new SKRect(left, capBox.BorderBox.Top, left + tableWidth, capBox.BorderBox.Bottom);
            capBox.PaddingBox = new SKRect(left, capBox.PaddingBox.Top, left + tableWidth, capBox.PaddingBox.Bottom);
            capBox.ContentBox = new SKRect(left, capBox.ContentBox.Top, left + tableWidth, capBox.ContentBox.Bottom);
        }

        float currentY = box.ContentBox.Top + captionHeight;

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

                var cellBox = CreateCellBox(cell, currentX, currentY, cellWidth, collapse);
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

        float contentLeft = box.ContentBox.Left;
        float padLeft = style?.PaddingLeft.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float padRight = style?.PaddingRight.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float padTop = style?.PaddingTop.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float padBottom = style?.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float borderLeft = collapse ? 0 : (style?.BorderLeftWidth ?? 0);
        float borderRight = collapse ? 0 : (style?.BorderRightWidth ?? 0);
        float borderTop = collapse ? 0 : (style?.BorderTopWidth ?? 0);
        float borderBottom = collapse ? 0 : (style?.BorderBottomWidth ?? 0);
        float marginLeft = style?.MarginLeft.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float marginRight = style?.MarginRight.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float marginTop = style?.MarginTop.ToPixels(style.FontSize, 16, 0, 0) ?? 0;
        float marginBottom = style?.MarginBottom.ToPixels(style.FontSize, 16, 0, 0) ?? 0;

        box.ContentBox = new SKRect(contentLeft, box.ContentBox.Top,
            contentLeft + tableWidth, currentY);
        box.PaddingBox = new SKRect(contentLeft - padLeft, box.ContentBox.Top - padTop,
            contentLeft + tableWidth + padRight, currentY + padBottom);
        box.BorderBox = new SKRect(box.PaddingBox.Left - borderLeft, box.PaddingBox.Top - borderTop,
            box.PaddingBox.Right + borderRight, box.PaddingBox.Bottom + borderBottom);
        box.MarginBox = new SKRect(box.BorderBox.Left - marginLeft, box.BorderBox.Top - marginTop,
            box.BorderBox.Right + marginRight, box.BorderBox.Bottom + marginBottom);
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

    private static float[] CalculateAutoColumnWidths(List<List<Element>> rows, float availableWidth, int colCount, bool collapse)
    {
        var widths = new float[colCount];
        var maxContentWidths = new float[colCount];
        var maxExtras = new float[colCount];

        foreach (var row in rows)
        {
            int colIdx = 0;
            foreach (var cell in row)
            {
                if (colIdx >= colCount) break;
                int colspan = GetColspan(cell);

                float contentWidth = MeasureCellContent(cell);
                float perColWidth = contentWidth / colspan;

                var cellStyle = cell.ComputedStyle;
                float cellExtra = 0;
                if (cellStyle != null)
                {
                    float padL = cellStyle.PaddingLeft.ToPixels(cellStyle.FontSize, 16, 0, 0);
                    float padR = cellStyle.PaddingRight.ToPixels(cellStyle.FontSize, 16, 0, 0);
                    cellExtra = padL + padR;
                    if (!collapse)
                        cellExtra += cellStyle.BorderLeftWidth + cellStyle.BorderRightWidth;
                }
                float perColExtra = cellExtra / colspan;

                for (int c = 0; c < colspan && (colIdx + c) < colCount; c++)
                {
                    maxContentWidths[colIdx + c] = Math.Max(maxContentWidths[colIdx + c], perColWidth);
                    maxExtras[colIdx + c] = Math.Max(maxExtras[colIdx + c], perColExtra);
                }

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
                widths[i] = maxContentWidths[i] + maxExtras[i];
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

    private static LayoutBox? CreateCellBox(Element cell, float x, float y, float width, bool collapse)
    {
        var style = cell.ComputedStyle;
        if (style == null) return null;

        float borderTop = collapse ? 0 : style.BorderTopWidth;
        float borderBottom = collapse ? 0 : style.BorderBottomWidth;
        float borderLeft = collapse ? 0 : style.BorderLeftWidth;
        float borderRight = collapse ? 0 : style.BorderRightWidth;
        float paddingTop = style.PaddingTop.ToPixels(style.FontSize, 16, 0, 0);
        float paddingBottom = style.PaddingBottom.ToPixels(style.FontSize, 16, 0, 0);
        float paddingLeft = style.PaddingLeft.ToPixels(style.FontSize, 16, 0, 0);
        float paddingRight = style.PaddingRight.ToPixels(style.FontSize, 16, 0, 0);

        float borderH = borderLeft + borderRight;
        float borderV = borderTop + borderBottom;
        float contentWidth = Math.Max(0, width - paddingLeft - paddingRight - borderH);
        float contentHeight = MeasureCellContentHeight(cell, contentWidth);

        float totalHeight = borderTop + paddingTop + contentHeight + paddingBottom + borderBottom;
        float totalWidth = width;

        var box = new LayoutBox();
        box.ContentBox = new SKRect(x + borderLeft + paddingLeft, y + borderTop + paddingTop,
                                    x + borderLeft + paddingLeft + contentWidth,
                                    y + borderTop + paddingTop + contentHeight);
        box.PaddingBox = new SKRect(x + borderLeft, y + borderTop,
                                    x + borderLeft + paddingLeft + contentWidth + paddingRight,
                                    y + borderTop + paddingTop + contentHeight + paddingBottom);
        box.BorderBox = new SKRect(x, y, x + totalWidth, y + totalHeight);
        box.MarginBox = new SKRect(x, y, x + totalWidth, y + totalHeight);

        cell.LayoutBox = box;
        return box;
    }
}
