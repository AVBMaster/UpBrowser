using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.Grid;

/// <summary>
/// CSS Grid Layout Algorithm - implements the CSS Grid Layout specification.
/// Inspired by Blink's LayoutNG Grid architecture.
/// </summary>
public class GridLayoutAlgorithm
{
    private readonly ITextMeasurer? _textMeasurer;

    public GridLayoutAlgorithm(ITextMeasurer? textMeasurer = null)
    {
        _textMeasurer = textMeasurer;
    }

    public void Layout(Element gridContainer, LayoutBox containerBox, float availableWidth)
    {
        var style = gridContainer.ComputedStyle;
        if (style == null) return;

        var grid = ParseGridTemplate(style, containerBox.ContentBox.Width, containerBox.ContentBox.Height);
        if (grid.Columns.Count == 0 || grid.Rows.Count == 0) return;

        var items = PlaceGridItems(gridContainer, grid);
        TrackSizes(grid, items, containerBox.ContentBox.Width, containerBox.ContentBox.Height);
        PositionItems(grid, items, containerBox);
    }

    private GridDefinition ParseGridTemplate(ComputedStyle style, float containerWidth, float containerHeight)
    {
        var grid = new GridDefinition();

        var templateColumns = "grid-template-columns";
        var templateRows = "grid-template-rows";

        grid.Columns = ParseTrackList(style, templateColumns, containerWidth);
        grid.Rows = ParseTrackList(style, templateRows, containerHeight);

        return grid;
    }

    private List<GridTrack> ParseTrackList(ComputedStyle style, string propertyName, float containerSize)
    {
        var tracks = new List<GridTrack>();

        var value = GetStyleProperty(style, propertyName);
        if (string.IsNullOrEmpty(value) || value == "none") return tracks;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            tracks.Add(ParseTrackSize(part, containerSize));
        }

        return tracks;
    }

    private GridTrack ParseTrackSize(string value, float containerSize)
    {
        var track = new GridTrack();

        if (value.EndsWith("fr"))
        {
            if (float.TryParse(value[..^2], out var fr))
            {
                track.SizeType = TrackSizeType.Fraction;
                track.Fraction = fr;
            }
        }
        else if (value == "auto")
        {
            track.SizeType = TrackSizeType.Auto;
        }
        else if (value == "min-content")
        {
            track.SizeType = TrackSizeType.MinContent;
        }
        else if (value == "max-content")
        {
            track.SizeType = TrackSizeType.MaxContent;
        }
        else if (value.EndsWith("px"))
        {
            track.SizeType = TrackSizeType.Fixed;
            if (float.TryParse(value[..^2], out var px))
                track.FixedSize = px;
        }
        else if (value.EndsWith("%"))
        {
            track.SizeType = TrackSizeType.Percentage;
            if (float.TryParse(value[..^2], out var pct))
                track.Percentage = pct / 100f;
        }
        else if (value.StartsWith("minmax("))
        {
            track.SizeType = TrackSizeType.MinMax;
            var inner = value[7..^1];
            var commaIdx = inner.IndexOf(',');
            if (commaIdx > 0)
            {
                track.MinSize = ParseTrackSize(inner[..commaIdx].Trim(), 0);
                track.MaxSize = ParseTrackSize(inner[(commaIdx + 1)..].Trim(), 0);
            }
        }

        return track;
    }

    private List<GridItem> PlaceGridItems(Element container, GridDefinition grid)
    {
        var items = new List<GridItem>();
        int autoRow = 0;
        int autoCol = 0;

        foreach (var child in container.Children)
        {
            if (child is not Element childElement) continue;
            var childStyle = childElement.ComputedStyle;
            if (childStyle == null || childStyle.Display == DisplayType.None) continue;

            var item = new GridItem { Element = childElement };

            item.ColumnStart = ParseGridLine(childStyle, "grid-column-start");
            item.ColumnEnd = ParseGridLine(childStyle, "grid-column-end");
            item.RowStart = ParseGridLine(childStyle, "grid-row-start");
            item.RowEnd = ParseGridLine(childStyle, "grid-row-end");

            if (item.ColumnStart <= 0 && item.ColumnEnd <= 0)
            {
                item.ColumnStart = autoCol + 1;
                item.ColumnEnd = item.ColumnStart + 1;
                autoCol++;
            }

            if (item.RowStart <= 0 && item.RowEnd <= 0)
            {
                item.RowStart = autoRow + 1;
                item.RowEnd = item.RowStart + 1;
                autoRow++;
            }

            items.Add(item);
        }

        return items;
    }

    private int ParseGridLine(ComputedStyle style, string propertyName)
    {
        var value = GetStyleProperty(style, propertyName);
        if (string.IsNullOrEmpty(value)) return 0;

        if (int.TryParse(value, out var line))
            return line;

        return 0;
    }

    private void TrackSizes(GridDefinition grid, List<GridItem> items, float containerWidth, float containerHeight)
    {
        ResolveFixedTracks(grid.Columns, containerWidth);
        ResolveFixedTracks(grid.Rows, containerHeight);

        ResolveFractionTracks(grid.Columns, containerWidth);
        ResolveFractionTracks(grid.Rows, containerHeight);
    }

    private void ResolveFixedTracks(List<GridTrack> tracks, float containerSize)
    {
        float usedSize = 0;
        int autoCount = 0;

        foreach (var track in tracks)
        {
            switch (track.SizeType)
            {
                case TrackSizeType.Fixed:
                    usedSize += track.FixedSize;
                    break;
                case TrackSizeType.Percentage:
                    track.FixedSize = track.Percentage * containerSize;
                    usedSize += track.FixedSize;
                    break;
                case TrackSizeType.Auto:
                    autoCount++;
                    break;
            }
        }

        if (autoCount > 0)
        {
            float remaining = Math.Max(0, containerSize - usedSize);
            float autoSize = remaining / autoCount;
            foreach (var track in tracks)
            {
                if (track.SizeType == TrackSizeType.Auto)
                    track.FixedSize = autoSize;
            }
        }
    }

    private void ResolveFractionTracks(List<GridTrack> tracks, float containerSize)
    {
        float fixedSize = 0;
        float totalFraction = 0;

        foreach (var track in tracks)
        {
            if (track.SizeType == TrackSizeType.Fraction)
                totalFraction += track.Fraction;
            else if (track.SizeType != TrackSizeType.Fraction)
                fixedSize += track.FixedSize;
        }

        if (totalFraction > 0)
        {
            float remaining = Math.Max(0, containerSize - fixedSize);
            foreach (var track in tracks)
            {
                if (track.SizeType == TrackSizeType.Fraction)
                    track.FixedSize = (track.Fraction / totalFraction) * remaining;
            }
        }
    }

    private void PositionItems(GridDefinition grid, List<GridItem> items, LayoutBox containerBox)
    {
        var columnPositions = new float[grid.Columns.Count + 1];
        var rowPositions = new float[grid.Rows.Count + 1];

        columnPositions[0] = containerBox.ContentBox.Left;
        for (int i = 0; i < grid.Columns.Count; i++)
            columnPositions[i + 1] = columnPositions[i] + grid.Columns[i].FixedSize;

        rowPositions[0] = containerBox.ContentBox.Top;
        for (int i = 0; i < grid.Rows.Count; i++)
            rowPositions[i + 1] = rowPositions[i] + grid.Rows[i].FixedSize;

        foreach (var item in items)
        {
            int colStart = Math.Max(0, item.ColumnStart - 1);
            int colEnd = Math.Min(grid.Columns.Count, item.ColumnEnd - 1);
            int rowStart = Math.Max(0, item.RowStart - 1);
            int rowEnd = Math.Min(grid.Rows.Count, item.RowEnd - 1);

            float x = columnPositions[colStart];
            float y = rowPositions[rowStart];
            float width = columnPositions[colEnd] - columnPositions[colStart];
            float height = rowPositions[rowEnd] - rowPositions[rowStart];

            var itemBox = item.Element.LayoutBox;
            if (itemBox != null)
            {
                itemBox.MarginBox = new SKRect(x, y, x + width, y + height);
                itemBox.BorderBox = itemBox.MarginBox;
                itemBox.PaddingBox = itemBox.MarginBox;
                itemBox.ContentBox = itemBox.MarginBox;
            }

            containerBox.Children.Add(itemBox!);
            itemBox!.Parent = containerBox;
        }
    }

    private string? GetStyleProperty(ComputedStyle style, string propertyName)
    {
        return propertyName switch
        {
            "grid-template-columns" => null,
            "grid-template-rows" => null,
            "grid-column-start" => null,
            "grid-column-end" => null,
            "grid-row-start" => null,
            "grid-row-end" => null,
            _ => null
        };
    }
}

public class GridDefinition
{
    public List<GridTrack> Columns { get; set; } = new();
    public List<GridTrack> Rows { get; set; } = new();
    public string? Areas { get; set; }
}

public class GridTrack
{
    public TrackSizeType SizeType { get; set; }
    public float FixedSize { get; set; }
    public float Fraction { get; set; }
    public float Percentage { get; set; }
    public GridTrack? MinSize { get; set; }
    public GridTrack? MaxSize { get; set; }
}

public class GridItem
{
    public Element Element { get; set; } = null!;
    public int ColumnStart { get; set; }
    public int ColumnEnd { get; set; }
    public int RowStart { get; set; }
    public int RowEnd { get; set; }
}

public enum TrackSizeType { Fixed, Fraction, Auto, MinContent, MaxContent, Percentage, MinMax }
