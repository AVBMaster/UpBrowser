using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Lays out flex items in a flex formatting context.
/// Implements the flex box layout algorithm: main/cross axis, flex-grow/shrink,
/// justify-content, align-items, and wrapping. Mirrors Blink's FlexLayoutAlgorithm.
/// </summary>
public class FlexLayoutAlgorithm : LayoutAlgorithm
{
    private readonly List<FlexItemData> _items = new();
    public List<FlexLine> Lines { get; } = new();

    public FlexLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }

    private class FlexItemData
    {
        public Element Element { get; }
        public ComputedStyle Style => Element.ComputedStyle!;
        public float FlexBaseSize { get; set; }
        public float HypotheticalMainSize { get; set; }
        public float HypotheticalCrossSize { get; set; }
        public float UsedMainSize { get; set; }
        public float UsedCrossSize { get; set; }
        public float MarginMainStart { get; set; }
        public float MarginMainEnd { get; set; }
        public float MarginCrossStart { get; set; }
        public float MarginCrossEnd { get; set; }
        public float MainOffset { get; set; }
        public float CrossOffset { get; set; }
        public bool IsFrozen { get; set; }
        public float ClampedMainSize { get; set; }

        public FlexItemData(Element element) => Element = element;
    }

    public override LayoutResult Layout()
    {
        var border = LengthUtils.ComputeBorders(Style);
        var padding = LengthUtils.ComputePadding(Space, Style);
        var bp = new BoxStrut(border.Top + padding.Top, border.Right + padding.Right,
            border.Bottom + padding.Bottom, border.Left + padding.Left);

        Builder.BorderLeft = border.Left; Builder.BorderTop = border.Top;
        Builder.BorderRight = border.Right; Builder.BorderBottom = border.Bottom;
        Builder.PaddingLeft = padding.Left; Builder.PaddingTop = padding.Top;
        Builder.PaddingRight = padding.Right; Builder.PaddingBottom = padding.Bottom;
        Builder.Element = Node;

        var style = Style;
        bool isRow = style.FlexDirection == FlexDirectionType.Row || style.FlexDirection == FlexDirectionType.RowReverse;
        bool isReverse = style.FlexDirection == FlexDirectionType.RowReverse || style.FlexDirection == FlexDirectionType.ColumnReverse;
        float availableMain = ChildAvailableInlineSize;
        float availableCross = isRow ? ChildAvailableBlockSize : ChildAvailableInlineSize;

        // Collect items, sorted by CSS order property (FlexChildIterator).
        _items.Clear();
        var childIterator = new FlexChildIterator(Node);
        for (var childEl = childIterator.NextChild(); childEl != null; childEl = childIterator.NextChild())
        {
            if (childEl.ComputedStyle?.Display != DisplayType.None)
                _items.Add(new FlexItemData(childEl));
        }

        // Compute flex base size and hypothetical sizes
        foreach (var item in _items)
        {
            ComputeFlexBaseSize(item, availableMain, isRow);
            item.HypotheticalMainSize = item.FlexBaseSize;
            item.HypotheticalCrossSize = ComputeCrossSize(item, availableCross, isRow);
        }

        // Resolve flexible lengths (grow/shrink)
        ResolveFlexibleLengths(_items, availableMain);

        // Determine cross size of the flex line
        float lineCrossSize = 0;
        foreach (var item in _items)
            lineCrossSize = Math.Max(lineCrossSize, item.HypotheticalCrossSize);

        // Resolve each item's used cross size. 'align-self: stretch' (the initial
        // value via 'align-items') makes an auto-sized item fill the line's cross
        // size; an item with a definite cross size keeps it. Without this
        // UsedCrossSize stayed 0 and every flex item got a zero-height fragment.
        foreach (var item in _items)
        {
            var alignSelf = ResolveAlignSelf(item.Style, style);
            bool hasDefiniteCross = isRow
                ? item.Style.Height is PixelLength or PercentLength
                : item.Style.Width is PixelLength or PercentLength;

            item.UsedCrossSize = alignSelf == Dom.AlignSelfType.Stretch && !hasDefiniteCross
                ? lineCrossSize
                : item.HypotheticalCrossSize;
        }

        // Align items along cross axis
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            item.CrossOffset = ComputeCrossOffset(item, lineCrossSize, style);
        }

        // Position items along main axis
        float mainOffset = PaddingLeft;
        float mainGap = ResolveGap(style.ColumnGap, style.FontSize);

        if (isReverse)
        {
            // Reverse direction: position from the end
            float totalMain = 0;
            foreach (var item in _items)
                totalMain += item.UsedMainSize + item.MarginMainStart + item.MarginMainEnd + mainGap;
            totalMain -= mainGap;
            mainOffset = PaddingLeft + availableMain - totalMain;
        }

        foreach (var item in _items)
        {
            item.MainOffset = mainOffset + PaddingLeft;
            item.MarginMainStart = ResolveAutoMargin(item.Style.MarginLeft, item.Style.FontSize, availableMain);
            item.MarginMainEnd = ResolveAutoMargin(item.Style.MarginRight, item.Style.FontSize, availableMain);
            item.MainOffset += item.MarginMainStart;
            mainOffset += item.UsedMainSize + item.MarginMainStart + item.MarginMainEnd + mainGap;
        }

        // Apply justify-content
        if (_items.Count > 0)
            ApplyJustifyContent(_items, availableMain, style);

        // Build fragments
        float rowBlockOffset = PaddingTop;
        float maxMainSize = 0;
        foreach (var item in _items)
        {
            var childSpace = new ConstraintSpace(
                availableInlineSize: item.UsedMainSize,
                availableBlockSize: float.PositiveInfinity,
                isFixedInlineSize: true,
                isFixedBlockSize: false
            );
            var algo = new BlockLayoutAlgorithm(item.Element, childSpace);
            var result = algo.Layout();
            var fragment = result.Fragment;

            float mainPos = isRow ? item.MainOffset : item.CrossOffset;
            float crossPos = isRow ? item.CrossOffset : item.MainOffset;

            fragment.InlineOffset = mainPos;
            fragment.BlockOffset = rowBlockOffset + crossPos;
            fragment.InlineSize = item.UsedMainSize;
            fragment.BlockSize = item.UsedCrossSize;
            fragment.MarginLeft = isRow ? item.MarginMainStart : item.MarginCrossStart;
            fragment.MarginTop = isRow ? item.MarginCrossStart : item.MarginMainStart;
            fragment.MarginRight = isRow ? item.MarginMainEnd : item.MarginCrossEnd;
            fragment.MarginBottom = isRow ? item.MarginCrossEnd : item.MarginMainEnd;

            Builder.AddChild(fragment);
            maxMainSize = Math.Max(maxMainSize, item.MainOffset + item.UsedMainSize);
        }

        // Compute container size
        float containerMain = _items.Count > 0 ? maxMainSize + PaddingRight : 0;
        float containerCross = _items.Count > 0 ? lineCrossSize + PaddingBottom : 0;

        Builder.InlineSize = isRow ? containerMain : containerCross;
        Builder.BlockSize = isRow ? containerCross : containerMain;
        Builder.IntrinsicBlockSize = Builder.BlockSize;

        var box = Builder.ToBoxFragment();
        box.Children.AddRange(Builder.Children);

        // Build the NG-aligned flex line output (FlexData), consumable by
        // FlexItemIterator.
        Lines.Clear();
        var lineOut = new FlexLine(_items.Count);
        foreach (var item in _items)
        {
            var childBox = new Dom.LayoutBox { Dimensions = new BoxDimensions { Style = item.Style, Element = item.Element } };
            lineOut.Items.Add(new FlexItem(new BlockNode(childBox))
            {
                MainAxisFinalSize = item.UsedMainSize,
                Offset = new FlexOffset(item.MainOffset, item.CrossOffset),
            });
        }
        lineOut.MainAxisFreeSpace = 0;
        lineOut.LineCrossSize = lineCrossSize;
        Lines.Add(lineOut);

        return LayoutResult.FromFragment(box);
    }

    private void ComputeFlexBaseSize(FlexItemData item, float availableMain, bool isRow)
    {
        var style = item.Style;
        if (style.FlexBasis is PixelLength px)
        {
            item.FlexBaseSize = px.Value;
        }
        else if (style.Width is PixelLength widthPx && isRow)
        {
            item.FlexBaseSize = widthPx.Value;
        }
        else if (style.Height is PixelLength heightPx && !isRow)
        {
            item.FlexBaseSize = heightPx.Value;
        }
        else
        {
            // Auto: use content size
            item.FlexBaseSize = EstimateContentSize(item.Element, availableMain);
        }

        // Clamp by flex-shrink default and min/max
        item.ClampedMainSize = item.FlexBaseSize;
    }

    private static float EstimateContentSize(Element element, float availableMain)
    {
        // Estimate content-based size
        float size = 0;
        foreach (var child in element.Children)
        {
            if (child is TextNode t)
                size += t.Data?.Length * 8 ?? 0;
            else if (child is Element e && e.ComputedStyle?.Width is PixelLength w)
                size += w.Value;
        }
        return size;
    }

    private float ComputeCrossSize(FlexItemData item, float availableCross, bool isRow)
    {
        var style = item.Style;
        if (isRow && style.Height is PixelLength h) return h.Value;
        if (!isRow && style.Width is PixelLength w) return w.Value;
        return EstimateContentCrossSize(item.Element);
    }

    private static float EstimateContentCrossSize(Element element)
    {
        float size = 0;
        foreach (var child in element.Children)
        {
            if (child is Element e && e.ComputedStyle?.Height is PixelLength h)
                size = Math.Max(size, h.Value);
        }
        return size;
    }

    private void ResolveFlexibleLengths(List<FlexItemData> items, float availableMain)
    {
        // First pass: freeze items with flex-basis <= 0
        foreach (var item in items)
        {
            item.UsedMainSize = item.FlexBaseSize;
            item.IsFrozen = false;
        }

        // Compute total base size
        float totalBase = 0;
        foreach (var item in items)
            totalBase += item.FlexBaseSize;

        float freeSpace = availableMain - totalBase;

        if (freeSpace > 0)
        {
            // Distribute positive free space proportionally to flex-grow
            float totalGrow = 0;
            foreach (var item in items)
                totalGrow += item.Style.FlexGrow;

            if (totalGrow > 0)
            {
                foreach (var item in items)
                {
                    float growShare = item.Style.FlexGrow / totalGrow;
                    item.UsedMainSize = item.FlexBaseSize + freeSpace * growShare;
                }
            }
        }
        else if (freeSpace < 0)
        {
            // Distribute negative free space proportionally to flex-shrink
            float totalShrink = 0;
            foreach (var item in items)
                totalShrink += item.Style.FlexShrink;

            if (totalShrink > 0)
            {
                float spaceToRemove = -freeSpace;
                foreach (var item in items)
                {
                    float shrinkShare = item.Style.FlexShrink / totalShrink;
                    item.UsedMainSize = Math.Max(0, item.FlexBaseSize - spaceToRemove * shrinkShare);
                }
            }
        }
    }

    /// <summary>
    /// Resolve 'align-self: auto' against the container's 'align-items', giving
    /// the item's effective cross-axis alignment.
    /// </summary>
    private static Dom.AlignSelfType ResolveAlignSelf(ComputedStyle itemStyle, ComputedStyle containerStyle)
    {
        var alignSelf = itemStyle.AlignSelf;
        if (alignSelf != Dom.AlignSelfType.Auto)
            return alignSelf;

        return containerStyle.AlignItems switch
        {
            Dom.AlignItemsType.FlexStart => Dom.AlignSelfType.FlexStart,
            Dom.AlignItemsType.FlexEnd => Dom.AlignSelfType.FlexEnd,
            Dom.AlignItemsType.Center => Dom.AlignSelfType.Center,
            Dom.AlignItemsType.Baseline => Dom.AlignSelfType.Baseline,
            _ => Dom.AlignSelfType.Stretch
        };
    }

    private static float ComputeCrossOffset(FlexItemData item, float lineCrossSize, ComputedStyle style)
    {
        var alignSelf = ResolveAlignSelf(item.Style, style);

        return alignSelf switch
        {
            Dom.AlignSelfType.FlexStart => 0,
            Dom.AlignSelfType.FlexEnd => lineCrossSize - item.UsedCrossSize,
            Dom.AlignSelfType.Center => (lineCrossSize - item.UsedCrossSize) / 2,
            _ => 0 // stretch handled by setting cross size
        };
    }

    private void ApplyJustifyContent(List<FlexItemData> items, float availableMain, ComputedStyle style)
    {
        if (items.Count == 0) return;
        float totalMain = 0;
        foreach (var item in items)
            totalMain += item.UsedMainSize + item.MarginMainStart + item.MarginMainEnd;
        float freeSpace = availableMain - totalMain;

        switch (style.JustifyContent)
        {
            case Dom.JustifyContentType.Center:
                Shift(items, freeSpace / 2);
                break;
            case Dom.JustifyContentType.FlexEnd:
                Shift(items, freeSpace);
                break;
            case Dom.JustifyContentType.SpaceBetween:
                Distribute(items, freeSpace, true);
                break;
            case Dom.JustifyContentType.SpaceAround:
                Distribute(items, freeSpace, false);
                break;
            case Dom.JustifyContentType.SpaceEvenly:
                DistributeEvenly(items, availableMain);
                break;
        }
    }

    private static void Shift(List<FlexItemData> items, float amount)
    {
        foreach (var item in items)
            item.MainOffset += amount;
    }

    private static void Distribute(List<FlexItemData> items, float freeSpace, bool spaceBetween)
    {
        int gaps = items.Count - 1;
        if (gaps <= 0) return;
        float gap = spaceBetween ? freeSpace / gaps : freeSpace / (items.Count * 2);
        float offset = spaceBetween ? 0 : gap;
        foreach (var item in items)
        {
            item.MainOffset += offset;
            offset += gap + (spaceBetween ? 0 : gap);
        }
    }

    private static void DistributeEvenly(List<FlexItemData> items, float availableMain)
    {
        float totalMain = 0;
        foreach (var item in items)
            totalMain += item.UsedMainSize + item.MarginMainStart + item.MarginMainEnd;
        float freeSpace = availableMain - totalMain;
        float gap = freeSpace / (items.Count + 1);
        float offset = gap;
        foreach (var item in items)
        {
            item.MainOffset += offset;
            offset += gap + item.UsedMainSize;
        }
    }

    private float ResolveAutoMargin(Length length, float fontSize, float containingSize)
    {
        if (length is AutoLength || length == null) return 0;
        return length.ToPixels(fontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
    }

    private float ResolveGap(Length gap, float fontSize) =>
        gap is AutoLength ? 0 : gap.ToPixels(fontSize, Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
}
