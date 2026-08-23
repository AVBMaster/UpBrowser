using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Relative positioning utilities. Mirrors relative_utils.cc.
/// </summary>
public static class RelativeUtils
{
    public static LogicalOffset ComputeRelativeOffset(ComputedStyle style, WritingDirectionMode writingDirection, LogicalSize containingBlockSize)
    {
        var offset = new LogicalOffset(0, 0);

        if (style.Position != PositionType.Relative)
            return offset;

        float left = style.Left is PixelLength pl ? pl.Value : 0;
        float right = style.Right is PixelLength pr ? pr.Value : 0;
        float top = style.Top is PixelLength pt ? pt.Value : 0;
        float bottom = style.Bottom is PixelLength pb ? pb.Value : 0;

        // For horizontal writing mode, left/right map to inline, top/bottom to block
        float inline = left - right;
        float block = top - bottom;

        return new LogicalOffset(inline, block);
    }

    public static PhysicalOffset ComputeRelativeOffsetForBoxFragment(Element node, ComputedStyle style, PhysicalSize containingBlockSize)
    {
        if (style.Position != PositionType.Relative)
            return PhysicalOffset.Zero;

        float left = style.Left is PixelLength pl ? pl.Value : 0;
        float top = style.Top is PixelLength pt ? pt.Value : 0;
        return new PhysicalOffset(left, top);
    }

    public static PhysicalOffset ComputeRelativeOffsetForInline(Element node, ConstraintSpace space, ComputedStyle style)
    {
        if (style.Position != PositionType.Relative)
            return PhysicalOffset.Zero;

        float left = style.Left is PixelLength pl ? pl.Value : 0;
        float top = style.Top is PixelLength pt ? pt.Value : 0;
        return new PhysicalOffset(left, top);
    }
}