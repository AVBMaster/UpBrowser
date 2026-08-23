namespace UpBrowser.Core.Css.Cascade;

/// <summary>CSS cascade origin, mirroring Blink's CascadeOrigin.</summary>
public enum CascadeOrigin
{
    None,
    UserAgent,
    User,
    Author,
    Animation,
    Transition
}

/// <summary>CSS cascade priority: encodes the full cascade order per CSS Cascading 4.</summary>
public readonly struct CascadePriority : IEquatable<CascadePriority>
{
    public readonly CascadeOrigin Origin;
    public readonly int LayerOrder;
    public readonly int TreeOrder;
    public readonly int Position;
    public readonly bool IsImportant;
    public readonly uint Generation;

    public CascadePriority(CascadeOrigin origin, int layerOrder, int treeOrder, int position, bool isImportant, uint generation = 0)
    {
        Origin = origin;
        LayerOrder = layerOrder;
        TreeOrder = treeOrder;
        Position = position;
        IsImportant = isImportant;
        Generation = generation;
    }

    public CascadePriority WithGeneration(uint generation) =>
        new(Origin, LayerOrder, TreeOrder, Position, IsImportant, generation);

    public bool IsRelevant => Origin != CascadeOrigin.None;

    /// <summary>
    /// Compare two priorities per CSS cascade order.
    /// Returns: -1 if a wins, 1 if b wins, 0 if equal.
    /// </summary>
    public static int Compare(CascadePriority a, CascadePriority b)
    {
        if (a.IsImportant != b.IsImportant)
            return a.IsImportant ? 1 : -1;

        int originCmp = OriginRank(a.Origin).CompareTo(OriginRank(b.Origin));
        if (originCmp != 0) return originCmp;

        if (a.LayerOrder != b.LayerOrder)
            return a.LayerOrder.CompareTo(b.LayerOrder);

        if (a.TreeOrder != b.TreeOrder)
            return a.TreeOrder.CompareTo(b.TreeOrder);

        if (a.Position != b.Position)
            return a.Position.CompareTo(b.Position);

        return 0;
    }

    private static int OriginRank(CascadeOrigin origin) => origin switch
    {
        CascadeOrigin.None => -1,
        CascadeOrigin.UserAgent => 0,
        CascadeOrigin.User => 1,
        CascadeOrigin.Author => 2,
        CascadeOrigin.Animation => 3,
        CascadeOrigin.Transition => 4,
        _ => 0
    };

    public bool Equals(CascadePriority other) =>
        Origin == other.Origin && LayerOrder == other.LayerOrder &&
        TreeOrder == other.TreeOrder && Position == other.Position &&
        IsImportant == other.IsImportant && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is CascadePriority p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Origin, LayerOrder, TreeOrder, Position, IsImportant);
    public override string ToString() => $"{Origin}{(IsImportant ? "!" : "")}({LayerOrder},{TreeOrder},{Position})";
}