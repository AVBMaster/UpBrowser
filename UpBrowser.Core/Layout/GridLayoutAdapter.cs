using UpBrowser.Core.Dom;
using GridImpl = UpBrowser.Core.Layout.Grid.GridLayoutAlgorithm;
using System.Linq;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Adapter exposing the existing Grid layout implementation through the LayoutAlgorithm
/// interface. Produces a BoxFragment tree from the grid container's LayoutBox tree.
/// </summary>
public class GridLayoutAdapter : LayoutAlgorithm
{
    private readonly Func<Element, float, float, float, LayoutBox?, LayoutBox?> _createLayoutBox;
    private readonly LayoutEngine _engine;

    public GridLayoutAdapter(Element node, in ConstraintSpace space,
        Func<Element, float, float, float, LayoutBox?, LayoutBox?>? createLayoutBox = null,
        LayoutEngine? engine = null) : base(node, space)
    {
        _createLayoutBox = createLayoutBox ?? DefaultBoxFactory;
        _engine = engine ?? new LayoutEngine();
    }

    private static LayoutBox DefaultBoxFactory(Element e, float x, float y, float w, LayoutBox? parent)
    {
        var box = new LayoutBox();
        box.MarginBox = new SkiaSharp.SKRect(x, y, x + w, y + 16);
        box.BorderBox = box.MarginBox;
        box.PaddingBox = box.MarginBox;
        box.ContentBox = box.MarginBox;
        return box;
    }

    public override LayoutResult Layout()
    {
        Builder.BorderLeft = BorderLeft;
        Builder.BorderTop = BorderTop;
        Builder.BorderRight = BorderRight;
        Builder.BorderBottom = BorderBottom;
        Builder.PaddingLeft = PaddingLeft;
        Builder.PaddingTop = PaddingTop;
        Builder.PaddingRight = PaddingRight;
        Builder.PaddingBottom = PaddingBottom;
        Builder.Element = Node;

        float availableWidth = ChildAvailableInlineSize;

        var containerBox = Node.LayoutBox ?? _createLayoutBox(Node, PaddingLeft, PaddingTop, availableWidth, null);
        var grid = new global::UpBrowser.Core.Layout.Grid.GridLayoutAlgorithm(TextMeasurer.Instance, _createLayoutBox, _engine);
        grid.Layout(Node, containerBox, availableWidth);

        var box = new BoxFragment
        {
            InlineSize = ComputeInlineSize(),
            BlockSize = ComputeBlockSize(containerBox),
            InlineOffset = 0,
            BlockOffset = 0,
            BorderLeft = BorderLeft,
            BorderTop = BorderTop,
            BorderRight = BorderRight,
            BorderBottom = BorderBottom,
            PaddingLeft = PaddingLeft,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            Element = Node,
        };

        if (containerBox.Children != null)
            CollectChildren(containerBox.Children, Node.Children.OfType<Element>().ToList(), box);

        Builder.InlineSize = box.InlineSize;
        Builder.BlockSize = box.BlockSize;
        Builder.IntrinsicBlockSize = box.BlockSize;

        return LayoutResult.FromFragment(box);
    }

    private static void CollectChildren(IReadOnlyList<LayoutBox> sources, List<Element> domChildren, BoxFragment target)
    {
        for (int i = 0; i < sources.Count && i < domChildren.Count; i++)
        {
            var child = sources[i];
            var childFragment = new BoxFragment
            {
                InlineOffset = child.MarginBox.Left,
                BlockOffset = child.MarginBox.Top,
                InlineSize = child.ContentBox.Width,
                BlockSize = child.ContentBox.Height,
                Element = domChildren[i],
            };
            target.Children.Add(childFragment);
            if (child.Children != null && child.Children.Count > 0)
                CollectChildren(child.Children, domChildren[i].Children.OfType<Element>().ToList(), childFragment);
        }
    }

    private float ComputeInlineSize()
    {
        var style = Style;
        if (style.Width is PixelLength px) return px.Value + BorderPaddingInline;
        return Space.HasDefiniteInlineSize ? Space.AvailableInlineSize : ChildAvailableInlineSize;
    }

    private float ComputeBlockSize(LayoutBox containerBox)
    {
        var style = Style;
        if (style.Height is PixelLength px) return px.Value + BorderPaddingBlock;
        return containerBox.ContentBox.Height + BorderPaddingBlock;
    }
}