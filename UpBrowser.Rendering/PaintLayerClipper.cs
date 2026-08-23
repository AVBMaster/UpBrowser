using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

/// <summary>
/// Applies the overflow clips and scroll translations inherited by a paint layer.
/// Mirrors paint_layer_clipper.cc: a layer is clipped by every clipping
/// ancestor, including ancestors that do not create a stacking context, and
/// offset by every scrolled ancestor's scroll origin.
/// </summary>
internal sealed class PaintLayerClipper
{
    private readonly DisplayList _displayList;

    public PaintLayerClipper(DisplayList displayList)
    {
        _displayList = displayList;
    }

    /// <summary>
    /// Pushes ancestor clips and scroll translations for <paramref name="element"/>
    /// and returns the stack of pushed state kinds (outermost first) that must be
    /// popped after painting the layer. The element's own clip/scroll is
    /// intentionally excluded; VisitElement owns it.
    /// </summary>
    public List<bool> PushAncestorStates(Element element, float contentOffsetY)
    {
        // Collect ancestors element → root, then replay root → element so
        // outer clips/transforms nest before inner ones.
        var ancestors = new List<Element>();
        for (var ancestor = element.ParentElement; ancestor != null; ancestor = ancestor.ParentElement)
            ancestors.Add(ancestor);
        ancestors.Reverse();

        var pushed = new List<bool>();
        // Accumulated scroll translation of ancestors already pushed; inner
        // clip rects live in that translated space and must be adjusted.
        float ax = 0, ay = 0;

        foreach (var ancestor in ancestors)
        {
            var style = ancestor.ComputedStyle;
            var box = ancestor.LayoutBox;
            if (style == null || box == null || !CreatesOverflowClip(style))
                continue;

            var clip = new SKRect(
                box.PaddingBox.Left - ax,
                box.PaddingBox.Top + contentOffsetY - ay,
                box.PaddingBox.Right - ax,
                box.PaddingBox.Bottom + contentOffsetY - ay);
            if (clip.Width <= 0 || clip.Height <= 0)
                continue;

            var op = PaintOpPool.GetPushClipOp();
            op.ClipRect = clip;
            op.Bounds = clip;
            _displayList.Add(op);
            pushed.Add(false);

            // A scrolled ancestor paints its contents at natural layout
            // positions; translate them back by the scroll origin so the
            // fragment lands in the visible viewport of the container.
            float sx = box.ScrollX, sy = box.ScrollY;
            if (sx != 0 || sy != 0)
            {
                var t = PaintOpPool.GetPushTransformOp();
                t.Matrix = SKMatrix.CreateTranslation(-sx, -sy);
                t.Bounds = clip;
                _displayList.Add(t);
                pushed.Add(true);
                ax += sx;
                ay += sy;
            }
        }

        return pushed;
    }

    public void Pop(List<bool> pushed, SKRect bounds)
    {
        for (int i = pushed.Count - 1; i >= 0; i--)
        {
            PaintOp op = pushed[i]
                ? PaintOpPool.GetPopTransformOp()
                : PaintOpPool.GetPopClipOp();
            op.Bounds = bounds;
            _displayList.Add(op);
        }
    }

    private static bool CreatesOverflowClip(ComputedStyle style) =>
        style.Overflow == OverflowType.Hidden ||
        style.Overflow == OverflowType.Scroll ||
        style.Overflow == OverflowType.Auto ||
        style.OverflowX == OverflowType.Hidden ||
        style.OverflowX == OverflowType.Scroll ||
        style.OverflowX == OverflowType.Auto ||
        style.OverflowY == OverflowType.Hidden ||
        style.OverflowY == OverflowType.Scroll ||
        style.OverflowY == OverflowType.Auto;
}
