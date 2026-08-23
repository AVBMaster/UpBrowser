using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

public class LayoutHTMLCanvas : LayoutReplaced
{
    private readonly List<LayoutObject> _children = new();

    public LayoutHTMLCanvas(HTMLCanvasElement? element) : base(element)
    {
    }

    public override bool IsCanvas => true;

    public void InvalidatePaint(PaintInvalidatorContext? context)
    {
    }

    public void CanvasSizeChanged()
    {
    }

    public bool DrawsBackgroundOntoContentLayer() => false;

    public void StyleDidChange(StyleDifference diff, ComputedStyle? oldStyle)
    {
    }

    public override string GetName() => "LayoutHTMLCanvas";

    public override void WillBeDestroyed()
    {
    }

    public LayoutObject? FirstChild => _children.Count > 0 ? _children[0] : null;
    public LayoutObject? LastChild => _children.Count > 0 ? _children[^1] : null;

    public override List<LayoutObject> Children => _children;

    public override bool CanHaveChildren => false;

    public override bool IsChildAllowed(LayoutObject? child, ComputedStyle style) => true;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public override void IntrinsicSizeChanged()
    {
        CanvasSizeChanged();
    }
}