using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Responsible for building a LogicalLineItems from a LineInfo.
/// It's a helper for InlineLayoutAlgorithm. Mirrors logical_line_builder.cc.
/// </summary>
public class LogicalLineBuilder
{
    private const int kOpaqueBidiLevel = 0xff;

    private readonly InlineNode _node;
    private readonly ConstraintSpace _constraintSpace;
    private readonly InlineBreakToken? _breakToken;
    private readonly InlineLayoutStateStack _boxStates;
    private readonly InlineChildLayoutContext _context;
    private readonly FontBaselineType _baselineType;
    private readonly bool _quirksMode;

    private bool _hasOutOfFlowPositionedItems;
    private bool _hasFloatingItems;
    private bool _hasRelativePositionedItems;
    private InlineItemResult? _initialLetterItemResult;

    public bool HasOutOfFlowPositionedItems => _hasOutOfFlowPositionedItems;
    public bool HasFloatingItems => _hasFloatingItems;
    public bool HasRelativePositionedItems => _hasRelativePositionedItems;
    public InlineItemResult? InitialLetterItemResult => _initialLetterItemResult;
    public InlineLayoutStateStack BoxStates => _boxStates;

    public LogicalLineBuilder(InlineNode node, ConstraintSpace constraintSpace, InlineBreakToken? breakToken,
        InlineLayoutStateStack boxStates, InlineChildLayoutContext? context = null)
    {
        _node = node;
        _constraintSpace = constraintSpace;
        _breakToken = breakToken;
        _boxStates = boxStates;
        _context = context ?? new InlineChildLayoutContext();
        _baselineType = node.Style is { } s && s.Direction == "rtl" ? FontBaselineType.Ideographic : FontBaselineType.Alphabetic;
        _quirksMode = false;
    }

    public void CreateLine(LineInfo lineInfo, LogicalLineItems lineBox, InlineLayoutAlgorithm? mainLineHelper)
    {
        if (lineInfo == null) return;

        List<InlineItemResult> lineItems = lineInfo.MutableResults();

        // Compute heights of all inline items by placing the dominant baseline at 0.
        var lineStyle = lineInfo.HasLineStyle ? lineInfo.LineStyle() : _node.Style;
        _boxStates.SetIsEmptyLine(lineInfo.IsEmptyLine());
        InlineBoxState? box = _boxStates.OnBeginPlaceItems(_node, lineStyle, _baselineType, _quirksMode, lineBox);

        if (_quirksMode && lineStyle.Display == DisplayType.ListItem)
        {
            box!.ComputeTextMetrics(lineStyle, FontHelper.GetFont(lineStyle), _baselineType);
        }

        box = HandleItemResults(lineInfo, lineItems, lineBox, mainLineHelper, box);

        _boxStates.OnEndPlaceItems(_constraintSpace, lineBox, _baselineType);

        if (_node.IsBidiEnabled())
        {
            _boxStates.PrepareForReorder(lineBox);
            BidiReorder(lineInfo.BaseDirection(), lineBox, _boxStates.RubyColumnList());
            _boxStates.UpdateAfterReorder(lineBox);
        }

        // Accumulate the inline position of every child left to right. This is a
        // distinct step in the source (InlineLayoutAlgorithm::CreateLine calls
        // box_states_->ComputeInlinePositions right after the builder); without it
        // every child keeps its origin at inline offset 0 and runs paint on top of
        // one another. Children are placed relative to the line's content start
        // (position 0); the caller adds the container padding/text-align offset.
        _boxStates.ComputeInlinePositions(lineBox, 0f, lineInfo.IsBlockInInline());
    }

    internal InlineBoxState? HandleItemResults(LineInfo lineInfo, List<InlineItemResult> lineItems,
        LogicalLineItems lineBox, InlineLayoutAlgorithm? mainLineHelper, InlineBoxState? box)
    {
        foreach (var itemResult in lineItems)
        {
            var item = itemResult.Item;
            switch (item.Type)
            {
                case InlineItem.InlineItemType.Text:
                    {
                        if (itemResult.Length == 0)
                        {
                            // Empty or fully collapsed text isn't needed for layout.
                            continue;
                        }

                        if (_quirksMode)
                            box!.EnsureTextMetrics(item.Style(), FontHelper.GetFont(item.Style()), _baselineType);

                        // Take all used fonts into account if 'line-height: normal'.
                        if (box!.IncludeUsedFonts && itemResult.ShapeResult != null)
                            box.AccumulateUsedFonts(itemResult.ShapeResult);

                        if (itemResult.IsHyphenated)
                        {
                            float hyphenInlineSize = itemResult.Hyphen!.InlineSize();
                            lineBox.AddChild(item, itemResult, itemResult.TextOffset(), box.TextTop,
                                itemResult.InlineSize - hyphenInlineSize, box.TextHeight, item.BidiLevel);
                            PlaceHyphen(itemResult, hyphenInlineSize, lineBox, box);
                        }
                        else
                        {
                            lineBox.AddChild(item, itemResult, itemResult.TextOffset(), box.TextTop,
                                itemResult.InlineSize, box.TextHeight, item.BidiLevel);
                        }
                        break;
                    }
                case InlineItem.InlineItemType.Control:
                    PlaceControlItem(item, lineInfo.ItemsData().TextContent, itemResult, lineBox, box);
                    break;
                case InlineItem.InlineItemType.OpenTag:
                    box = HandleOpenTag(item, itemResult, lineBox);
                    break;
                case InlineItem.InlineItemType.CloseTag:
                    box = HandleCloseTag(item, itemResult, lineBox, box);
                    break;
                case InlineItem.InlineItemType.AtomicInline:
                    box = PlaceAtomicInline(item, itemResult, lineBox);
                    _hasRelativePositionedItems |= item.Style().Position == PositionType.Relative;
                    break;
                case InlineItem.InlineItemType.BlockInInline:
                    mainLineHelper?.PlaceBlockInInline(item, itemResult, lineBox);
                    break;
                case InlineItem.InlineItemType.OpenRubyColumn:
                    if (itemResult.RubyColumn != null)
                    {
                        box = PlaceRubyColumn(lineInfo, itemResult, lineBox, box);
                    }
                    else
                    {
                        lineBox.AddChild(item.BidiLevel);
                    }
                    break;
                case InlineItem.InlineItemType.CloseRubyColumn:
                    lineBox.AddChild(item.BidiLevel);
                    break;
                case InlineItem.InlineItemType.RubyLinePlaceholder:
                    {
                        float startOverhang = itemResult.Margins.Left;
                        float endOverhang = itemResult.Margins.Right;
                        lineBox.AddChild(item, itemResult, itemResult.TextOffset(), 0,
                            itemResult.InlineSize + startOverhang + endOverhang, 0, item.BidiLevel);
                        lineBox[lineBox.Count - 1].Rect = new LogicalRect(startOverhang, 0, lineBox[lineBox.Count - 1].Rect.InlineSize, 0);
                        break;
                    }
                case InlineItem.InlineItemType.ListMarker:
                    PlaceListMarker(item, itemResult);
                    break;
                case InlineItem.InlineItemType.OutOfFlowPositioned:
                    {
                        TextDirection direction = item.GetLayoutObject().IsOriginalDisplayInlineType()
                            ? item.Direction
                            : _constraintSpace.Direction;
                        lineBox.AddChild(item.GetLayoutObject(), item.BidiLevel, direction);
                        _hasOutOfFlowPositionedItems = true;
                        break;
                    }
                case InlineItem.InlineItemType.Floating:
                    {
                        if (itemResult.PositionedFloat != null)
                        {
                            if (itemResult.PositionedFloat.BreakBeforeToken == null)
                            {
                                lineBox.AddChild(itemResult.PositionedFloat.LayoutResult!, itemResult.PositionedFloat.BfcOffset, item.BidiLevel);
                            }
                        }
                        else
                        {
                            lineBox.AddChild(item.GetLayoutObject(), item.BidiLevel, itemResult.Start());
                        }
                        _hasFloatingItems = true;
                        _hasRelativePositionedItems |= item.Style().Position == PositionType.Relative;
                        break;
                    }
                case InlineItem.InlineItemType.BidiControl:
                    lineBox.AddChild(item.BidiLevel);
                    break;
                case InlineItem.InlineItemType.InitialLetterBox:
                    if (_initialLetterItemResult == null)
                    {
                        _initialLetterItemResult = itemResult;
                        PlaceInitialLetterBox(item, itemResult, lineBox);
                    }
                    break;
            }
        }
        return box;
    }

    private InlineBoxState? HandleOpenTag(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox)
    {
        var box = _boxStates.OnOpenTag(_constraintSpace, item, itemResult, _baselineType, lineBox);
        if (!_quirksMode || !item.IsEmptyItem())
        {
            box!.ComputeTextMetrics(item.Style(), FontHelper.GetFont(item.Style()), _baselineType);
        }
        return box;
    }

    private InlineBoxState? HandleCloseTag(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox, InlineBoxState? box)
    {
        if (_quirksMode && !item.IsEmptyItem())
            box!.EnsureTextMetrics(item.Style(), FontHelper.GetFont(item.Style()), _baselineType);
        box = _boxStates.OnCloseTag(_constraintSpace, lineBox, box, _baselineType);
        return box;
    }

    private void PlaceControlItem(InlineItem item, string textContent, InlineItemResult itemResult, LogicalLineItems lineBox, InlineBoxState? box)
    {
        // Don't generate fragments if this is a generated (not in DOM) break
        // opportunity during the white space collapsing in InlineItemBuilder.
        if (item.IsGeneratedForLineBreak)
            return;

        if (itemResult.Length == 0)
            return;

        if (_quirksMode && !box!.HasMetrics)
            box.EnsureTextMetrics(item.Style(), FontHelper.GetFont(item.Style()), _baselineType);

        lineBox.AddChild(item, itemResult.ShapeResult, itemResult.TextOffset(), "",
            box?.TextTop ?? 0, itemResult.InlineSize, box?.TextHeight ?? 0, item.BidiLevel);
    }

    private void PlaceHyphen(InlineItemResult itemResult, float hyphenInlineSize, LogicalLineItems lineBox, InlineBoxState? box)
    {
        var item = itemResult.Item;
        lineBox.AddChild(item, itemResult.Hyphen!.ShapeResult, itemResult.TextOffset(), itemResult.Hyphen.Text,
            box?.TextTop ?? 0, hyphenInlineSize, box?.TextHeight ?? 0, item.BidiLevel);
    }

    private InlineBoxState? PlaceAtomicInline(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox)
    {
        var box = _boxStates.OnOpenTag(_constraintSpace, item, itemResult, _baselineType, lineBox);

        PlaceLayoutResult(itemResult, lineBox, box, box!.Margins.Left + itemResult.SpacingBefore);

        return _boxStates.OnCloseTag(_constraintSpace, lineBox, box, _baselineType);
    }

    private void PlaceLayoutResult(InlineItemResult itemResult, LogicalLineItems lineBox, InlineBoxState? box, float inlineOffset)
    {
        if (itemResult.LayoutResult == null) return;
        var item = itemResult.Item;
        var fragment = itemResult.LayoutResult.Fragment;

        var font = FontHelper.GetFont(item.Style());
        float ascent = font.Ascent;
        float descent = Math.Max(font.Descent, fragment.BlockSize - ascent);
        if (descent < 0) descent = fragment.BlockSize;
        var metrics = new FontHeight(Math.Max(0, ascent), Math.Max(0, descent));
        box?.Unite(metrics);

        float lineTop = itemResult.Margins.Top - metrics.Ascent;
        lineBox.AddChild(itemResult.LayoutResult, new LogicalOffset(inlineOffset, lineTop),
            itemResult.InlineSize, fragment.BlockSize, item.BidiLevel);
    }

    private void PlaceInitialLetterBox(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox)
    {
        if (itemResult.LayoutResult == null) return;
        lineBox.AddChild(itemResult.LayoutResult,
            new LogicalOffset(itemResult.Margins.Left, 0), itemResult.InlineSize,
            itemResult.LayoutResult.Fragment.BlockSize, item.BidiLevel);
    }

    private InlineBoxState? PlaceRubyColumn(LineInfo lineInfo, InlineItemResult itemResult, LogicalLineItems lineBox, InlineBoxState? box)
    {
        // Ruby annotation placement is not modeled; base line items are laid out
        // through the normal item handler so the column contributes its width.
        var rubyColumn = itemResult.RubyColumn!;
        int startIndex = lineBox.Count;
        int rubyColumnStartIndex = _boxStates.RubyColumnList().Count;

        for (int i = 0; i < rubyColumn.PositionList.Count; i++)
        {
            var logicalColumn = _boxStates.CreateRubyColumn();
            logicalColumn.StartIndex = startIndex;
        }
        if (rubyColumn.PositionList.Count == 0)
        {
            var logicalColumn = _boxStates.CreateRubyColumn();
            logicalColumn.StartIndex = startIndex;
            logicalColumn.RubyPosition = RubyPosition.kOver;
        }

        box = HandleItemResults(lineInfo, rubyColumn.BaseLine.MutableResults(), lineBox, null, box);
        int columnBaseSize = lineBox.Count - startIndex;

        for (int i = 0; i < _boxStates.RubyColumnList().Count - rubyColumnStartIndex; i++)
        {
            var logicalColumn = _boxStates.RubyColumnAt(rubyColumnStartIndex + i);
            if (i < rubyColumn.PositionList.Count)
                logicalColumn.RubyPosition = rubyColumn.PositionList[i] == 0 ? RubyPosition.kOver : RubyPosition.kUnder;
            logicalColumn.Size = columnBaseSize;
        }

        return box;
    }

    private void PlaceListMarker(InlineItem item, InlineItemResult itemResult)
    {
        if (_quirksMode)
            _boxStates.LineBoxState.EnsureTextMetrics(item.Style(), FontHelper.GetFont(item.Style()), _baselineType);
    }

    internal void BidiReorder(TextDirection baseDirection, LogicalLineItems lineBox, List<LogicalRubyColumn> columnList)
    {
        if (lineBox.IsEmpty)
            return;

        int baseDirectionLevel = baseDirection == TextDirection.Ltr ? 0 : 1;

        // Create a list of chunk indices in the visual order, per UAX#9 L2.
        var levels = new int[lineBox.Count];
        bool hasOpaqueItems = false;
        for (int i = 0; i < lineBox.Count; i++)
        {
            var item = lineBox[i];
            if (item.IsOpaqueToBidiReordering)
            {
                levels[i] = kOpaqueBidiLevel;
                hasOpaqueItems = true;
                continue;
            }
            // UAX#9 L1: trailing whitespaces should use paragraph direction.
            if (item.HasOnlyBidiTrailingSpaces)
            {
                levels[i] = baseDirectionLevel;
                continue;
            }
            levels[i] = item.BidiLevel;
        }

        // For opaque items, copy bidi levels from adjacent items.
        if (hasOpaqueItems)
        {
            int lastLevel = baseDirectionLevel;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                if (levels[i] == kOpaqueBidiLevel)
                    levels[i] = lastLevel;
                else
                    lastLevel = levels[i];
            }
        }

        // Compute visual indices from resolved levels.
        int[] logicalToVisual = BidiParagraph.IndicesInVisualOrder(levels);

        // Reorder to the visual order.
        LogicalLineItems visualItems = _context.AcquireTempLogicalLineItems();
        visualItems.ReserveInitialCapacity(lineBox.Count);
        for (int i = 0; i < logicalToVisual.Length; i++)
        {
            int visualIndex = 0;
            for (int logicalIndex = 0; logicalIndex < lineBox.Count; logicalIndex++)
            {
                if (logicalToVisual[logicalIndex] == i)
                {
                    visualIndex = logicalIndex;
                    break;
                }
            }
            visualItems.AddChild(lineBox[visualIndex]);
        }
        // Replacing contents in-place.
        for (int i = 0; i < lineBox.Count; i++)
            lineBox[i] = visualItems[i];
        lineBox.Clear();
        for (int i = 0; i < visualItems.Count; i++)
            lineBox.AddChild(visualItems[i]);
        _context.ReleaseTempLogicalLineItems(visualItems);

        // Adjust LogicalRubyColumn::start_index.
        if (columnList.Count > 0)
        {
            var logicalToVisualMap = new int[lineBox.Count];
            for (int logicalIndex = 0; logicalIndex < lineBox.Count; logicalIndex++)
                logicalToVisualMap[logicalToVisual[logicalIndex]] = logicalIndex;

            foreach (var column in columnList)
            {
                if (column.StartIndex < logicalToVisualMap.Length)
                    column.StartIndex = logicalToVisualMap[column.StartIndex];
            }
            columnList.Sort((c1, c2) =>
            {
                int diff = c2.StartIndex - c1.StartIndex;
                return diff != 0 ? (diff > 0 ? -1 : 1) : c2.Size.CompareTo(c1.Size);
            });
        }
    }

    public void RebuildBoxStates(LineInfo lineInfo, int startItemIndex, int endItemIndex)
    {
        var lineStyle = lineInfo.HasLineStyle ? lineInfo.LineStyle() : _node.Style;
        LogicalLineItems lineBox = _context.AcquireTempLogicalLineItems();
        _boxStates.OnBeginPlaceItems(_node, lineStyle, _baselineType, _quirksMode, lineBox);
        for (int i = Math.Max(0, startItemIndex); i < endItemIndex && i < lineInfo.ItemsData().Items.Count; i++)
        {
            var item = lineInfo.ItemsData().Items[i];
            if (item.Type != InlineItem.InlineItemType.OpenTag) continue;
            var itemResult = new InlineItemResult(item, i);
            LineBreaker.ComputeOpenTagResult(item, _constraintSpace, _node.IsSvgText(), itemResult);
            HandleOpenTag(item, itemResult, lineBox);
        }
        _context.ReleaseTempLogicalLineItems(lineBox);
    }
}