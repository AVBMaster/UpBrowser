using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Grid;

public class GridNode
{
    public GridPlacementData CachedPlacementData => new();
    public GridLineResolver CachedLineResolver => new();

    public void InvalidateSubgridMinMaxSizesCache() { }

    public bool ShouldInvalidateSubgridMinMaxSizesCacheFor(GridLayoutData layoutData) => false;

    public GridItems ConstructGridItems(GridLineResolver lineResolver, out bool mustInvalidatePlacementCache)
    {
        mustInvalidatePlacementCache = false;
        return new GridItems();
    }

    public void AppendSubgriddedItems(GridItems gridItems) { }

    public MinMaxSizesResult ComputeSubgridMinMaxSizes(GridSizingSubtree sizingSubtree, ConstraintSpace space) =>
        new(new MinMaxSizes(0, float.MaxValue), false);

    public LayoutUnit ComputeSubgridIntrinsicBlockSize(GridSizingSubtree sizingSubtree, ConstraintSpace space) =>
        LayoutUnit.FromValue(0);
}