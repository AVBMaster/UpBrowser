using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Rendering;

/// <summary>
/// Paints table-specific decorations. Mirrors table_painters.cc.
/// Table backgrounds paint on the grid rect (union of all cells), and
/// table part backgrounds (table, section, row, column) are propagated
/// into cells so they show through cell gaps.
/// </summary>
internal static class TablePainter
{
    public static bool IsTable(Element element) =>
        element.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase);

    public static bool IsTableSection(Element element) =>
        element.TagName is "TBODY" or "THEAD" or "TFOOT";

    public static bool IsTableRow(Element element) =>
        element.TagName.Equals("TR", StringComparison.OrdinalIgnoreCase);

    public static bool IsTableCell(Element element) =>
        element.TagName is "TD" or "TH";

    public static void PaintTableBackground(DisplayList displayList, Element element, ComputedStyle style, LayoutBox box, float contentOffsetY)
    {
        if (!IsTable(element)) return;

        bool hasBgColor = style.BackgroundColor.HasValue && style.BackgroundColor.Value.Alpha > 0;
        bool hasBgImage = style.BackgroundImage is { Count: > 0 } && style.BackgroundImage!.Any(s => s != "none");
        if (!hasBgColor && !hasBgImage) return;

        // Paint the background on the grid rect (union of all cells), not the
        // border rect, so the table background is visible through cell gaps.
        var gridRect = ComputeGridRect(element, box, contentOffsetY);
        if (gridRect.Width <= 0 || gridRect.Height <= 0) return;

        if (hasBgColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = gridRect;
            op.FillColor = style.BackgroundColor.Value;
            op.Bounds = gridRect;
            displayList.Add(op);
        }

        if (hasBgImage && style.BackgroundImage!.Any(s => s.Contains("gradient", StringComparison.OrdinalIgnoreCase)))
        {
            var shader = GradientRenderer.CreateGradient(style.BackgroundImage!.FirstOrDefault() ?? "", gridRect);
            if (shader != null)
            {
                var path = new SKPath();
                path.AddRect(gridRect);
                var op = PaintOpPool.GetDrawPathOp();
                op.Path = path;
                op.FillPaint = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader, IsAntialias = true };
                op.Bounds = gridRect;
                displayList.Add(op);
            }
        }
    }

    /// <summary>
    /// Paints table part backgrounds (table, section, row) into cells.
    /// The table part's background is painted behind each cell so it shows
    /// through cell padding gaps.
    /// </summary>
    public static void PaintTablePartBackgroundIntoCell(DisplayList displayList, Element tablePart, ComputedStyle partStyle, SKRect partRect, Element cell, SKRect cellRect)
    {
        if (partStyle.Visibility != VisibilityType.Visible) return;

        bool hasBgColor = partStyle.BackgroundColor.HasValue && partStyle.BackgroundColor.Value.Alpha > 0;
        bool hasBgImage = partStyle.BackgroundImage is { Count: > 0 } && partStyle.BackgroundImage!.Any(s => s != "none");
        if (!hasBgColor && !hasBgImage) return;

        // Clip the part background to the cell rect so it only shows through
        // this cell.
        if (hasBgColor)
        {
            var op = PaintOpPool.GetDrawRectOp();
            op.Rect = cellRect;
            op.FillColor = partStyle.BackgroundColor.Value;
            op.Bounds = cellRect;
            displayList.Add(op);
        }
    }

    private static SKRect ComputeGridRect(Element table, LayoutBox box, float contentOffsetY)
    {
        float left = float.MaxValue, top = float.MaxValue;
        float right = float.MinValue, bottom = float.MinValue;
        bool foundCell = false;

        foreach (var section in table.Children)
        {
            if (section is not Element sectionEl || !IsTableSection(sectionEl)) continue;
            foreach (var row in sectionEl.Children)
            {
                if (row is not Element rowEl || !IsTableRow(rowEl)) continue;
                foreach (var cell in rowEl.Children)
                {
                    if (cell is not Element cellEl || !IsTableCell(cellEl)) continue;
                    var cellBox = cellEl.LayoutBox;
                    if (cellBox == null) continue;

                    left = Math.Min(left, cellBox.BorderBox.Left);
                    top = Math.Min(top, cellBox.BorderBox.Top);
                    right = Math.Max(right, cellBox.BorderBox.Right);
                    bottom = Math.Max(bottom, cellBox.BorderBox.Bottom);
                    foundCell = true;
                }
            }
        }

        if (!foundCell)
            return new SKRect(box.BorderBox.Left, box.BorderBox.Top + contentOffsetY,
                box.BorderBox.Right, box.BorderBox.Bottom + contentOffsetY);

        return new SKRect(left, top + contentOffsetY, right, bottom + contentOffsetY);
    }
}