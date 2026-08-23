using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout;

public class SimplifiedLayoutAlgorithm : LayoutAlgorithm
{
    public SimplifiedLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }
    public override LayoutResult Layout() => LayoutResult.FromFragment(Builder.ToBoxFragment());
}

public class SimplifiedOofLayoutAlgorithm : LayoutAlgorithm
{
    public SimplifiedOofLayoutAlgorithm(Element node, in ConstraintSpace space) : base(node, space) { }
    public override LayoutResult Layout() => LayoutResult.FromFragment(Builder.ToBoxFragment());
}