using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.Grid;

public class GridLayoutAlgorithm
{
    private readonly ITextMeasurer? _textMeasurer;
    private readonly Func<Element, float, float, float, LayoutBox?, LayoutBox?> _createLayoutBox;
    private readonly LayoutEngine _engine;
    private ComputedStyle? _containerStyle;

    public GridLayoutAlgorithm(
        ITextMeasurer? textMeasurer,
        Func<Element, float, float, float, LayoutBox?, LayoutBox?> createLayoutBox,
        LayoutEngine engine)
    {
        _textMeasurer = textMeasurer;
        _createLayoutBox = createLayoutBox;
        _engine = engine;
    }

    public void Layout(Element gridContainer, LayoutBox containerBox, float availableWidth)
    {
        _containerStyle = gridContainer.ComputedStyle;
        if (_containerStyle == null) return;

        var explicitColumns = ParseTrackList("grid-template-columns", containerBox.ContentBox.Width);
        var explicitRows = ParseTrackList("grid-template-rows", containerBox.ContentBox.Height);
        var areas = ParseTemplateAreas(_containerStyle.GridTemplateAreas);

        var items = CollectAndPlaceItems(gridContainer, explicitColumns.Count, explicitRows.Count, areas);

        ExpandImplicitTracks(items, ref explicitColumns, ref explicitRows);

        ResolveTracks(explicitColumns, items, containerBox.ContentBox.Width, isColumn: true);
        ResolveTracks(explicitRows, items, containerBox.ContentBox.Height, isColumn: false);

        float rowGap = _containerStyle.RowGap.ToPixels(_containerStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
        float columnGap = _containerStyle.ColumnGap.ToPixels(_containerStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);

        PositionItems(items, explicitColumns, explicitRows, containerBox, columnGap, rowGap);
    }

    private List<string[]> ParseTemplateAreas(string? areasStr)
    {
        var areas = new List<string[]>();
        if (string.IsNullOrEmpty(areasStr)) return areas;

        var rows = areasStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var row in rows)
        {
            var trimmed = row.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(trimmed)) continue;
            areas.Add(trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return areas;
    }

    private List<GridTrack> ParseTrackList(string propertyName, float containerSize)
    {
        var tracks = new List<GridTrack>();
        var value = propertyName == "grid-template-columns"
            ? _containerStyle?.GridTemplateColumns
            : _containerStyle?.GridTemplateRows;

        if (string.IsNullOrEmpty(value) || value == "none") return tracks;

        ParseTrackListValue(value, containerSize, tracks);
        return tracks;
    }

    private void ParseTrackListValue(string value, float containerSize, List<GridTrack> tracks)
    {
        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i])) { i++; continue; }

            if (value[i] == ',') { i++; continue; }

            if (i + 6 < value.Length && value.Substring(i, 7).ToLowerInvariant() == "repeat(")
            {
                i += 7;
                int endParen = FindMatchingParen(value, i);
                if (endParen < 0) break;
                var repeatContent = value[i..endParen];
                i = endParen + 1;

                int commaIdx = repeatContent.IndexOf(',');
                if (commaIdx < 0) continue;

                var countStr = repeatContent[..commaIdx].Trim().ToLowerInvariant();
                var trackStr = repeatContent[(commaIdx + 1)..].Trim();

                int repeatCount = 0;
                bool autoFill = countStr == "auto-fill" || countStr == "auto-fit";

                if (!autoFill)
                {
                    if (!int.TryParse(countStr, out repeatCount) || repeatCount <= 0)
                        continue;
                }

                var repeatTracks = new List<GridTrack>();
                ParseTrackListValue(trackStr, containerSize, repeatTracks);
                if (repeatTracks.Count == 0) continue;

                if (autoFill)
                {
                    float totalTrackSize = 0;
                    float totalGap = _containerStyle?.ColumnGap.ToPixels(_containerStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight) ?? 0;
                    foreach (var t in repeatTracks)
                    {
                        if (t.SizeType == TrackSizeType.Fixed)
                            totalTrackSize += t.FixedSize;
                        else if (t.SizeType == TrackSizeType.Percentage)
                            totalTrackSize += t.Percentage * containerSize;
                        else
                            totalTrackSize += 100;
                    }
                    float gapTotal = totalGap * (repeatTracks.Count - 1);
                    float availableForTracks = Math.Max(0, containerSize - gapTotal);
                    int fits = totalTrackSize > 0 ? (int)(availableForTracks / totalTrackSize) : 0;
                    if (fits <= 0 && repeatTracks.Count > 0) fits = 1;
                    repeatCount = Math.Max(1, fits);
                }

                for (int r = 0; r < repeatCount; r++)
                    tracks.AddRange(repeatTracks.Select(t => t.Clone()));
                continue;
            }

            int endIdx = i;
            while (endIdx < value.Length && !char.IsWhiteSpace(value[endIdx]) && value[endIdx] != ',')
            {
                if (value[endIdx] == '(')
                {
                    endIdx = FindMatchingParen(value, endIdx + 1);
                    if (endIdx < 0) break;
                    endIdx++;
                }
                else
                {
                    endIdx++;
                }
            }
            var token = value[i..endIdx].Trim();
            i = endIdx;

            if (!string.IsNullOrEmpty(token))
            {
                if (token.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
                {
                    tracks.Add(ParseMinMax(token, containerSize));
                }
                else if (token.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase))
                {
                    tracks.Add(ParseFitContent(token, containerSize));
                }
                else
                {
                    tracks.Add(ParseTrackSize(token, containerSize));
                }
            }
        }
    }

    private static int FindMatchingParen(string s, int start)
    {
        int depth = 1;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private GridTrack ParseTrackSize(string value, float containerSize)
    {
        var track = new GridTrack();
        value = value.Trim().ToLowerInvariant();

        if (value.EndsWith("fr"))
        {
            if (float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fr))
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
            if (float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var px))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = px;
            }
        }
        else if (value.EndsWith("%"))
        {
            if (float.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                track.SizeType = TrackSizeType.Percentage;
                track.Percentage = pct / 100f;
            }
        }
        else if (value.EndsWith("em"))
        {
            if (float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var em))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = em * (_containerStyle?.FontSize ?? 16);
            }
        }
        else if (value.EndsWith("rem"))
        {
            if (float.TryParse(value[..^3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var rem))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = rem * _engine.RootFontSize;
            }
        }
        else if (value.EndsWith("vw"))
        {
            if (float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var vw))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = vw * _engine.ViewportWidth / 100f;
            }
        }
        else if (value.EndsWith("vh"))
        {
            if (float.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var vh))
            {
                track.SizeType = TrackSizeType.Fixed;
                track.FixedSize = vh * _engine.ViewportHeight / 100f;
            }
        }
        else if (value == "0")
        {
            track.SizeType = TrackSizeType.Fixed;
            track.FixedSize = 0;
        }

        return track;
    }

    private GridTrack ParseMinMax(string value, float containerSize)
    {
        var track = new GridTrack { SizeType = TrackSizeType.MinMax };
        var inner = value[7..^1];
        int commaIdx = FindMinMaxComma(inner);
        if (commaIdx > 0)
        {
            track.MinSize = ParseTrackSize(inner[..commaIdx].Trim(), containerSize);
            track.MaxSize = ParseTrackSize(inner[(commaIdx + 1)..].Trim(), containerSize);
        }
        return track;
    }

    private static int FindMinMaxComma(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    private GridTrack ParseFitContent(string value, float containerSize)
    {
        var track = new GridTrack { SizeType = TrackSizeType.MinMax };
        var inner = value[11..^1];
        track.MinSize = new GridTrack { SizeType = TrackSizeType.Auto };
        track.MaxSize = ParseTrackSize(inner.Trim(), containerSize);
        return track;
    }

    private List<GridItem> CollectAndPlaceItems(Element container, int explicitColCount, int explicitRowCount, List<string[]> areas)
    {
        var items = new List<GridItem>();
        var namedAreas = new Dictionary<string, (int col, int row, int colSpan, int rowSpan)>();

        if (areas.Count > 0)
        {
            BuildNamedAreaMap(areas, namedAreas);
        }

        int autoCursorCol = 0;
        int autoCursorRow = 0;
        int maxCol = explicitColCount;
        int maxRow = explicitRowCount;

        foreach (var child in container.Children)
        {
            if (child is not Element childElement) continue;
            var childStyle = childElement.ComputedStyle;
            if (childStyle == null || childStyle.Display == DisplayType.None) continue;

            var item = new GridItem { Element = childElement };
            var style = childElement.ComputedStyle;

            bool hasExplicitPlacement = false;

            if (!string.IsNullOrEmpty(style?.GridArea))
            {
                var areaName = style.GridArea.Trim().ToLowerInvariant();
                if (namedAreas.TryGetValue(areaName, out var area))
                {
                    item.ColumnStart = area.col + 1;
                    item.ColumnEnd = area.col + area.colSpan + 1;
                    item.RowStart = area.row + 1;
                    item.RowEnd = area.row + area.rowSpan + 1;
                    hasExplicitPlacement = true;
                }
            }

            if (!hasExplicitPlacement)
            {
                var (colStart, colEnd) = ParseGridLine(style, "grid-column-start", "grid-column-end", explicitColCount);
                var (rowStart, rowEnd) = ParseGridLine(style, "grid-row-start", "grid-row-end", explicitRowCount);

                if (colStart != 0 || colEnd != 0)
                {
                    item.ColumnStart = colStart;
                    item.ColumnEnd = colEnd;
                    hasExplicitPlacement = true;
                }
                if (rowStart != 0 || rowEnd != 0)
                {
                    item.RowStart = rowStart;
                    item.RowEnd = rowEnd;
                    hasExplicitPlacement = true;
                }
            }

            if (!hasExplicitPlacement)
            {
                bool dense = _containerStyle?.GridAutoFlow == GridAutoFlowType.Dense;
                if (dense)
                {
                    PlaceItemDense(items, item, maxCol);
                }
                else
                {
                    item.ColumnStart = autoCursorCol + 1;
                    item.ColumnEnd = item.ColumnStart + 1;
                    item.RowStart = autoCursorRow + 1;
                    item.RowEnd = autoCursorRow + 2;

                    autoCursorCol++;
                    if (autoCursorCol >= Math.Max(1, maxCol))
                    {
                        autoCursorCol = 0;
                        autoCursorRow++;
                    }
                }
            }

            NormalizeSpan(item, Math.Max(1, maxCol), Math.Max(1, maxRow));
            items.Add(item);
        }

        return items;
    }

    private void BuildNamedAreaMap(List<string[]> areas, Dictionary<string, (int col, int row, int colSpan, int rowSpan)> map)
    {
        var areaBounds = new Dictionary<string, (int minCol, int maxCol, int minRow, int maxRow)>();

        for (int r = 0; r < areas.Count; r++)
        {
            var row = areas[r];
            for (int c = 0; c < row.Length; c++)
            {
                var name = row[c].ToLowerInvariant();
                if (name == "." || string.IsNullOrEmpty(name)) continue;

                if (areaBounds.TryGetValue(name, out var bounds))
                {
                    areaBounds[name] = (
                        Math.Min(bounds.minCol, c),
                        Math.Max(bounds.maxCol, c),
                        Math.Min(bounds.minRow, r),
                        Math.Max(bounds.maxRow, r)
                    );
                }
                else
                {
                    areaBounds[name] = (c, c, r, r);
                }
            }
        }

        foreach (var kv in areaBounds)
        {
            map[kv.Key] = (
                kv.Value.minCol,
                kv.Value.minRow,
                kv.Value.maxCol - kv.Value.minCol + 1,
                kv.Value.maxRow - kv.Value.minRow + 1
            );
        }
    }

    private void PlaceItemDense(List<GridItem> placedItems, GridItem item, int maxCol)
    {
        int cols = Math.Max(1, maxCol);
        var occupied = new HashSet<(int col, int row)>();

        foreach (var pi in placedItems)
        {
            for (int c = pi.ColumnStart; c < pi.ColumnEnd; c++)
                for (int r = pi.RowStart; r < pi.RowEnd; r++)
                    occupied.Add((c, r));
        }

        for (int row = 1; ; row++)
        {
            for (int col = 1; col <= cols; col++)
            {
                bool fits = true;
                for (int dc = 0; dc < 1 && fits; dc++)
                    for (int dr = 0; dr < 1 && fits; dr++)
                        if (occupied.Contains((col + dc, row + dr)))
                            fits = false;

                if (fits)
                {
                    item.ColumnStart = col;
                    item.ColumnEnd = col + 1;
                    item.RowStart = row;
                    item.RowEnd = row + 1;
                    return;
                }
            }
        }
    }

    private (int start, int end) ParseGridLine(ComputedStyle? style, string startProp, string endProp, int explicitCount)
    {
        if (style == null) return (0, 0);

        string? startVal = null;
        string? endVal = null;

        if (startProp == "grid-column-start") startVal = style.GridColumnStart;
        else if (startProp == "grid-row-start") startVal = style.GridRowStart;
        if (endProp == "grid-column-end") endVal = style.GridColumnEnd;
        else if (endProp == "grid-row-end") endVal = style.GridRowEnd;

        if (string.IsNullOrEmpty(startVal) && string.IsNullOrEmpty(endVal))
            return (0, 0);

        int start = 0;
        int startSpan = 0;
        int end = 0;
        int endSpan = 0;

        if (!string.IsNullOrEmpty(startVal))
        {
            if (startVal.Trim().ToLowerInvariant().StartsWith("span "))
            {
                int.TryParse(startVal.Trim()[5..], out startSpan);
            }
            else if (int.TryParse(startVal.Trim(), out var si))
            {
                start = si;
            }
        }

        if (!string.IsNullOrEmpty(endVal))
        {
            if (endVal.Trim().ToLowerInvariant().StartsWith("span "))
            {
                int.TryParse(endVal.Trim()[5..], out endSpan);
            }
            else if (int.TryParse(endVal.Trim(), out var ei))
            {
                end = ei;
            }
        }

        if (startSpan > 0 && end > 0)
        {
            return (end - startSpan, end);
        }
        if (endSpan > 0 && start > 0)
        {
            return (start, start + endSpan);
        }
        if (start > 0 && end > 0)
        {
            return (start, end);
        }
        if (start > 0 && end == 0)
        {
            return (start, start + 1);
        }
        if (end > 0 && start == 0)
        {
            if (endSpan > 0) return (end - endSpan, end);
            return (end - 1, end);
        }
        if (startSpan > 0)
        {
            return (1, 1 + startSpan);
        }
        if (endSpan > 0)
        {
            return (1, 1 + endSpan);
        }

        return (0, 0);
    }

    private void NormalizeSpan(GridItem item, int maxCols, int maxRows)
    {
        if (item.ColumnStart <= 0 && item.ColumnEnd <= 0)
        {
            item.ColumnStart = 1;
            item.ColumnEnd = 2;
        }

        if (item.ColumnStart <= 0)
            item.ColumnStart = 1;
        if (item.ColumnEnd <= 0)
            item.ColumnEnd = item.ColumnStart + 1;
        if (item.ColumnEnd <= item.ColumnStart)
            item.ColumnEnd = item.ColumnStart + 1;

        if (item.RowStart <= 0 && item.RowEnd <= 0)
        {
            item.RowStart = 1;
            item.RowEnd = 2;
        }
        if (item.RowStart <= 0)
            item.RowStart = 1;
        if (item.RowEnd <= 0)
            item.RowEnd = item.RowStart + 1;
        if (item.RowEnd <= item.RowStart)
            item.RowEnd = item.RowStart + 1;
    }

    private void ExpandImplicitTracks(List<GridItem> items, ref List<GridTrack> columns, ref List<GridTrack> rows)
    {
        int maxCol = columns.Count;
        int maxRow = rows.Count;

        foreach (var item in items)
        {
            if (item.ColumnEnd - 1 > maxCol)
                maxCol = item.ColumnEnd - 1;
            if (item.RowEnd - 1 > maxRow)
                maxRow = item.RowEnd - 1;
        }

        while (columns.Count < maxCol)
        {
            var auto = new GridTrack { SizeType = TrackSizeType.Auto };
            columns.Add(auto);
        }
        while (rows.Count < maxRow)
        {
            var auto = new GridTrack { SizeType = TrackSizeType.Auto };
            rows.Add(auto);
        }
    }

    private void ResolveTracks(List<GridTrack> tracks, List<GridItem> items, float containerSize, bool isColumn)
    {
        if (tracks.Count == 0) return;

        ResolveFixedTracks(tracks, containerSize);
        ResolveIntrinsicTracks(tracks, items, isColumn, containerSize);
        ResolveFractionTracks(tracks, containerSize);
    }

    private void ResolveFixedTracks(List<GridTrack> tracks, float containerSize)
    {
        foreach (var track in tracks)
        {
            switch (track.SizeType)
            {
                case TrackSizeType.Fixed:
                    track.ResolvedSize = track.FixedSize;
                    break;
                case TrackSizeType.Percentage:
                    track.ResolvedSize = track.Percentage * containerSize;
                    break;
                case TrackSizeType.MinMax:
                    if (track.MinSize != null && track.MaxSize != null)
                    {
                        if (track.MinSize.SizeType == TrackSizeType.Fixed)
                            track.ResolvedSize = track.MinSize.FixedSize;
                        else if (track.MinSize.SizeType == TrackSizeType.Percentage)
                            track.ResolvedSize = track.MinSize.Percentage * containerSize;
                        else
                            track.ResolvedSize = 0;

                        float max;
                        if (track.MaxSize.SizeType == TrackSizeType.Fixed)
                            max = track.MaxSize.FixedSize;
                        else if (track.MaxSize.SizeType == TrackSizeType.Percentage)
                            max = track.MaxSize.Percentage * containerSize;
                        else if (track.MaxSize.SizeType == TrackSizeType.Fraction)
                            max = float.MaxValue;
                        else
                            max = float.MaxValue;

                        track.ResolvedSize = Math.Min(track.ResolvedSize, max);
                    }
                    break;
            }
        }
    }

    private void ResolveIntrinsicTracks(List<GridTrack> tracks, List<GridItem> items, bool isColumn, float containerSize)
    {
        float usedSize = 0;
        int autoCount = 0;

        foreach (var track in tracks)
        {
            if (track.SizeType == TrackSizeType.Auto ||
                track.SizeType == TrackSizeType.MinContent ||
                track.SizeType == TrackSizeType.MaxContent ||
                (track.SizeType == TrackSizeType.MinMax && track.ResolvedSize <= 0))
            {
                autoCount++;
            }
            else
            {
                usedSize += track.ResolvedSize;
            }
        }

        if (autoCount > 0)
        {
            foreach (var track in tracks)
            {
                if (track.SizeType == TrackSizeType.Auto ||
                    track.SizeType == TrackSizeType.MinContent ||
                    track.SizeType == TrackSizeType.MaxContent ||
                    track.SizeType == TrackSizeType.MinMax)
                {
                    float maxContent = 0;

                    foreach (var item in items)
                    {
                        int trackStart = isColumn ? item.ColumnStart - 1 : item.RowStart - 1;
                        int trackEnd = isColumn ? item.ColumnEnd - 1 : item.RowEnd - 1;

                        if (trackStart <= tracks.IndexOf(track) && tracks.IndexOf(track) < trackEnd)
                        {
                            var childStyle = item.Element.ComputedStyle;
                            if (childStyle != null)
                            {
                                float intrinsicSize = 0;
                                if (isColumn)
                                {
                                    if (childStyle.Width is PixelLength pw)
                                        intrinsicSize = pw.Value / (trackEnd - trackStart);
                                    else
                                        intrinsicSize = MeasureIntrinsicContentWidth(item.Element);
                                }
                                else
                                {
                                    if (childStyle.Height is PixelLength ph)
                                        intrinsicSize = ph.Value / (trackEnd - trackStart);
                                    else
                                        intrinsicSize = MeasureIntrinsicContentHeight(item.Element);
                                }
                                if (intrinsicSize > maxContent)
                                    maxContent = intrinsicSize;
                            }
                        }
                    }

                    if (maxContent > 0)
                        track.ResolvedSize = maxContent;
                }
            }

            float remainingSize = 0;
            foreach (var track in tracks)
            {
                if (track.SizeType != TrackSizeType.Auto &&
                    track.SizeType != TrackSizeType.MinContent &&
                    track.SizeType != TrackSizeType.MaxContent &&
                    track.SizeType != TrackSizeType.MinMax)
                {
                    remainingSize += track.ResolvedSize;
                }
            }

            float remaining = Math.Max(0, containerSize - remainingSize);
            int unresolvedAuto = 0;
            foreach (var track in tracks)
            {
                if (track.ResolvedSize <= 0 &&
                    (track.SizeType == TrackSizeType.Auto ||
                     track.SizeType == TrackSizeType.MinContent ||
                     track.SizeType == TrackSizeType.MaxContent ||
                     track.SizeType == TrackSizeType.MinMax))
                {
                    unresolvedAuto++;
                }
            }

            if (unresolvedAuto > 0)
            {
                float autoSize = remaining / unresolvedAuto;
                foreach (var track in tracks)
                {
                    if (track.ResolvedSize <= 0 &&
                        (track.SizeType == TrackSizeType.Auto ||
                         track.SizeType == TrackSizeType.MinContent ||
                         track.SizeType == TrackSizeType.MaxContent ||
                         track.SizeType == TrackSizeType.MinMax))
                    {
                        track.ResolvedSize = Math.Max(track.ResolvedSize, autoSize);
                    }
                }
            }
        }
    }

    private float MeasureIntrinsicContentWidth(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) return 0;

        float totalTextWidth = 0;
        CollectTextWidth(element, style, ref totalTextWidth);

        if (totalTextWidth > 0)
        {
            float padLeft = style.PaddingLeft.ToPixels(style.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float padRight = style.PaddingRight.ToPixels(style.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            return totalTextWidth + padLeft + padRight + style.BorderLeftWidth + style.BorderRightWidth;
        }

        return 50;
    }

    private float MeasureIntrinsicContentHeight(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) return 20;

        float totalTextHeight = style.FontSize * (style.LineHeight > 0 ? style.LineHeight : 1.2f);
        float padTop = style.PaddingTop.ToPixels(style.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
        float padBottom = style.PaddingBottom.ToPixels(style.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
        return totalTextHeight + padTop + padBottom + style.BorderTopWidth + style.BorderBottomWidth;
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

    private float MeasureTextWidth(string text, float fontSize, string? fontFamily)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (_textMeasurer != null)
            return _textMeasurer.MeasureText(text, fontFamily ?? "Arial", fontSize);
        float avgCharWidth = fontSize * 0.45f;
        int asciiCount = text.Count(c => c < 128);
        int nonAscii = text.Length - asciiCount;
        return asciiCount * avgCharWidth + nonAscii * (fontSize * 0.7f);
    }

    private void ResolveFractionTracks(List<GridTrack> tracks, float containerSize)
    {
        float fixedSize = 0;
        float totalFraction = 0;

        foreach (var track in tracks)
        {
            if (track.SizeType == TrackSizeType.Fraction)
                totalFraction += track.Fraction;
            else
            {
                if (track.ResolvedSize > 0)
                    fixedSize += track.ResolvedSize;
                else if (track.SizeType == TrackSizeType.MinMax && track.MaxSize?.SizeType == TrackSizeType.Fraction)
                    totalFraction += Math.Max(1, track.MaxSize.Fraction);
            }
        }

        if (totalFraction > 0)
        {
            float columnGap = _containerStyle?.ColumnGap.ToPixels(_containerStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight) ?? 0;
            float rowGap = _containerStyle?.RowGap.ToPixels(_containerStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight) ?? 0;
            bool isColumn = true;

            float gapTotal = isColumn ? columnGap * (tracks.Count - 1) : rowGap * (tracks.Count - 1);
            float remaining = Math.Max(0, containerSize - fixedSize - gapTotal);

            foreach (var track in tracks)
            {
                if (track.SizeType == TrackSizeType.Fraction)
                {
                    track.ResolvedSize = (track.Fraction / totalFraction) * remaining;
                }
                else if (track.SizeType == TrackSizeType.MinMax && track.MaxSize?.SizeType == TrackSizeType.Fraction)
                {
                    float fr = Math.Max(1, track.MaxSize.Fraction);
                    track.ResolvedSize = (fr / totalFraction) * remaining;
                }
            }
        }
    }

    private void PositionItems(List<GridItem> items, List<GridTrack> columns, List<GridTrack> rows,
        LayoutBox containerBox, float columnGap, float rowGap)
    {
        if (columns.Count == 0 || rows.Count == 0) return;

        var columnPositions = ComputePositionArray(columns, containerBox.ContentBox.Left, columnGap);
        var rowPositions = ComputePositionArray(rows, containerBox.ContentBox.Top, rowGap);

        var containerStyle = _containerStyle;
        var justifyItems = ParseJustifyItems(containerStyle?.JustifyItems ?? "stretch");
        var alignItems = containerStyle?.AlignItems ?? Dom.AlignItemsType.Stretch;
        var justifyContent = ParseContentDistribution(containerStyle?.JustifyContent.ToString().ToLowerInvariant() ?? "normal");
        var alignContent = ParseContentDistribution(containerStyle?.AlignContent ?? "normal");

        if (justifyContent != ContentDistributionType.Normal)
            ApplyContentAlignment(columnPositions, columns, containerBox.ContentBox.Width, columnGap, justifyContent);
        if (alignContent != ContentDistributionType.Normal)
            ApplyContentAlignment(rowPositions, rows, containerBox.ContentBox.Height, rowGap, alignContent);

        foreach (var item in items)
        {
            int colStart = Math.Clamp(item.ColumnStart - 1, 0, columns.Count - 1);
            int colEnd = Math.Clamp(item.ColumnEnd - 1, 0, columns.Count - 1);
            int rowStart = Math.Clamp(item.RowStart - 1, 0, rows.Count - 1);
            int rowEnd = Math.Clamp(item.RowEnd - 1, 0, rows.Count - 1);

            if (colEnd < colStart) colEnd = colStart;
            if (rowEnd < rowStart) rowEnd = rowStart;

            float cellX = columnPositions[colStart];
            float cellY = rowPositions[rowStart];
            float cellW = columnPositions[colEnd + 1] - columnPositions[colStart];
            float cellH = rowPositions[rowEnd + 1] - rowPositions[rowStart];

            var childStyle = item.Element.ComputedStyle;
            if (childStyle == null) continue;

            float marginTop = childStyle.MarginTop.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float marginBottom = childStyle.MarginBottom.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float marginLeft = childStyle.MarginLeft.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float marginRight = childStyle.MarginRight.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);

            float borderTop = childStyle.BorderTopWidth;
            float borderBottom = childStyle.BorderBottomWidth;
            float borderLeft = childStyle.BorderLeftWidth;
            float borderRight = childStyle.BorderRightWidth;

            float paddingTop = childStyle.PaddingTop.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float paddingBottom = childStyle.PaddingBottom.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float paddingLeft = childStyle.PaddingLeft.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);
            float paddingRight = childStyle.PaddingRight.ToPixels(childStyle.FontSize, _engine.RootFontSize, _engine.ViewportWidth, _engine.ViewportHeight);

            var alignSelf = ParseGridAlignSelf(childStyle.AlignSelf, alignItems);
            var justifySelf = ParseJustifySelf(childStyle.JustifySelf, justifyItems);

            float availContentW = Math.Max(0, cellW - marginLeft - marginRight - borderLeft - borderRight - paddingLeft - paddingRight);
            float availContentH = Math.Max(0, cellH - marginTop - marginBottom - borderTop - borderBottom - paddingTop - paddingBottom);

            float contentW = availContentW;
            float contentH = availContentH;

            if (childStyle.Width is PixelLength pw)
                contentW = Math.Max(0, pw.Value - borderLeft - borderRight - paddingLeft - paddingRight);
            if (childStyle.Height is PixelLength ph)
                contentH = Math.Max(0, ph.Value - borderTop - borderBottom - paddingTop - paddingBottom);
            if (childStyle.Width is PercentLength pwp)
                contentW = Math.Max(0, pwp.Value * cellW - borderLeft - borderRight - paddingLeft - paddingRight);

            if (justifySelf != JustifySelfType.Stretch && childStyle.Width is AutoLength)
            {
                float intrinsic = MeasureIntrinsicContentWidth(item.Element);
                contentW = Math.Min(intrinsic, availContentW);
            }

            if (alignSelf != UpBrowser.Core.Dom.AlignSelfType.Stretch && childStyle.Height is AutoLength)
            {
                float intrinsic = MeasureIntrinsicContentHeight(item.Element);
                contentH = Math.Min(intrinsic, availContentH);
            }

            contentW = Math.Max(0, contentW);
            contentH = Math.Max(0, contentH);

            float itemX = cellX + marginLeft + borderLeft + paddingLeft;
            float itemY = cellY + marginTop + borderTop + paddingTop;

            switch (justifySelf)
            {
                case JustifySelfType.Start:
                    break;
                case JustifySelfType.End:
                    itemX = cellX + cellW - marginRight - borderRight - paddingRight - contentW;
                    break;
                case JustifySelfType.Center:
                    itemX = cellX + marginLeft + borderLeft + paddingLeft + (availContentW - contentW) / 2;
                    break;
                case JustifySelfType.Stretch:
                    contentW = availContentW;
                    break;
            }

            switch (alignSelf)
            {
                case UpBrowser.Core.Dom.AlignSelfType.FlexStart:
                    break;
                case UpBrowser.Core.Dom.AlignSelfType.FlexEnd:
                    itemY = cellY + cellH - marginBottom - borderBottom - paddingBottom - contentH;
                    break;
                case UpBrowser.Core.Dom.AlignSelfType.Center:
                    itemY = cellY + marginTop + borderTop + paddingTop + (availContentH - contentH) / 2;
                    break;
                case UpBrowser.Core.Dom.AlignSelfType.Stretch:
                    contentH = availContentH;
                    break;
            }

            contentW = Math.Max(0, contentW);
            contentH = Math.Max(0, contentH);

            float marginBoxX = cellX + marginLeft;
            float marginBoxY = cellY + marginTop;
            float marginBoxW = cellW - marginLeft - marginRight;
            float marginBoxH = cellH - marginTop - marginBottom;

            _createLayoutBox(item.Element, itemX, itemY, contentW, containerBox);

            var finalBox = item.Element.LayoutBox;
            if (finalBox != null)
            {
                finalBox.MarginBox = new SKRect(marginBoxX, marginBoxY, marginBoxX + marginBoxW, marginBoxY + marginBoxH);
                finalBox.BorderBox = new SKRect(
                    marginBoxX + marginLeft,
                    marginBoxY + marginTop,
                    marginBoxX + marginBoxW - marginRight,
                    marginBoxY + marginBoxH - marginBottom);
                finalBox.PaddingBox = new SKRect(
                    marginBoxX + marginLeft + borderLeft,
                    marginBoxY + marginTop + borderTop,
                    marginBoxX + marginBoxW - marginRight - borderRight,
                    marginBoxY + marginBoxH - marginBottom - borderBottom);
                finalBox.ContentBox = new SKRect(
                    itemX,
                    itemY,
                    itemX + contentW,
                    itemY + contentH);
                containerBox.Children.Add(finalBox);
                finalBox.Parent = containerBox;
            }
        }
    }

    private static float[] ComputePositionArray(List<GridTrack> tracks, float origin, float gap)
    {
        var positions = new float[tracks.Count + 1];
        positions[0] = origin;
        for (int i = 0; i < tracks.Count; i++)
            positions[i + 1] = positions[i] + tracks[i].ResolvedSize + (i < tracks.Count - 1 ? gap : 0);
        return positions;
    }

    private static void ApplyContentAlignment(float[] positions, List<GridTrack> tracks, float containerSize, float gap, ContentDistributionType distribution)
    {
        if (tracks.Count == 0) return;

        float totalTrackSize = 0;
        for (int i = 0; i < tracks.Count; i++)
            totalTrackSize += tracks[i].ResolvedSize;

        float totalGap = gap * (tracks.Count - 1);
        float usedSize = totalTrackSize + totalGap;
        float remaining = Math.Max(0, containerSize - usedSize);

        if (remaining <= 0) return;

        float offset = 0;
        switch (distribution)
        {
            case ContentDistributionType.Center:
                offset = remaining / 2;
                break;
            case ContentDistributionType.End:
            case ContentDistributionType.FlexEnd:
                offset = remaining;
                break;
            case ContentDistributionType.SpaceBetween:
                if (tracks.Count > 1)
                {
                    float extraGap = remaining / (tracks.Count - 1);
                    for (int i = 1; i < positions.Length; i++)
                        positions[i] += extraGap * i;
                }
                return;
            case ContentDistributionType.SpaceAround:
                if (tracks.Count > 0)
                {
                    float extraGap = remaining / tracks.Count;
                    for (int i = 0; i < positions.Length; i++)
                        positions[i] += extraGap * i + extraGap / 2;
                }
                return;
            case ContentDistributionType.SpaceEvenly:
                if (tracks.Count > 0)
                {
                    float extraGap = remaining / (tracks.Count + 1);
                    for (int i = 0; i < positions.Length; i++)
                        positions[i] += extraGap * (i + 1);
                }
                return;
        }

        if (offset > 0)
        {
            for (int i = 0; i < positions.Length; i++)
                positions[i] += offset;
        }
    }

    private static JustifyItemsType ParseJustifyItems(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "start" => JustifyItemsType.Start,
            "end" => JustifyItemsType.End,
            "center" => JustifyItemsType.Center,
            "stretch" => JustifyItemsType.Stretch,
            _ => JustifyItemsType.Stretch
        };
    }

    private static AlignItemsType ParseAlignItems(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "start" or "flex-start" => AlignItemsType.FlexStart,
            "end" or "flex-end" => AlignItemsType.FlexEnd,
            "center" => AlignItemsType.Center,
            "stretch" => AlignItemsType.Stretch,
            "baseline" => AlignItemsType.Baseline,
            _ => AlignItemsType.Stretch
        };
    }

    private static JustifySelfType ParseJustifySelf(string value, JustifyItemsType fallback)
    {
        if (string.IsNullOrEmpty(value) || value == "auto")
        {
            return fallback switch
            {
                JustifyItemsType.Start => JustifySelfType.Start,
                JustifyItemsType.End => JustifySelfType.End,
                JustifyItemsType.Center => JustifySelfType.Center,
                _ => JustifySelfType.Stretch
            };
        }
        return value.ToLowerInvariant() switch
        {
            "start" => JustifySelfType.Start,
            "end" => JustifySelfType.End,
            "center" => JustifySelfType.Center,
            "stretch" => JustifySelfType.Stretch,
            _ => JustifySelfType.Stretch
        };
    }

    private static UpBrowser.Core.Dom.AlignSelfType ParseGridAlignSelf(UpBrowser.Core.Dom.AlignSelfType alignSelf, UpBrowser.Core.Dom.AlignItemsType fallback)
    {
        if (alignSelf == UpBrowser.Core.Dom.AlignSelfType.Auto)
        {
            return fallback switch
            {
                UpBrowser.Core.Dom.AlignItemsType.FlexStart => UpBrowser.Core.Dom.AlignSelfType.FlexStart,
                UpBrowser.Core.Dom.AlignItemsType.FlexEnd => UpBrowser.Core.Dom.AlignSelfType.FlexEnd,
                UpBrowser.Core.Dom.AlignItemsType.Center => UpBrowser.Core.Dom.AlignSelfType.Center,
                UpBrowser.Core.Dom.AlignItemsType.Baseline => UpBrowser.Core.Dom.AlignSelfType.Baseline,
                _ => UpBrowser.Core.Dom.AlignSelfType.Stretch
            };
        }
        return alignSelf;
    }

    private static ContentDistributionType ParseContentDistribution(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "normal") return ContentDistributionType.Normal;
        return value.ToLowerInvariant() switch
        {
            "start" or "flex-start" => ContentDistributionType.Start,
            "end" or "flex-end" => ContentDistributionType.End,
            "center" => ContentDistributionType.Center,
            "stretch" => ContentDistributionType.Stretch,
            "space-between" => ContentDistributionType.SpaceBetween,
            "space-around" => ContentDistributionType.SpaceAround,
            "space-evenly" => ContentDistributionType.SpaceEvenly,
            _ => ContentDistributionType.Normal
        };
    }

    private static void RoundLayoutBox(LayoutBox box, float dpiScale)
    {
        if (dpiScale <= 0) dpiScale = 1.0f;
        box.MarginBox = LayoutMath.RoundRect(box.MarginBox, dpiScale);
        box.BorderBox = LayoutMath.RoundRect(box.BorderBox, dpiScale);
        box.PaddingBox = LayoutMath.RoundRect(box.PaddingBox, dpiScale);
        box.ContentBox = LayoutMath.RoundRect(box.ContentBox, dpiScale);
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
    public float ResolvedSize { get; set; }
    public GridTrack? MinSize { get; set; }
    public GridTrack? MaxSize { get; set; }
    public float? MinSizeValue => MinSize?.ResolvedSize > 0 ? MinSize.ResolvedSize : null;
    public float? MaxSizeValue => MaxSize?.ResolvedSize > 0 ? MaxSize.ResolvedSize : null;

    public GridTrack Clone()
    {
        return new GridTrack
        {
            SizeType = SizeType,
            FixedSize = FixedSize,
            Fraction = Fraction,
            Percentage = Percentage,
            ResolvedSize = ResolvedSize,
            MinSize = MinSize?.Clone(),
            MaxSize = MaxSize?.Clone()
        };
    }
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

public enum JustifyItemsType { Start, End, Center, Stretch }

public enum JustifySelfType { Start, End, Center, Stretch }

public enum ContentDistributionType { Normal, Start, End, Center, Stretch, SpaceBetween, SpaceAround, SpaceEvenly, FlexStart, FlexEnd }
