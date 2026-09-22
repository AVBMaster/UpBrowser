using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Inline layout algorithm aligned to the modern layout pipeline. Uses ConstraintSpaceBuilder,
/// LengthUtils, BoxFragmentBuilder. Handles text runs, atomic inlines,
/// line breaking, and inline formatting context.
/// </summary>
public class InlineLayoutAlgorithm : LayoutAlgorithm
{
    private readonly LayoutAlgorithm _parent;
    private readonly List<BoxLine> _lines = new();
    private float _currentLineInlineOffset;
    private float _currentLineBlockOffset;
    private float _lineHeight;
    private FragmentItems? _fragmentItems;

    public FragmentItems? FragmentItems => _fragmentItems;

    public InlineLayoutAlgorithm(Element node, in ConstraintSpace space, LayoutAlgorithm parent)
        : base(node, space)
    {
        _parent = parent;
        _lineHeight = Fonts.LineBoxMetrics.GetLineHeight(node.ComputedStyle);
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

        _currentLineInlineOffset = padding.Left;
        _currentLineBlockOffset = padding.Top;
        _lines.Clear();

        float availInline = ChildAvailableInlineSize;

        // Line breaking must use THIS block's content-box width, not the parent's
        // available width. Otherwise a width-constrained block (e.g. width:129px,
        // or a multicol column) never wraps its text 鈥?it breaks only at the
        // ancestor width. When the constraint space fixes the inline size
        // (fragmentainers), that fixed size is already the content width.
        if (!Space.IsFixedInlineSize)
        {
            float ownBorderBox = LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
                t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
            if (!LengthUtils.IsIndefinite(ownBorderBox))
                availInline = Math.Max(0, ownBorderBox - bp.HorizontalSum);
        }

        float curInlineSize = 0, curBlockSize = 0, curBaseline = 0, maxBlockSize = 0;

        if (TryLayoutLinesWithNgPipeline(availInline, padding.Left, padding.Top))
        {
            // Modern pipeline produced lines; skip legacy path.
        }
        else
        {
            var curLine = new BoxLine { InlineOffset = padding.Left, BlockOffset = padding.Top };

            foreach (var child in Node.Children)
            {
                if (child is TextNode tn)
                    curLine = ProcessText(tn, curLine, availInline, ref curInlineSize, ref curBlockSize, ref curBaseline, ref maxBlockSize);
                else if (child is Element el)
                    curLine = ProcessElement(el, curLine, availInline, ref curInlineSize, ref curBlockSize, ref curBaseline, ref maxBlockSize);
            }

            if (curLine.Runs.Count > 0)
            {
                curLine.InlineSize = curInlineSize; curLine.BlockSize = curBlockSize;
                curLine.BaselineOffset = curBaseline; _lines.Add(curLine);
            }
        }

        float intrinsicBlock = 0;
        foreach (var l in _lines) intrinsicBlock = Math.Max(intrinsicBlock, l.BlockEnd + padding.Bottom);
        Builder.IntrinsicBlockSize = intrinsicBlock;

        float blockSize = LengthUtils.ComputeBlockSizeForFragment(Space, Style, bp, intrinsicBlock, availInline);
        if (LengthUtils.IsIndefinite(blockSize)) blockSize = intrinsicBlock;
        var (minB, maxB) = LengthUtils.ComputeMinMaxBlockSizes(Space, Style, bp, null, _ => intrinsicBlock);
        Builder.BlockSize = Math.Clamp(blockSize, minB, maxB);

        float inlineSize = LengthUtils.ComputeInlineSizeForFragment(Space, Style, bp,
            t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
        if (LengthUtils.IsIndefinite(inlineSize)) inlineSize = availInline;

        // Shrink-to-fit auto width (atomic inline / inline-block / float): the box
        // sizes to its content (the widest line), not to the full available
        // width. Without this an inline-block such as a <button> stretches across
        // the whole line and forces surrounding content to wrap.
        if (Space.IsShrinkToFit && Style.Width is null or AutoLength)
        {
            float maxLineInline = 0;
            foreach (var l in _lines)
                maxLineInline = Math.Max(maxLineInline, l.InlineSize);
            float contentBorderBox = maxLineInline + bp.HorizontalSum;
            if (contentBorderBox > 0)
                inlineSize = Math.Min(inlineSize, contentBorderBox);
        }

        var (minI, maxI) = LengthUtils.ComputeMinMaxInlineSizes(Space, Style, bp,
            t => new MinMaxSizesResult(new MinMaxSizes(availInline, availInline)));
        Builder.InlineSize = Math.Clamp(inlineSize, minI, maxI);

        var frag = Builder.ToBoxFragment();
        frag.Lines.AddRange(_lines);
        frag.FragmentItems = _fragmentItems;
        return LayoutResult.FromFragment(frag);
    }

    /// <summary>
    /// Modern inline layout path: collect items via InlineNode, break lines
    /// via LineBreaker, build logical line items via LogicalLineBuilder, then
    /// convert to the existing BoxLine/BoxRun output model. Returns false when
    /// there is nothing to lay out (so callers can fall back).
    /// </summary>
    private bool TryLayoutLinesWithNgPipeline(float availInline, float paddingLeft, float paddingTop)
    {
        var inlineNode = new InlineNode(Node, Style);
        inlineNode.CollectInlineItems();
        var data = inlineNode.ItemsData;
        if (data.Items.Count == 0) return false;

        var lineBreaker = new LineBreaker();
        lineBreaker.SetUnitContext(Space.RootFontSize, Space.ViewportWidth, Space.ViewportHeight);
        var lines = lineBreaker.BreakLines(data, availInline, Style);

        var stateStack = new InlineLayoutStateStack();
        var builder = new LogicalLineBuilder(inlineNode, Space, null, stateStack);

        // text-overflow: ellipsis support via LineTruncator. Per spec it only
        // applies when the block clips overflow (overflow != visible).
        bool useEllipsis = Style.TextOverflow is TextOverflowType.Ellipsis
            && Style.Overflow is not OverflowType.Visible;
        var itemsBuilder = new FragmentItemsBuilder();

        foreach (var info in lines)
        {
            info.AvailableInlineSize = availInline;
            var logicalLineItems = new LogicalLineItems();
            builder.CreateLine(info, logicalLineItems, this);

            // Truncate overflowing lines and place ellipsis.
            if (useEllipsis && info.HasOverflow())
            {
                var truncator = new LineTruncator(info);
                truncator.TruncateLine(info.InlineSize, logicalLineItems, stateStack);
            }

            // Apply text-align (end/center/justify). Justify distributes the
            // free inline space into word-spacing expansion opportunities on every
            // line except the last; start/left needs no work.
            if (info.TextAlign() is not TextAlignType.Start)
                JustificationUtils.ApplyTextAlignment(info, logicalLineItems, availInline);

            // The line box height is the united strut of the inline boxes on the
            // line; fall back to this container's own strut when the line breaker
            // did not report one.
            float lineBlockSize = info.BlockSize > 0
                ? info.BlockSize
                : Fonts.LineBoxMetrics.GetLineHeight(Style);

            var boxLine = new BoxLine
            {
                InlineOffset = paddingLeft,
                BlockOffset = _currentLineBlockOffset,
                InlineSize = info.InlineSize,
                BlockSize = lineBlockSize,
                BaselineOffset = _currentLineBlockOffset +
                    Fonts.LineBoxMetrics.GetBaselineForLineHeight(Style, lineBlockSize)
            };

            // Add a line item + content items to the FragmentItemsBuilder.
            var lineBoxFragment = new PhysicalLineBoxFragment
            {
                Size = new PhysicalSize(Math.Min(info.InlineSize, availInline), boxLine.BlockSize),
                BaselineOffset = boxLine.BaselineOffset,
            };
            itemsBuilder.Add(FragmentItem.CreateLine(lineBoxFragment, logicalLineItems.Count));

            for (int i = 0; i < logicalLineItems.Count; i++)
            {
                var item = logicalLineItems[i];
                if (item.IsHiddenForPaint) continue;
                if (!item.HasInFlowOrFloatingFragment && item.InlineItem == null && string.IsNullOrEmpty(item.TextContent))
                    continue;

                // LogicalLineBuilder has already laid the children out left to
                // right via ComputeInlinePositions, so the child's placed inline
                // offset is its Rect.InlineStart (relative to the line content
                // start). Add the container padding to get the container-relative
                // paint offset. Re-accumulating here was what stacked every run at
                // the same x.
                float runInline = paddingLeft + item.Rect.InlineStart;
                float itemInlineSize = item.Rect.InlineSize;

                if (item.InlineItem?.IsAtomicInline == true || item.LayoutResult != null)
                {
                    var atomicFragment = item.LayoutResult?.Fragment;
                    // For atomic inlines placed via PlaceLayoutResult the
                    // LogicalLineItem carries no InlineItem, so recover the element
                    // from the laid-out fragment; without it the box cannot be
                    // matched back to its DOM element and never paints.
                    var el = item.InlineItem?.Element ?? atomicFragment?.Element;
                    float atomicBlockSize = atomicFragment != null && atomicFragment.BlockSize > 0
                        ? atomicFragment.BlockSize
                        : (item.Size.BlockSize > 0 ? item.Size.BlockSize : Style.FontSize);
                    boxLine.Runs.Add(new BoxRun
                    {
                        Element = el,
                        InlineOffset = runInline,
                        InlineSize = itemInlineSize,
                        BlockOffset = _currentLineBlockOffset,
                        BlockSize = atomicBlockSize,
                        IsAtomicInline = true,
                        // Carry the atomic inline's own fragment so the converter
                        // can turn it into a paintable box (background/border/text).
                        AtomicInlineBox = atomicFragment,
                    });
                }
                else
                {
                    var text = item.TextContent ?? item.InlineItem?.TextContent() ?? "";
                    if (text.Length > 0
                        && item.InlineItem != null
                        && item.InlineItem.Type != InlineItem.InlineItemType.Text
                        && text.IndexOf(Character.kObjectReplacementCharacter) >= 0)
                    {
                        // The U+FFFC here is the internal object-replacement
                        // placeholder of an inline item (atomic inline / float /
                        // out-of-flow positioned), laid out as a replaced or
                        // control unit. It is layout bookkeeping, not user text,
                        // and must never be painted as a glyph.
                        text = "";
                    }
                    if (text.Length > 0)
                    {
                        // The painted text run must carry its TextNode so the
                        // renderer (DrawInlineRuns) can paint it and resolve the
                        // per-text-node style/selection state. BaselineOffset 0
                        // keeps the run on the line box's absolute baseline.
                        boxLine.Runs.Add(new BoxRun
                        {
                            Text = text,
                            Node = item.InlineItem?.GetLayoutObject()?.Node,
                            InlineOffset = runInline,
                            InlineSize = itemInlineSize,
                            BlockOffset = _currentLineBlockOffset,
                            BlockSize = Math.Max(Style.FontSize, item.Size.BlockSize),
                            BaselineOffset = 0
                        });
                        itemsBuilder.Add(new FragmentItem(FragmentItem.ItemType.Text)
                        {
                            Text = text,
                            Offset = new PhysicalOffset(item.Rect.InlineStart, 0),
                            Size = new PhysicalSize(itemInlineSize, boxLine.BlockSize),
                        });
                    }
                }
            }

            _lines.Add(boxLine);

            // Grow the line box to fit any atomic inlines (inline-block, etc.)
            // taller than the text strut. A baseline-aligned atomic inline sits
            // with its border-box bottom on the line's baseline, so it contributes
            // its full height above the baseline. The line's ascent therefore
            // grows to the tallest such box, and the atomic runs are repositioned
            // so their bottom rests on the (possibly lowered) baseline.
            AdjustLineForAtomicInlines(boxLine, lineBlockSize);

            // Also feed via LogicalLineContainer.
            var lineContainer = new LogicalLineContainer();
            foreach (var li in logicalLineItems)
                lineContainer.BaseLine.AddChild(li);
            itemsBuilder.AddLogicalLineContainer(lineContainer, WritingDirectionMode.HorizontalLtr, null);
            _currentLineBlockOffset += boxLine.BlockSize;
        }

        // Attach the flat fragment items list for hit-testing / painting.
        _fragmentItems = itemsBuilder.ToFragmentItems(data.TextContent);
        return true;
    }

    /// <summary>
    /// Enlarge a line box so atomic inlines that are taller than the text strut
    /// fit, and place each atomic run according to its 'vertical-align'. Only the
    /// common cases (baseline, top, middle, bottom) are handled; anything else
    /// falls back to baseline.
    /// </summary>
    private void AdjustLineForAtomicInlines(BoxLine boxLine, float strutHeight)
    {
        float maxAtomicHeight = 0;
        bool hasAtomic = false;
        foreach (var run in boxLine.Runs)
        {
            if (run.IsAtomicInline)
            {
                hasAtomic = true;
                if (run.BlockSize > maxAtomicHeight) maxAtomicHeight = run.BlockSize;
            }
        }
        if (!hasAtomic) return;

        float strutAscent = Fonts.LineBoxMetrics.GetBaselineForLineHeight(Style, strutHeight);
        float strutDescent = strutHeight - strutAscent;

        // A baseline-aligned box needs `height` above the baseline. The line's
        // ascent is the greater of the text ascent and the tallest such box.
        float lineAscent = Math.Max(strutAscent, maxAtomicHeight);
        float lineHeight = lineAscent + strutDescent;

        float lineTopToBaseline = lineAscent;
        boxLine.BlockSize = lineHeight;
        boxLine.BaselineOffset = boxLine.BlockOffset + lineTopToBaseline;

        // Reposition every run relative to the (possibly moved) baseline.
        foreach (var run in boxLine.Runs)
        {
            if (!run.IsAtomicInline)
                continue;

            var valign = run.Element?.ComputedStyle?.VerticalAlign ?? VerticalAlignType.Baseline;
            float top = valign switch
            {
                VerticalAlignType.Top => 0f,
                VerticalAlignType.Bottom => lineHeight - run.BlockSize,
                VerticalAlignType.Middle => (lineHeight - run.BlockSize) / 2f,
                // baseline / text-top / text-bottom / sub / super fall back to
                // sitting the box bottom on the baseline.
                _ => lineTopToBaseline - run.BlockSize,
            };
            run.BlockOffset = boxLine.BlockOffset + top;
        }
    }

    private BoxLine ProcessText(TextNode textNode, BoxLine currentLine, float availInline,
        ref float curInlineSize, ref float curBlockSize, ref float curBaseline, ref float maxBlockSize)
    {
        var text = textNode.Data ?? "";
        if (string.IsNullOrEmpty(text)) return currentLine;

        var style = textNode.ParentElement?.ComputedStyle;
        float fontSize = style?.FontSize ?? 16;
        float charWidth = fontSize * 0.5f;
        var textStrut = Fonts.LineBoxMetrics.GetStrut(style);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n' || (c == ' ' && DoesLineFit(text, i, charWidth, curInlineSize, availInline)))
            {
                if (curInlineSize > 0)
                {
                    currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
                    currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
                }
                curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
                _currentLineBlockOffset += Math.Max(fontSize, maxBlockSize);
                currentLine = new BoxLine { InlineOffset = 0, BlockOffset = _currentLineBlockOffset };
                maxBlockSize = 0;
                if (c == '\n') continue;
            }

            float w = c == ' ' ? charWidth * 0.5f : charWidth;
            if (curInlineSize + w > availInline && curInlineSize > 0)
            {
                currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
                currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
                curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
                _currentLineBlockOffset += Math.Max(fontSize, maxBlockSize);
                currentLine = new BoxLine { InlineOffset = 0, BlockOffset = _currentLineBlockOffset };
                maxBlockSize = 0;
            }

            curInlineSize += w;
            curBlockSize = Math.Max(curBlockSize, textStrut.LineHeight);
            curBaseline = textStrut.Ascent;
            maxBlockSize = Math.Max(maxBlockSize, textStrut.LineHeight);

            currentLine.Runs.Add(new BoxRun
            {
                Text = c.ToString(),
                InlineOffset = curInlineSize - w,
                InlineSize = w,
                BlockOffset = _currentLineBlockOffset,
                BlockSize = textStrut.LineHeight,
                BaselineOffset = textStrut.Ascent,
                IsBreakOpportunity = c == ' '
            });
        }
        return currentLine;
    }

    private BoxLine ProcessElement(Element child, BoxLine currentLine, float availInline,
        ref float curInlineSize, ref float curBlockSize, ref float curBaseline, ref float maxBlockSize)
    {
        var style = child.ComputedStyle;
        if (style == null || style.Display == DisplayType.None) return currentLine;

        float fs = style.FontSize;
        float w = style.Width is PixelLength pl ? pl.Value : 20;
        float h = style.Height is PixelLength ph ? ph.Value : fs;

        if (curInlineSize + w > availInline && curInlineSize > 0)
        {
            currentLine.InlineSize = curInlineSize; currentLine.BlockSize = curBlockSize;
            currentLine.BaselineOffset = curBaseline; _lines.Add(currentLine);
            curInlineSize = 0; curBlockSize = 0; curBaseline = 0;
            _currentLineBlockOffset += Math.Max(fs, maxBlockSize);
            currentLine = new BoxLine { InlineOffset = 0, BlockOffset = _currentLineBlockOffset };
            maxBlockSize = 0;
        }

        curInlineSize += w; curBlockSize = Math.Max(curBlockSize, h);
        // An atomic inline sits on the line with its bottom margin edge on the
        // baseline (CSS 2.1 10.8.1), so its baseline offset is its own height.
        curBaseline = Math.Max(curBaseline, h); maxBlockSize = Math.Max(maxBlockSize, h);

        currentLine.Runs.Add(new BoxRun
        {
            Element = child, InlineOffset = curInlineSize - w, InlineSize = w,
            BlockOffset = _currentLineBlockOffset, BlockSize = h, IsAtomicInline = true
        });
        return currentLine;
    }

    private static bool DoesLineFit(string text, int pos, float charWidth, float curInline, float avail) =>
        pos > 0 && text[pos - 1] != ' ' && curInline + charWidth > avail;

    /// <summary>
    /// Places a block-level box nested inside an inline formatting context.
    /// Mirrors InlineLayoutAlgorithm::PlaceBlockInInline().
    /// </summary>
    public void PlaceBlockInInline(InlineItem item, InlineItemResult itemResult, LogicalLineItems lineBox)
    {
        lineBox.AddChild(item.GetLayoutObject(), item.BidiLevel, itemResult.Start());
    }

    private float fontSize => Style.FontSize;
}