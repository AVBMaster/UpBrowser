using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using UpBrowser.Core.Layout.Inline;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Static position of an out-of-flow positioned element. Mirrors
/// static_position.h.
/// </summary>
public readonly struct LogicalStaticPosition
{
    public enum StaticInlinePosition
    {
        Left, Center, Right,
    }

    public enum StaticBlockPosition
    {
        Top, Center, Bottom,
    }

    public LogicalOffset Offset { get; }
    public StaticInlinePosition InlinePosition { get; }
    public StaticBlockPosition BlockPosition { get; }
    public WritingDirectionMode WritingDirection { get; }

    public LogicalStaticPosition(LogicalOffset offset, StaticInlinePosition inlinePosition, StaticBlockPosition blockPosition, WritingDirectionMode writingDirection)
    {
        Offset = offset;
        InlinePosition = inlinePosition;
        BlockPosition = blockPosition;
        WritingDirection = writingDirection;
    }
}

/// <summary>
/// Containing block info for an out-of-flow node. Mirrors OofContainingBlock.
/// </summary>
public readonly struct OofContainingBlock
{
    public PhysicalOffset Offset { get; }
    public PhysicalSize Size { get; }

    public OofContainingBlock(PhysicalOffset offset, PhysicalSize size)
    {
        Offset = offset;
        Size = size;
    }
}

/// <summary>
/// A candidate for out-of-flow layout. Mirrors LogicalOofPositionedNode.
/// </summary>
public class OutOfFlowChildCandidate
{
    public LayoutBox Box { get; }
    public LogicalStaticPosition StaticPosition { get; }
    public bool IsAbsolute { get; set; }
    public bool IsFixed { get; set; }
    public bool IsHiddenForPaint { get; set; }
    public bool RequiresContentBeforeBreaking { get; set; }
    public OofInlineContainer<LogicalOffset> InlineContainer { get; set; }

    public OutOfFlowChildCandidate(LayoutBox box, LogicalStaticPosition staticPosition)
    {
        Box = box;
        StaticPosition = staticPosition;
        IsAbsolute = box.Parent != null && !box.IsFloating;
        IsHiddenForPaint = false;
        InlineContainer = OofInlineContainer<LogicalOffset>.Empty;
    }

    public LogicalOofPositionedNode ToLogicalNode() =>
        new(Box, StaticPosition, RequiresContentBeforeBreaking, IsHiddenForPaint, InlineContainer);
}

/// <summary>
/// Helper class for positioning out-of-flow blocks. It should be used together
/// with BoxFragmentBuilder. Mirrors out_of_flow_layout_part.cc.
/// </summary>
public class OutOfFlowLayoutPart
{
    private readonly BoxFragmentBuilder _containerBuilder;
    private readonly List<OutOfFlowChildCandidate> _candidates = new();

    public OutOfFlowLayoutPart(BoxFragmentBuilder containerBuilder)
    {
        _containerBuilder = containerBuilder;
    }

    public void AddCandidate(OutOfFlowChildCandidate candidate) => _candidates.Add(candidate);

    /// <summary>
    /// Position and append all OOF children to the fragment builder. Mirrors
    /// OutOfFlowLayoutPart::Run().
    /// </summary>
    public void Run()
    {
        if (_candidates.Count == 0) return;

        float containerInline = _containerBuilder.InlineSize;
        float containerBlock = _containerBuilder.BlockSize;
        var containerContentSize = new LogicalSize(
            Math.Max(0, containerInline - _containerBuilder.BorderLeft - _containerBuilder.BorderRight - _containerBuilder.PaddingLeft - _containerBuilder.PaddingRight),
            Math.Max(0, containerBlock - _containerBuilder.BorderTop - _containerBuilder.BorderBottom - _containerBuilder.PaddingTop - _containerBuilder.PaddingBottom));

        foreach (var candidate in _candidates)
        {
            if (candidate.IsHiddenForPaint) continue;
            var style = GetStyleOf(candidate.Box);
            if (style == null) continue;

            // Compute the border box size of the OOF box.
            var (inlineSize, blockSize) = AbsoluteUtils.ComputeOutOfFlowSize(style, containerContentSize, new BoxStrut(0, 0, 0, 0));

            // Determine position.
            var (x, y) = AbsoluteUtils.ComputeOutOfFlowPosition(style, inlineSize, blockSize, containerContentSize);

            // Apply static position for auto insets (e.g. in-flow position for
            // "static" placement).
            ApplyStaticPosition(candidate, style, inlineSize, blockSize, containerContentSize, ref x, ref y);

            // Create the fragment for this OOF box.
            var fragment = new BoxFragment
            {
                InlineSize = inlineSize,
                BlockSize = blockSize,
                InlineOffset = x + _containerBuilder.PaddingLeft,
                BlockOffset = y + _containerBuilder.PaddingTop,
                Element = GetElementOf(candidate.Box),
                IsOutOfFlowPositioned = true,
            };

            _containerBuilder.Children.Add(fragment);
        }
    }

    private static void ApplyStaticPosition(OutOfFlowChildCandidate candidate, ComputedStyle style, float inlineSize, float blockSize,
        LogicalSize containerContentSize, ref float x, ref float y)
    {
        var sp = candidate.StaticPosition;

        // If 'left' and 'right' are both auto, use the static position for inline axis.
        if (style.Left is AutoLength && style.Right is AutoLength)
        {
            x = sp.InlinePosition switch
            {
                LogicalStaticPosition.StaticInlinePosition.Left => 0,
                LogicalStaticPosition.StaticInlinePosition.Center => Math.Max(0, (containerContentSize.InlineSize - inlineSize) / 2),
                LogicalStaticPosition.StaticInlinePosition.Right => Math.Max(0, containerContentSize.InlineSize - inlineSize),
                _ => sp.Offset.InlineOffset,
            };
        }

        // If 'top' and 'bottom' are both auto, use the static position for block axis.
        if (style.Top is AutoLength && style.Bottom is AutoLength)
        {
            y = sp.BlockPosition switch
            {
                LogicalStaticPosition.StaticBlockPosition.Top => 0,
                LogicalStaticPosition.StaticBlockPosition.Center => Math.Max(0, (containerContentSize.BlockSize - blockSize) / 2),
                LogicalStaticPosition.StaticBlockPosition.Bottom => Math.Max(0, containerContentSize.BlockSize - blockSize),
                _ => sp.Offset.BlockOffset,
            };
        }
    }

    private static ComputedStyle? GetStyleOf(LayoutBox box) => box.Dimensions?.Style ?? null;
    private static Element? GetElementOf(LayoutBox box) => box.Dimensions?.Element ?? null;
}