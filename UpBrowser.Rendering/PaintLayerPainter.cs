using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Rendering;

internal sealed class PaintLayerPainter
{
    private readonly PaintVisitor _visitor;
    private readonly DisplayList _displayList;
    private readonly PaintLayer _layer;
    private readonly PaintLayerClipper _clipper;

    public PaintLayerPainter(PaintVisitor visitor, PaintLayer layer)
    {
        _visitor = visitor;
        _displayList = visitor.GetDisplayList();
        _layer = layer;
        _clipper = new PaintLayerClipper(_displayList);
    }

    public void Paint(float contentOffsetY)
    {
        var element = _layer.Element;
        var layoutBox = _layer.LayoutBox;
        var style = _layer.Style;
        if (layoutBox == null || style == null) return;

        // A layered scroll container paints its background statically and its
        // scrollable content via the cached DrawScrollLayerOp (live scroll); its
        // descendant layers are skipped by the tree walk. Everything else keeps
        // the inline paint path.
        bool layered = false;
        if (layoutBox.IsScrollContainer)
        {
            _visitor.EnsureScrollLayer(element, layoutBox, style);
            layered = ScrollLayerCache.IsLayered(layoutBox);
        }

        var pushedStates = _clipper.PushAncestorStates(element, contentOffsetY, _visitor.PhysicalScale);

        PaintLayerBackground(element, layoutBox, style, contentOffsetY);

        if (layered)
        {
            // The scroll-layer op keeps the content visible to direct/snapshot
            // renderers; the tile compositor draws the cached layer LIVE each frame.
            _visitor.EmitScrollLayerOp(layoutBox);
            _clipper.Pop(pushedStates, layoutBox.BorderBox);
            return;
        }

        PaintLayerContent(element, layoutBox, style, contentOffsetY);

        PaintOverflowControls(layoutBox, style, contentOffsetY);

        _clipper.Pop(pushedStates, layoutBox.BorderBox);
    }

    private void PaintLayerBackground(Element element, LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        if (style.Visibility != VisibilityType.Visible) return;
        if (style.Display == DisplayType.Inline) return;

        var offsetBorderBox = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + contentOffsetY);

        bool transfersToView = _visitor.GetCurrentDocument() != null &&
            ViewPainter.BackgroundTransfersToView(element, _visitor.GetCurrentDocument()!);

        if (!transfersToView)
        {
            _visitor.PaintLayerBackgroundFill(element, style, offsetBorderBox);
            _visitor.PaintLayerBorder(element, style, offsetBorderBox);
            _visitor.PaintLayerOutline(element, style, offsetBorderBox);
        }
    }

    private void PaintLayerContent(Element element, LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        if (style.Visibility != VisibilityType.Visible) return;

        var offsetBorderBox = new SKRect(
            box.BorderBox.Left,
            box.BorderBox.Top + contentOffsetY,
            box.BorderBox.Right,
            box.BorderBox.Bottom + contentOffsetY);

        _visitor.PaintLayerContent(element, style, box, offsetBorderBox);
    }

    private void PaintOverflowControls(LayoutBox box, ComputedStyle style, float contentOffsetY)
    {
        if (!box.IsScrollContainer) return;

        bool overflowYScroll = style.OverflowY == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
        bool overflowXScroll = style.OverflowX == OverflowType.Scroll || style.Overflow == OverflowType.Scroll;
        bool needsScrollY = overflowYScroll || box.ScrollContentHeight > box.ContentBox.Height;
        bool needsScrollX = overflowXScroll || box.ScrollContentWidth > box.ContentBox.Width;

        if (needsScrollY || needsScrollX)
        {
            _visitor.PaintLayerScrollbar(box, style, contentOffsetY);
        }
    }
}