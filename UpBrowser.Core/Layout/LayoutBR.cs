using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

public class LayoutBR : LayoutText
{
    public LayoutBR(HTMLBRElement node) : base(node, "\n")
    {
    }

    public override string GetName() => "LayoutBR";

    public override bool IsBR => true;

    public override int CaretMinOffset() => 0;
    public override int CaretMaxOffset() => 1;

    public PositionWithAffinity? PositionForPoint(PhysicalOffset point) => null;

    public Position? PositionForCaretOffset(uint offset) => null;

    public uint? CaretOffsetForPosition(Position? position) => null;

    protected override uint NonCollapsedCaretMaxOffset() => 1;
}