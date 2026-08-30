using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Core.Layout.Grid;

/// <summary>
/// CSS Grid Layout Algorithm, mirroring the engine's grid layout algorithm.
/// Implements the full grid track sizing algorithm per CSS Grid spec:
/// 1. Init track sizes
/// 2. Resolve intrinsic track sizes (min-content, max-content, auto)
/// 3. Maximize tracks (distribute positive free space)
/// 4. Stretch auto tracks
/// 5. Expand flexible tracks (fr units)
/// Also handles item placement, alignment, and auto-placement with dense packing.
/// </summary>
public class GridLayoutAlgorithm
{
    private readonly ITextMeasurer? _textMeasurer;
    private readonly ConstraintSpace _space;
    private readonly float _rootFontSize;
    private readonly float _viewportWidth;
    private readonly float _viewportHeight;
    private ComputedStyle? _containerStyle;
    private float _containerWidth;
    private float _containerHeight;
    private bool _containerHeightAuto;

    public GridLayoutAlgorithm(
        ITextMeasurer? textMeasurer,
        in ConstraintSpace space)
    {
        _textMeasurer = textMeasurer;
        _space = space;
        _rootFontSize = space.RootFontSize;
        _viewportWidth = space.ViewportWidth;
        _viewportHeight = space.ViewportHeight;
    }

    public void Layout(Element gridContainer, LayoutBox containerBox, float availableWidth)
    {
        _containerStyle = gridContainer.ComputedStyle;
        if (_containerStyle == null) return;

        _containerWidth = containerBox.ContentBox.Width;
        _containerHeight = containerBox.ContentBox.Height;
        _containerHeightAuto = !_space.HasDefiniteBlockSize;

        float rowGap = _containerStyle.RowGap.ToPixels(_containerStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);
        float columnGap = _containerStyle.ColumnGap.ToPixels(_containerStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight);

        var explicitColumns = ParseTrackList("grid-template-columns", _containerWidth);
        var explicitRows = ParseTrackList("grid-template-rows", _containerHeight);
        var areas = ParseTemplateAreas(_containerStyle.GridTemplateAreas);

        var items = CollectAndPlaceItems(gridContainer, explicitColumns.Count, explicitRows.Count, areas, columnGap, rowGap);

        ExpandImplicitTracks(items, ref explicitColumns, ref explicitRows);

        // Track sizing algorithm
        ResolveTracks(explicitColumns, items, _containerWidth, columnGap, isColumn: true);
        ResolveTracks(explicitRows, items, _containerHeight, rowGap, isColumn: false);

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
                    if (!int.TryParse(countStr, out repeatCount) || repeatCount <= 0) continue;
                }

                var repeatTracks = new List<GridTrack>();
                ParseTrackListValue(trackStr, containerSize, repeatTracks);
                if (repeatTracks.Count == 0) continue;

                if (autoFill)
                {
                    float totalGap = _containerStyle?.ColumnGap.ToPixels(_containerStyle.FontSize, _rootFontSize, _viewportWidth, _viewportHeight) ?? 0;
                    float totalTrackSize = 0;
                    foreach (var t in repeatTracks)
                        totalTrackSize += t.BaseSize;
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
                else endIdx++;
            }
            var token = value[i..endIdx].Trim();
            i = endIdx;

            if (!string.IsNullOrEmpty(token))
            {
                if (token.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(ParseMinMax(token, containerSize));
                else if (token.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase))
                    tracks.Add(ParseFitContent(token, containerSize));
                else
                    tracks.Add(ParseTrackSize(token, containerSize));
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

        if (value.EndsWith("fr") && float.TryParse(value[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fr))
        {
            track.SizeType = TrackSizeType.Fraction; track.Fraction = fr;
        }
        else if (value == "auto") track.SizeType = TrackSizeType.Auto;
        else if (value == "min-content") track.SizeType = TrackSizeType.MinContent;
        else if (value == "max-content") track.SizeType = TrackSizeType.MaxContent;
        else if (value.EndsWith("px") && TryParseFloat(value[..^2], out var px)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = px; }
        else if (value.EndsWith("%") && TryParseFloat(value[..^1], out var pct)) { track.SizeType = TrackSizeType.Percentage; track.Percentage = pct / 100f; }
        else if (value.EndsWith("em") && TryParseFloat(value[..^2], out var em)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = em * (_containerStyle?.FontSize ?? 16); }
        else if (value.EndsWith("rem") && TryParseFloat(value[..^3], out var rem)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = rem * _rootFontSize; }
        else if (value.EndsWith("vw") && TryParseFloat(value[..^2], out var vw)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = vw * _viewportWidth / 100f; }
        else if (value.EndsWith("vh") && TryParseFloat(value[..^2], out var vh)) { track.SizeType = TrackSizeType.Fixed; track.FixedSize = vh * _viewportHeight / 100f; }
        else if (value == "0") { track.SizeType = TrackSizeType.Fixed; track.FixedSize = 0; }

        track.BaseSize = track.ResolveSize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);
        return track;
    }

    private static bool TryParseFloat(string s, out float result) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

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

    private List<GridItem> CollectAndPlaceItems(Element container, int explicitColCount, int explicitRowCount, List<string[]> areas, float columnGap, float rowGap)
    {
        var items = new List<GridItem>();
        var namedAreas = new Dictionary<string, (int col, int row, int colSpan, int rowSpan)>();

        if (areas.Count > 0) BuildNamedAreaMap(areas, namedAreas);

        bool densePacking = _containerStyle?.GridAutoFlow == GridAutoFlowType.Dense || _containerStyle?.GridAutoFlow == GridAutoFlowType.Column;

        int autoCursorCol = 0;
        int autoCursorRow = 0;
        int maxCol = explicitColCount;
        int maxRow = explicitRowCount;

        // Collect items with explicit placement first
        var explicitItems = new List<GridItem>();
        var autoItems = new List<GridItem>();

        foreach (var child in container.Children)
        {
            if (child is not Element childElement) continue;
            var childStyle = childElement.ComputedStyle;
            if (childStyle == null || childStyle.Display == DisplayType.None) continue;

            var item = new GridItem { Element = childElement };
            var style = childElement.ComputedStyle!;
            bool hasExplicit = false;

            if (!string.IsNullOrEmpty(style.GridArea))
            {
                var areaName = style.GridArea.Trim().ToLowerInvariant();
                if (namedAreas.TryGetValue(areaName, out var area))
                {
                    item.ColumnStart = area.col + 1;
                    item.ColumnEnd = area.col + area.colSpan + 1;
                    item.RowStart = area.row + 1;
                    item.RowEnd = area.row + area.rowSpan + 1;
                    hasExplicit = true;
                }
            }

            if (!hasExplicit)
            {
                var (colStart, colEnd) = ParseGridLine(style, "grid-column-start", "grid-column-end", explicitColCount);
                var (rowStart, rowEnd) = ParseGridLine(style, "grid-row-start", "grid-row-end", explicitRowCount);
                if (colStart != 0 || colEnd != 0) { item.ColumnStart = colStart; item.ColumnEnd = colEnd; hasExplicit = true; }
                if (rowStart != 0 || rowEnd != 0) { item.RowStart = rowStart; item.RowEnd = rowEnd; hasExplicit = true; }
            }

            if (!hasExplicit)
            {
                item.ColumnStart = 0; item.ColumnEnd = 0; item.RowStart = 0; item.RowEnd = 0;
            }

            // Resolve spans
            if (item.ColumnEnd <= item.ColumnStart && item.ColumnEnd != 0) item.ColumnEnd = item.ColumnStart + 1;
            if (item.RowEnd <= item.RowStart && item.RowEnd != 0) item.RowEnd = item.RowStart + 1;
            if (item.ColumnEnd == 0 && item.ColumnStart != 0) item.ColumnEnd = item.ColumnStart + 1;
            if (item.RowEnd == 0 && item.RowStart != 0) item.RowEnd = item.RowStart + 1;
            if (item.ColumnStart == 0 && item.ColumnEnd != 0) item.ColumnStart = item.ColumnEnd - 1;
            if (item.RowStart == 0 && item.RowEnd != 0) item.RowStart = item.RowEnd - 1;
            if (item.ColumnStart == 0 && item.ColumnEnd == 0) { item.ColumnStart = 1; item.ColumnEnd = 2; }
            if (item.RowStart == 0 && item.RowEnd == 0) { item.RowStart = 1; item.RowEnd = 2; }

            item.ColumnSpan = item.ColumnEnd - item.ColumnStart;
            item.RowSpan = item.RowEnd - item.RowStart;

            if (hasExplicit)
                explicitItems.Add(item);
            else
                autoItems.Add(item);
        }

        // Place explicit items
        foreach (var item in explicitItems)
        {
            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
            maxRow = Math.Max(maxRow, item.RowEnd - 1);
            item.IsPlaced = true;
            items.Add(item);
        }

        // Auto-placement with dense packing
        if (densePacking)
        {
            // Dense: place each auto item at the earliest possible position
            foreach (var item in autoItems)
            {
                for (int r = 0; r <= maxRow + 100; r++)
                {
                    for (int c = 0; c <= maxCol + 100; c++)
                    {
                        if (!IsOccupied(items, c, r, item.ColumnSpan, item.RowSpan))
                        {
                            item.ColumnStart = c + 1;
                            item.RowStart = r + 1;
                            item.ColumnEnd = item.ColumnStart + item.ColumnSpan;
                            item.RowEnd = item.RowStart + item.RowSpan;
                            item.IsPlaced = true;
                            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
                            maxRow = Math.Max(maxRow, item.RowEnd - 1);
                            goto nextDense;
                        }
                    }
                }
            nextDense:;
                items.Add(item);
            }
        }
        else
        {
            // Sparse auto-placement: fill rows first, then columns
            int cursorRow = 0;
            int cursorCol = 0;
            foreach (var item in autoItems)
            {
                bool placed = false;
                for (int r = cursorRow; r <= maxRow + 100; r++)
                {
                    for (int c = (r == cursorRow ? cursorCol : 0); c <= maxCol + 100; c++)
                    {
                        if (!IsOccupied(items, c, r, item.ColumnSpan, item.RowSpan))
                        {
                            item.ColumnStart = c + 1;
                            item.RowStart = r + 1;
                            item.ColumnEnd = item.ColumnStart + item.ColumnSpan;
                            item.RowEnd = item.RowStart + item.RowSpan;
                            item.IsPlaced = true;
                            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
                            maxRow = Math.Max(maxRow, item.RowEnd - 1);
                            cursorRow = r;
                            cursorCol = c + item.ColumnSpan;
                            placed = true;
                            break;
                        }
                    }
                    if (placed) break;
                }
                if (!placed)
                {
                    item.ColumnStart = 1;
                    item.RowStart = maxRow + 1;
                    item.ColumnEnd = item.ColumnStart + item.ColumnSpan;
                    item.RowEnd = item.RowStart + item.RowSpan;
                    item.IsPlaced = true;
                    maxRow = item.RowEnd - 1;
                }
                items.Add(item);
            }
        }

        return items;
    }

    private static bool IsOccupied(List<GridItem> items, int col, int row, int colSpan, int rowSpan)
    {
        foreach (var item in items)
        {
            if (!item.IsPlaced) continue;
            int itemColStart = item.ColumnStart - 1;
            int itemRowStart = item.RowStart - 1;
            int itemColEnd = item.ColumnEnd - 1;
            int itemRowEnd = item.RowEnd - 1;

            // Check overlap
            if (col < itemColEnd && col + colSpan > itemColStart &&
                row < itemRowEnd && row + rowSpan > itemRowStart)
                return true;
        }
        return false;
    }

    private void BuildNamedAreaMap(List<string[]> areas, Dictionary<string, (int col, int row, int colSpan, int rowSpan)> map)
    {
        for (int r = 0; r < areas.Count; r++)
        {
            for (int c = 0; c < areas[r].Length; c++)
            {
                string name = areas[r][c].ToLowerInvariant();
                if (name == "." || string.IsNullOrEmpty(name)) continue;
                if (map.ContainsKey(name)) continue;

                // Find the span of this area
                int colSpan = 1, rowSpan = 1;
                while (c + colSpan < areas[r].Length && areas[r][c + colSpan].ToLowerInvariant() == name) colSpan++;
                for (int rr = r + 1; rr < areas.Count; rr++)
                {
                    bool allMatch = true;
                    for (int cc = c; cc < c + colSpan && cc < areas[rr].Length; cc++)
                    {
                        if (areas[rr][cc].ToLowerInvariant() != name) { allMatch = false; break; }
                    }
                    if (allMatch && areas[rr].Length >= c + colSpan) rowSpan++;
                    else break;
                }
                map[name] = (c, r, colSpan, rowSpan);
            }
        }
    }

    private static (int start, int end) ParseGridLine(ComputedStyle style, string startProp, string endProp, int explicitCount)
    {
        int start = 0, end = 0;
        var startVal = startProp switch
        {
            "grid-column-start" => style.GridColumnStart,
            "grid-column-end" => style.GridColumnEnd,
            "grid-row-start" => style.GridRowStart,
            "grid-row-end" => style.GridRowEnd,
            _ => null
        };
        var endVal = endProp switch
        {
            "grid-column-start" => style.GridColumnStart,
            "grid-column-end" => style.GridColumnEnd,
            "grid-row-start" => style.GridRowStart,
            "grid-row-end" => style.GridRowEnd,
            _ => null
        };

        if (!string.IsNullOrEmpty(startVal))
        {
            if (startVal.Equals("span", StringComparison.OrdinalIgnoreCase))
                start = -1;
            else if (int.TryParse(startVal, out var s))
                start = s > 0 ? s : s + explicitCount + 1;
        }

        if (!string.IsNullOrEmpty(endVal))
        {
            if (endVal.Equals("span", StringComparison.OrdinalIgnoreCase))
                end = -1;
            else if (int.TryParse(endVal, out var e))
                end = e > 0 ? e : e + explicitCount + 1;
        }

        // Handle span: start:span 2 means the item spans 2 cols
        if (start < 0 && end > 0) { start = end - 2; }
        if (end < 0 && start > 0) { end = start + 2; }

        return (start, end);
    }

    private void ExpandImplicitTracks(List<GridItem> items, ref List<GridTrack> columns, ref List<GridTrack> rows)
    {
        int maxCol = columns.Count;
        int maxRow = rows.Count;
        foreach (var item in items)
        {
            maxCol = Math.Max(maxCol, item.ColumnEnd - 1);
            maxRow = Math.Max(maxRow, item.RowEnd - 1);
        }

        while (columns.Count < maxCol)
            columns.Add(new GridTrack { SizeType = TrackSizeType.Auto, BaseSize = 100 });
        while (rows.Count < maxRow)
            rows.Add(new GridTrack { SizeType = TrackSizeType.Auto, BaseSize = 20 });
    }

    private void ResolveTracks(List<GridTrack> tracks, List<GridItem> items, float containerSize, float gap, bool isColumn)
    {
        if (tracks.Count == 0) return;

        // Step 1: Initialize base sizes from min/max constraints
        foreach (var track in tracks)
            track.Initialize(containerSize, _containerStyle?.FontSize ?? 16, _viewportWidth, _viewportHeight);

        // Step 2: Calculate item contributions for intrinsic sizing
        foreach (var item in items)
        {
            var style = item.Element.ComputedStyle;
            if (style == null) continue;

            if (isColumn)
            {
                int start = item.ColumnStart - 1;
                int end = item.ColumnEnd - 1;
                float itemSize = ResolveDefiniteSize(style.Width, containerSize, style.FontSize, _rootFontSize);

                if (itemSize > 0)
                {
                    float perTrackSize = itemSize / (end - start);
                    for (int i = start; i < end && i < tracks.Count; i++)
                    {
                        tracks[i].BaseSize = Math.Max(tracks[i].BaseSize, perTrackSize);
                        tracks[i].GrowLimit = Math.Max(tracks[i].GrowLimit, perTrackSize);
                    }
                }
            }
            else
            {
                int start = item.RowStart - 1;
                int end = item.RowEnd - 1;
                float itemSize = ResolveDefiniteSize(style.Height, containerSize, style.FontSize, _rootFontSize);

                if (itemSize > 0)
                {
                    float perTrackSize = itemSize / (end - start);
                    for (int i = start; i < end && i < tracks.Count; i++)
                    {
                        tracks[i].BaseSize = Math.Max(tracks[i].BaseSize, perTrackSize);
                        tracks[i].GrowLimit = Math.Max(tracks[i].GrowLimit, perTrackSize);
                    }
                }
            }
        }

        // Step 3: Maximize tracks (distribute positive free space)
        float totalUsed = 0;
        foreach (var t in tracks)
            totalUsed += t.BaseSize;

        float totalGap = gap * (tracks.Count - 1);
        float freeSpace = containerSize - totalUsed - totalGap;

        if (freeSpace > 0)
        {
            // Distribute to non-fr tracks first
            int nonFrCount = tracks.Count(t => t.SizeType != TrackSizeType.Fraction);
            if (nonFrCount > 0)
            {
                float perTrack = freeSpace / nonFrCount;
                foreach (var t in tracks)
                {
                    if (t.SizeType != TrackSizeType.Fraction)
                    {
                        float growLimit = t.GrowLimit > 0 ? t.GrowLimit : float.MaxValue;
                        float add = Math.Min(perTrack, growLimit - t.BaseSize);
                        if (add > 0)
                        {
                            t.BaseSize += add;
                            freeSpace -= add;
                        }
                    }
                }
            }
        }

        // Step 4: Stretch auto tracks
        if (freeSpace > 0)
        {
            int autoCount = tracks.Count(t => t.SizeType == TrackSizeType.Auto);
            if (autoCount > 0)
            {
                float perTrack = freeSpace / autoCount;
                foreach (var t in tracks)
                {
                    if (t.SizeType == TrackSizeType.Auto)
                    {
                        t.BaseSize += perTrack;
                    }
                }
                freeSpace = 0;
            }
        }

        // Step 5: Expand flexible (fr) tracks
        if (freeSpace > 0 || tracks.Any(t => t.SizeType == TrackSizeType.Fraction))
        {
            ExpandFlexibleTracks(tracks, containerSize, freeSpace, totalGap);
        }

        // Clamp
        foreach (var t in tracks)
            t.BaseSize = Math.Max(t.BaseSize, 0);
    }

    private void ExpandFlexibleTracks(List<GridTrack> tracks, float containerSize, float freeSpace, float totalGap)
    {
        float totalFr = 0;
        float nonFrUsed = 0;
        foreach (var t in tracks)
        {
            if (t.SizeType == TrackSizeType.Fraction)
                totalFr += t.Fraction;
            else
                nonFrUsed += t.BaseSize;
        }

        if (totalFr <= 0) return;

        float availableForFr = containerSize - nonFrUsed - totalGap;
        if (availableForFr <= 0) return;

        float frUnit = availableForFr / totalFr;

        foreach (var t in tracks)
        {
            if (t.SizeType == TrackSizeType.Fraction)
                t.BaseSize = frUnit * t.Fraction;
        }
    }

    private static float ResolveDefiniteSize(Length? length, float containerSize, float fontSize, float rootFontSize)
    {
        if (length is PixelLength px) return px.Value;
        if (length is PercentLength pct) return pct.Value * containerSize;
        if (length is EmLength em) return em.Value * fontSize;
        if (length is RemLength rem) return rem.Value * rootFontSize;
        return 0;
    }

    private void PositionItems(List<GridItem> items, List<GridTrack> columns, List<GridTrack> rows, LayoutBox containerBox, float columnGap, float rowGap)
    {
        // Compute column offsets
        var colOffsets = new float[columns.Count + 1];
        float offset = containerBox.ContentBox.Left;
        for (int i = 0; i < columns.Count; i++)
        {
            colOffsets[i] = offset;
            offset += columns[i].BaseSize + columnGap;
        }
        colOffsets[columns.Count] = offset;

        // Compute row offsets
        var rowOffsets = new float[rows.Count + 1];
        offset = containerBox.ContentBox.Top;
        for (int i = 0; i < rows.Count; i++)
        {
            rowOffsets[i] = offset;
            offset += rows[i].BaseSize + rowGap;
        }
        rowOffsets[rows.Count] = offset;

        var containerStyle = _containerStyle!;
        var justifyItems = ParseJustifyItems(containerStyle.JustifyItems);
        var alignItems = ParseAlignItems(containerStyle.AlignItems.ToString());

        // Position each item
        foreach (var item in items)
        {
            int col = item.ColumnStart - 1;
            int row = item.RowStart - 1;
            int colEnd = Math.Min(item.ColumnEnd - 1, columns.Count);
            int rowEnd = Math.Min(item.RowEnd - 1, rows.Count);

            float cellX = colOffsets[col];
            float cellY = rowOffsets[row];
            float cellW = colOffsets[colEnd] - colOffsets[col];
            float cellH = rowOffsets[rowEnd] - rowOffsets[row];

            // Lay out the item at the cell's inline size and convert the real
            // fragment (text runs, line boxes, nested children) into the layout
            // box tree — the same per-child dispatch flex uses, so a grid item's
            // nested formatting contexts (flex/grid/table/replaced) are covered.
            var childSpace = _space.InheritBuilder(cellW, float.PositiveInfinity)
                .SetIsFixedInlineSize(true)
                .SetIsNewFormattingContext(true)
                .ToConstraintSpace();
            var itemResult = new BlockLayoutAlgorithm(item.Element, childSpace).Layout();
            var childBox = AuroraFragmentConverter.ToLayoutBox(itemResult.Fragment, item.Element, containerBox);

            float itemW = childBox.ContentBox.Width;
            float itemH = childBox.ContentBox.Height;

            // Apply alignment
            var style = item.Element.ComputedStyle!;
            var justifySelf = ParseJustifySelf(style.JustifySelf ?? "auto", justifyItems);
            var alignSelf = ParseAlignSelfEnum(style.AlignSelf, alignItems);

            // An auto-sized item stretches to fill its cell (CSS grid default);
            // an item with a definite size keeps it and is aligned in the cell.
            bool stretchInline = justifySelf == JustifyItemsType.Stretch && (style.Width is AutoLength or null);
            bool stretchBlock = alignSelf == AlignItemsType.Stretch && (style.Height is AutoLength or null);

            float alignW = stretchInline ? cellW : itemW;
            float alignH = stretchBlock ? cellH : itemH;

            float finalX = cellX + GetAlignmentOffset(cellW, alignW, justifySelf);
            float finalY = cellY + GetAlignmentOffset(cellH, alignH, alignSelf);

            // Translate the child box and its subtree (lines, runs, children)
            // to the aligned position within the grid content box.
            TranslateBox(childBox, finalX - childBox.BorderBox.Left, finalY - childBox.BorderBox.Top);

            if (stretchInline || stretchBlock)
                ExpandBoxToCell(childBox,
                    stretchInline ? cellW : childBox.BorderBox.Width,
                    stretchBlock ? cellH : childBox.BorderBox.Height);

            childBox.Float = FloatType.None;

            // Add to container
            containerBox.Children.Add(childBox);
        }

        // Reflect the laid-out content height (row tracks + gaps) on the
        // container box so auto-height grids report a real content box instead
        // of the placeholder seeded for track unit resolution.
        float contentEnd = rowOffsets[rows.Count];
        containerBox.ContentBox = new SKRect(
            containerBox.ContentBox.Left,
            containerBox.ContentBox.Top,
            containerBox.ContentBox.Right,
            _containerHeightAuto ? Math.Max(containerBox.ContentBox.Top, contentEnd)
                                 : Math.Max(containerBox.ContentBox.Bottom, contentEnd));
    }

    /// <summary>
    /// Shift a converted layout box subtree by (dx, dy) without disturbing the
    /// offsets between lines/runs/children (they all move together).
    /// </summary>
    private static void TranslateBox(Dom.LayoutBox box, float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;

        box.MarginBox = Offset(box.MarginBox, dx, dy);
        box.BorderBox = Offset(box.BorderBox, dx, dy);
        box.PaddingBox = Offset(box.PaddingBox, dx, dy);
        box.ContentBox = Offset(box.ContentBox, dx, dy);

        if (box.Lines != null)
        {
            foreach (var line in box.Lines)
            {
                line.X += dx;
                line.Y += dy;
                line.Baseline += dy;
                foreach (var run in line.Runs)
                {
                    run.X += dx;
                    run.Baseline += dy;
                }
            }
        }
        if (box.LineRuns != null)
        {
            foreach (var run in box.LineRuns)
            {
                run.X += dx;
                run.Baseline += dy;
            }
        }

        foreach (var child in box.Children)
            TranslateBox(child, dx, dy);
    }

    private static SKRect Offset(SKRect r, float dx, float dy) =>
        new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);

    /// <summary>
    /// Grow a stretched item's box to fill its cell. Only expands (never shrinks
    /// below the laid-out content), so an item that overflows its row tracks
    /// keeps its content size.
    /// </summary>
    private static void ExpandBoxToCell(Dom.LayoutBox box, float borderWidth, float borderHeight)
    {
        float dw = borderWidth - box.BorderBox.Width;
        float dh = borderHeight - box.BorderBox.Height;
        if (dw > 0)
        {
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right + dw, box.ContentBox.Bottom);
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right + dw, box.PaddingBox.Bottom);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right + dw, box.BorderBox.Bottom);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right + dw, box.MarginBox.Bottom);
        }
        if (dh > 0)
        {
            box.ContentBox = new SKRect(box.ContentBox.Left, box.ContentBox.Top, box.ContentBox.Right, box.ContentBox.Bottom + dh);
            box.PaddingBox = new SKRect(box.PaddingBox.Left, box.PaddingBox.Top, box.PaddingBox.Right, box.PaddingBox.Bottom + dh);
            box.BorderBox = new SKRect(box.BorderBox.Left, box.BorderBox.Top, box.BorderBox.Right, box.BorderBox.Bottom + dh);
            box.MarginBox = new SKRect(box.MarginBox.Left, box.MarginBox.Top, box.MarginBox.Right, box.MarginBox.Bottom + dh);
        }
    }

    private static JustifyItemsType ParseJustifyItems(string value) => value.ToLowerInvariant() switch
    {
        "start" => JustifyItemsType.Start,
        "end" => JustifyItemsType.End,
        "center" => JustifyItemsType.Center,
        "stretch" => JustifyItemsType.Stretch,
        _ => JustifyItemsType.Stretch
    };

    private static AlignItemsType ParseAlignItems(string value) => value.ToLowerInvariant() switch
    {
        "start" => AlignItemsType.Start,
        "end" => AlignItemsType.End,
        "center" => AlignItemsType.Center,
        "stretch" => AlignItemsType.Stretch,
        "baseline" => AlignItemsType.Baseline,
        _ => AlignItemsType.Stretch
    };

    private static JustifyItemsType ParseJustifySelf(string value, JustifyItemsType parent) => value.ToLowerInvariant() switch
    {
        "auto" => parent,
        "start" => JustifyItemsType.Start,
        "end" => JustifyItemsType.End,
        "center" => JustifyItemsType.Center,
        "stretch" => JustifyItemsType.Stretch,
        _ => parent
    };

    private static AlignItemsType ParseAlignSelfEnum(Dom.AlignSelfType value, AlignItemsType parent) => value switch
    {
        Dom.AlignSelfType.Auto => parent,
        Dom.AlignSelfType.FlexStart => AlignItemsType.Start,
        Dom.AlignSelfType.FlexEnd => AlignItemsType.End,
        Dom.AlignSelfType.Center => AlignItemsType.Center,
        Dom.AlignSelfType.Stretch => AlignItemsType.Stretch,
        Dom.AlignSelfType.Baseline => AlignItemsType.Baseline,
        _ => parent
    };

    private static float GetAlignmentOffset(float cellSize, float itemSize, JustifyItemsType alignment) => alignment switch
    {
        JustifyItemsType.Start => 0,
        JustifyItemsType.End => cellSize - itemSize,
        JustifyItemsType.Center => (cellSize - itemSize) / 2,
        JustifyItemsType.Stretch => 0, // stretch: item fills the cell
        _ => 0
    };

    private static float GetAlignmentOffset(float cellSize, float itemSize, AlignItemsType alignment) => alignment switch
    {
        AlignItemsType.Start => 0,
        AlignItemsType.End => cellSize - itemSize,
        AlignItemsType.Center => (cellSize - itemSize) / 2,
        AlignItemsType.Stretch => 0,
        AlignItemsType.Baseline => 0,
        _ => 0
    };

    private enum JustifyItemsType { Start, End, Center, Stretch }
    private enum AlignItemsType { Start, End, Center, Stretch, Baseline }
}

public class GridTrack
{
    public TrackSizeType SizeType { get; set; } = TrackSizeType.Auto;
    public float FixedSize { get; set; }
    public float Percentage { get; set; }
    public float Fraction { get; set; }
    public float BaseSize { get; set; }
    public float GrowLimit { get; set; } = float.MaxValue;
    public GridTrack? MinSize { get; set; }
    public GridTrack? MaxSize { get; set; }

    public float ResolveSize(float containerSize, float fontSize, float viewportWidth, float viewportHeight) => SizeType switch
    {
        TrackSizeType.Fixed => FixedSize,
        TrackSizeType.Percentage => Percentage * containerSize,
        TrackSizeType.Fraction => 0,
        TrackSizeType.Auto => 0,
        TrackSizeType.MinContent => 0,
        TrackSizeType.MaxContent => 0,
        TrackSizeType.MinMax => ResolveMinMax(containerSize, fontSize, viewportWidth, viewportHeight),
        _ => 0
    };

    private float ResolveMinMax(float containerSize, float fontSize, float viewportWidth, float viewportHeight)
    {
        float min = MinSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? 0;
        float max = MaxSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? float.MaxValue;
        if (max == 0) max = float.MaxValue;
        return Math.Clamp(BaseSize, min, max);
    }

    public void Initialize(float containerSize, float fontSize, float viewportWidth, float viewportHeight)
    {
        if (SizeType == TrackSizeType.MinMax)
        {
            float min = MinSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? 0;
            float max = MaxSize?.ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight) ?? float.MaxValue;
            if (max == 0) max = float.MaxValue;
            BaseSize = min;
            GrowLimit = max;
        }
        else if (SizeType == TrackSizeType.Fraction)
        {
            BaseSize = 0;
            GrowLimit = float.MaxValue;
        }
        else
        {
            BaseSize = ResolveSize(containerSize, fontSize, viewportWidth, viewportHeight);
            GrowLimit = BaseSize;
        }
    }

    public GridTrack Clone() => new()
    {
        SizeType = SizeType, FixedSize = FixedSize, Percentage = Percentage,
        Fraction = Fraction, BaseSize = BaseSize, MinSize = MinSize, MaxSize = MaxSize
    };
}

public enum TrackSizeType { Fixed, Percentage, Fraction, Auto, MinContent, MaxContent, MinMax }

public class GridItem
{
    public Element Element { get; set; } = null!;
    public int ColumnStart { get; set; } = 1;
    public int ColumnEnd { get; set; } = 2;
    public int RowStart { get; set; } = 1;
    public int RowEnd { get; set; } = 2;
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public bool IsPlaced { get; set; }
}