using UpBrowser.Core.Dom;
using UpBrowser.Core.Fonts;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Lays out flex items in a flex formatting context.
/// Implements the flex box layout algorithm: main/cross axis, flex-grow/shrink,
/// justify-content, align-items, and wrapping. Mirrors the engine's FlexLayoutAlgorithm.
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
        public float MainAxisBorderPadding { get; set; }
        public float CrossAxisBorderPadding { get; set; }
        public bool Stretched { get; set; }

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

        // Definite cross/main size from the container's own width/height. When the
        // flex container has a definite main size (its own width/height rather than
        // the parent's), items and justify-content are resolved against it --
        // otherwise a centered box with its own width would size/justify against
        // the (much wider) containing block. Pixel AND percentage lengths are
        // definite here: a width:50% container resolves against the space's
        // percentage base (the containing block), matching how the container-size
        // computation below (Compute*ForFragment) resolves the same property, so
        // the gate and the resolution can never disagree.
        float definiteMain;
        float definiteCross;
        if (isRow)
        {
            definiteMain = ResolveOwnContentSize(style.Width, isInlineAxis: true, bp);
            definiteCross = ResolveOwnContentSize(style.Height, isInlineAxis: false, bp);
        }
        else
        {
            definiteMain = ResolveOwnContentSize(style.Height, isInlineAxis: false, bp);
            definiteCross = ResolveOwnContentSize(style.Width, isInlineAxis: true, bp);
        }

        float availableMain = !float.IsNaN(definiteMain)
            ? definiteMain
            : ChildAvailableInlineSize;
        float availableCross = !float.IsNaN(definiteCross)
            ? definiteCross
            : (isRow ? ChildAvailableBlockSize : ChildAvailableInlineSize);

        // Collect items in DOM order. Per CSS flexbox spec, contiguous in-flow
        // text becomes an anonymous block flex item, so a flex container can
        // center bare text (e.g. display:flex with only a text child). Element
        // children keep their 'order'-based sorting; anonymous items have order 0.
        _items.Clear();
        {
            var slots = new List<(int DomIndex, int Order, FlexItemData Item)>();
            var pendingText = new List<TextNode>();
            int domIndex = 0;

            foreach (var child in Node.Children)
            {
                if (child is TextNode tn)
                {
                    pendingText.Add(tn);
                }
                else if (child is Element el)
                {
                    if (HasMeaningfulText(pendingText))
                        slots.Add((domIndex++, 0, CreateAnonymousFlexItem(style, pendingText)));
                    pendingText.Clear();
                    if (el.ComputedStyle?.Display != DisplayType.None)
                    {
                        int order = el.ComputedStyle?.Order ?? 0;
                        slots.Add((domIndex, order, new FlexItemData(el)));
                    }
                    domIndex++;
                }
            }

            if (HasMeaningfulText(pendingText))
                slots.Add((domIndex++, 0, CreateAnonymousFlexItem(style, pendingText)));

            foreach (var slot in slots.OrderBy(s => s.Order).ThenBy(s => s.DomIndex))
                _items.Add(slot.Item);
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

        // Border/padding of each item. Flex sizes are content-box sizes (the
        // resolution of 'width'/'flex-basis'), so the item's border box is the
        // content size plus its own border and padding, mirroring block layout.
        foreach (var item in _items)
        {
            var itemBorder = LengthUtils.ComputeBorders(item.Style);
            var itemPadding = LengthUtils.ComputePadding(Space, item.Style);
            item.MainAxisBorderPadding = isRow
                ? itemBorder.HorizontalSum + itemPadding.HorizontalSum
                : itemBorder.VerticalSum + itemPadding.VerticalSum;
            item.CrossAxisBorderPadding = isRow
                ? itemBorder.VerticalSum + itemPadding.VerticalSum
                : itemBorder.HorizontalSum + itemPadding.HorizontalSum;
        }

        // Determine cross size of the flex line. The line spans the largest
        // item's OUTER hypothetical cross size (content + border + padding).
        // With a definite container cross size (single line), the line spans the
        // full inner cross size so that align-items/justify can center content
        // within the container.
        float lineCrossSize = 0;
        foreach (var item in _items)
            lineCrossSize = Math.Max(lineCrossSize, item.HypotheticalCrossSize + item.CrossAxisBorderPadding);
        if (_items.Count > 0 && !float.IsNaN(definiteCross))
            lineCrossSize = Math.Max(lineCrossSize, availableCross);

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

            item.Stretched = alignSelf == Dom.AlignSelfType.Stretch && !hasDefiniteCross;
            item.UsedCrossSize = item.Stretched
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
                totalMain += item.UsedMainSize + item.MainAxisBorderPadding + item.MarginMainStart + item.MarginMainEnd + mainGap;
            totalMain -= mainGap;
            mainOffset = PaddingLeft + availableMain - totalMain;
        }

        foreach (var item in _items)
        {
            item.MainOffset = mainOffset + PaddingLeft;
            item.MarginMainStart = ResolveAutoMargin(item.Style.MarginLeft, item.Style.FontSize, availableMain);
            item.MarginMainEnd = ResolveAutoMargin(item.Style.MarginRight, item.Style.FontSize, availableMain);
            item.MainOffset += item.MarginMainStart;
            mainOffset += item.UsedMainSize + item.MainAxisBorderPadding + item.MarginMainStart + item.MarginMainEnd + mainGap;
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
            // Pick the child's layout algorithm by its own display type (e.g. a
            // nested flex/grid item lays out with its flex/grid algorithm rather
            // than as an opaque block).
            var result = BlockLayoutAlgorithm.LayoutAtomicInlineRoot(item.Element, childSpace);
            var fragment = result.Fragment;

            float mainPos = isRow ? item.MainOffset : item.CrossOffset;
            float crossPos = isRow ? item.CrossOffset : item.MainOffset;

            fragment.InlineOffset = mainPos;
            fragment.BlockOffset = rowBlockOffset + crossPos;
            // Fragment outer size = content (used flex size) + border/padding on
            // both axes. Stretched items keep their border box equal to the line
            // cross size (the content box shrinks inside the border/padding).
            fragment.InlineSize = item.UsedMainSize + (isRow ? item.MainAxisBorderPadding : item.CrossAxisBorderPadding);
            fragment.BlockSize = item.UsedCrossSize + (item.Stretched ? 0 : (isRow ? item.CrossAxisBorderPadding : item.MainAxisBorderPadding));
            fragment.MarginLeft = isRow ? item.MarginMainStart : item.MarginCrossStart;
            fragment.MarginTop = isRow ? item.MarginCrossStart : item.MarginMainStart;
            fragment.MarginRight = isRow ? item.MarginMainEnd : item.MarginCrossEnd;
            fragment.MarginBottom = isRow ? item.MarginCrossEnd : item.MarginMainEnd;

            Builder.AddChild(fragment);
            maxMainSize = Math.Max(maxMainSize, item.MainOffset + item.UsedMainSize + item.MainAxisBorderPadding);
        }

        // Compute container size. When the container has a definite main/cross size
        // of its own (e.g. width:120px / height:80px on the flex box) it wins
        // over the content-derived size. The declared width/height is the CONTENT
        // size (content-box semantics, like block layout); the border box adds the
        // container's own border+padding, so e.g. width:200px + 10px borders give
        // a 220px-wide box.
        float containerMain = _items.Count > 0 ? maxMainSize + PaddingRight : 0;
        float containerCross = _items.Count > 0 ? lineCrossSize + PaddingBottom : 0;

        // A block-level flex container (display:flex, not inline-flex) with an
        // auto width stretches to its containing block, exactly like a block
        // box. For a row flex that is the main axis; for a column flex the
        // inline (cross) axis. Indefinite sizes keep the content-derived size.
        bool fillsAvailableInline = style.Width is AutoLength && style.Display == DisplayType.Flex;
        if (fillsAvailableInline)
        {
            if (isRow && float.IsNaN(definiteMain))
                containerMain = ChildAvailableInlineSize;
            else if (!isRow && float.IsNaN(definiteCross))
                containerCross = ChildAvailableInlineSize;
        }
        if (!float.IsNaN(definiteMain))
            containerMain = isRow
                ? LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                    t => new MinMaxSizesResult(new MinMaxSizes(definiteMain, definiteMain)))
                : LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, containerMain, definiteMain);
        if (!float.IsNaN(definiteCross))
            containerCross = isRow
                ? LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, containerCross, definiteCross)
                : LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                    t => new MinMaxSizesResult(new MinMaxSizes(definiteCross, definiteCross)));

        Builder.InlineSize = isRow ? containerMain : containerCross;
        Builder.BlockSize = isRow ? containerCross : containerMain;
        Builder.IntrinsicBlockSize = Builder.BlockSize;

        var box = Builder.ToBoxFragment();
        box.Children.AddRange(Builder.Children);

        // Build the flex line output (FlexData), consumable by
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

    /// <summary>
    /// Resolve the flex container's own declared size on one axis to a
    /// content-box extent, or NaN when the property is auto/indefinite.
    /// Pixel lengths keep the historical convention (declared size minus the
    /// container's own border+padding); percentages resolve against the space's
    /// percentage base (the containing block), the same base
    /// LengthUtils.ResolveInline/BlockLength uses when the fragment size is
    /// computed. Returns NaN for auto and for percentages against an
    /// indeterminate base, so callers fall back to content-derived sizing.
    /// </summary>
    private float ResolveOwnContentSize(Length? length, bool isInlineAxis, BoxStrut bp)
    {
        float borderPadding = isInlineAxis ? bp.HorizontalSum : bp.VerticalSum;
        if (length is PixelLength px)
            return Math.Max(0, px.Value - borderPadding);
        if (length is PercentLength pct)
        {
            float basis = isInlineAxis
                ? Space.PercentageResolutionInlineSize
                : Space.PercentageResolutionBlockSize;
            if (float.IsNaN(basis) || float.IsInfinity(basis))
                return float.NaN;
            return Math.Max(0, basis * pct.Value - borderPadding);
        }
        return float.NaN;
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
        // Estimate content-based size. Text is measured with the real text
        // measurer when available so content-sized flex bases (and therefore
        // justify-content centering) use the actual glyph advance width.
        float size = 0;
        var style = element.ComputedStyle;
        foreach (var child in element.Children)
        {
            if (child is TextNode t)
            {
                string data = t.Data ?? "";
                if (data.Length == 0) continue;
                size += style != null ? TextMeasureProxy.MeasureText(data, style) : data.Length * 8;
            }
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
        var style = element.ComputedStyle;
        if (style != null)
        {
            foreach (var child in element.Children)
            {
                if (child is TextNode t && !string.IsNullOrWhiteSpace(t.Data))
                    size = Math.Max(size, LineBoxMetrics.GetLineHeight(style));
            }
        }

        foreach (var child in element.Children)
        {
            if (child is Element e && e.ComputedStyle?.Height is PixelLength h)
                size = Math.Max(size, h.Value);
        }
        return size;
    }

    /// <summary>
    /// True when the pending run contains at least one text node with non-space
    /// content. Whitespace-only runs between element children must not become
    /// anonymous flex items (they are collapsed by block/inline layout).
    /// </summary>
    private static bool HasMeaningfulText(List<TextNode> pendingText)
    {
        foreach (var textNode in pendingText)
        {
            var data = textNode.Data;
            if (!string.IsNullOrWhiteSpace(data))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds an anonymous block flex item wrapping a contiguous run of text
    /// nodes (CSS: in-flow text directly inside a flex container becomes an
    /// anonymous flex item). The synthetic element keeps <see cref="FlexItemData"/>
    /// uniform so block layout produces the text runs; the original text nodes
    /// are referenced without re-parenting, so painting and hit-testing still
    /// resolve style and events through their real parent element.
    /// </summary>
    private static FlexItemData CreateAnonymousFlexItem(ComputedStyle containerStyle, IReadOnlyList<TextNode> textNodes)
    {
        var wrapper = new HtmlElement("anonymous-flex-item");
        var style = containerStyle.Clone();
        style.Display = DisplayType.Block;
        style.Width = AutoLength.Instance;
        style.Height = AutoLength.Instance;
        style.MarginTop = style.MarginRight = style.MarginBottom = style.MarginLeft = new PixelLength(0);
        style.PaddingTop = style.PaddingRight = style.PaddingBottom = style.PaddingLeft = new PixelLength(0);
        style.BorderTopWidth = style.BorderBottomWidth = style.BorderLeftWidth = style.BorderRightWidth = 0;
        style.MinWidth = null;
        style.MaxWidth = null;
        style.MinHeight = null;
        style.MaxHeight = null;
        style.Order = 0;
        style.FlexGrow = 0;
        style.FlexShrink = 1;
        style.FlexBasis = AutoLength.Instance;
        style.BackgroundColor = null;
        style.BackgroundImage = null;
        wrapper.ComputedStyle = style;
        foreach (var textNode in textNodes)
            wrapper.Children.Add(textNode);
        return new FlexItemData(wrapper);
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
            Dom.AlignSelfType.FlexEnd => lineCrossSize - item.UsedCrossSize - item.CrossAxisBorderPadding,
            Dom.AlignSelfType.Center => (lineCrossSize - item.UsedCrossSize - item.CrossAxisBorderPadding) / 2,
            _ => 0 // stretch handled by setting cross size
        };
    }

    private void ApplyJustifyContent(List<FlexItemData> items, float availableMain, ComputedStyle style)
    {
        if (items.Count == 0) return;
        float totalMain = 0;
        foreach (var item in items)
            totalMain += item.UsedMainSize + item.MainAxisBorderPadding + item.MarginMainStart + item.MarginMainEnd;
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
