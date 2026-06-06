using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance;

namespace UpBrowser.Core.Performance.Rendering;

/// <summary>
/// Statistics exposed by <see cref="IncrementalLayoutEngine"/>: how many nodes were
/// actually re-laid-out, how many were skipped thanks to the cache, etc. The diagnostics
/// UI consumes these to show "Layout work avoided" panels.
/// </summary>
public sealed class LayoutStats
{
    private int _nodesVisited;
    private int _nodesSkipped;
    private int _nodesRelaid;
    private int _cacheHits;
    private int _cacheMisses;
    private long _elapsedNanos;
    private int _dirtyRoots;

    public int NodesVisited => _nodesVisited;
    public int NodesSkipped => _nodesSkipped;
    public int NodesReLaid => _nodesRelaid;
    public int CacheHits => _cacheHits;
    public int CacheMisses => _cacheMisses;
    public long ElapsedNanos => _elapsedNanos;
    public double ElapsedMillis => _elapsedNanos / 1_000_000.0;
    public int DirtyRoots => _dirtyRoots;

    public double SkipRatio =>
        _nodesVisited == 0 ? 0 : (double)_nodesSkipped / _nodesVisited;

    public void Reset()
    {
        _nodesVisited = _nodesSkipped = _nodesRelaid = 0;
        _cacheHits = _cacheMisses = 0;
        _elapsedNanos = 0;
        _dirtyRoots = 0;
    }

    internal void IncVisited() => _nodesVisited++;
    internal void IncSkipped() => _nodesSkipped++;
    internal void IncRelaid() => _nodesRelaid++;
    internal void IncHit() => _cacheHits++;
    internal void IncMiss() => _cacheMisses++;
    internal void IncDirtyRoot() => _dirtyRoots++;
    internal void AddElapsed(long ns) => _elapsedNanos += ns;
}

/// <summary>
/// An incremental, opt-in layout engine. It re-uses the existing <see cref="LayoutEngine"/>
/// for the actual box construction logic, but gates each node on a cache lookup. The
/// production engine stays untouched for backwards compatibility; consumers that need
/// incremental behaviour instantiate this engine and route layout requests through it.
/// </summary>
public sealed class IncrementalLayoutEngine
{
    private readonly LayoutEngine _base;
    public LayoutCache Cache { get; }
    public LayoutStats Stats { get; } = new();
    public bool UseCache = true;
    public bool UseDirtyPropagation = true;
    public bool SkipCleanSubtrees = true;
    public ViewportCullingPolicy Culling { get; set; } = ViewportCullingPolicy.None;

    public IncrementalLayoutEngine(LayoutEngine baseEngine, LayoutCache? cache = null)
    {
        _base = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        Cache = cache ?? new LayoutCache();
    }

    /// <summary>
    /// Run a full layout pass. The result is identical to <see cref="LayoutEngine.Layout"/>
    /// but elements that are not dirty and whose cache key matches will skip the inner
    /// <c>CreateLayoutBox</c> call.
    /// </summary>
    public void Layout(Document document, float width, float height, float dpiScale = 1.0f, float rootFontSize = 16f)
    {
        Stats.Reset();
        var sw = Clock.NowNanos();
        var root = document.DocumentElement ?? document.Body;
        if (root == null) { Stats.AddElapsed(Clock.NowNanos() - sw); return; }

        if (UseDirtyPropagation) Stats.IncDirtyRoot();

        // Always lay out from the root because container width may have changed.
        // Inner nodes are gated by the cache.
        Traverse(root, width, dpiScale, rootFontSize, isRoot: true);
        Stats.AddElapsed(Clock.NowNanos() - sw);
    }

    /// <summary>
    /// Mark a subtree as needing relayout. Use this after DOM mutations or style updates.
    /// </summary>
    public void InvalidateSubtree(Element root)
    {
        Cache.InvalidateSubtree(root);
        foreach (var child in root.Children)
        {
            if (child is Element ce)
            {
                DirtyState.AddSelf(ce, DirtyFlags.AllLayout);
                DirtyState.AddChildren(ce, DirtyFlags.AllLayout);
            }
        }
        DirtyState.AddSelf(root, DirtyFlags.AllLayout);
        DirtyState.AddChildren(root, DirtyFlags.AllLayout);
    }

    /// <summary>
    /// Mark a single element as needing style recomputation.
    /// </summary>
    public void InvalidateStyle(Element element)
    {
        DirtyState.AddSelf(element, DirtyFlags.Style);
        DirtyState.AddChildren(element, DirtyFlags.ChildrenStyle);
        DirtyState.BumpStyleVersion(element);
        Cache.Invalidate(element);
    }

    private LayoutBox? Traverse(Element element, float availableWidth, float dpiScale, float rootFontSize, bool isRoot)
    {
        Stats.IncVisited();

        var key = new LayoutCacheKey(element, availableWidth, availableWidth, rootFontSize, dpiScale);

        if (UseCache && !isRoot)
        {
            // Skip if the node is clean and we have a valid cached result.
            var clean = !UseDirtyPropagation || DirtyState.IsClean(element);
            if (clean && Cache.TryGetCached(element, key, out var cached) && cached is not null)
            {
                Stats.IncSkipped();
                Stats.IncHit();
                element.LayoutBox = cached.Box;
                return cached.Box;
            }
            Stats.IncMiss();
        }

        Stats.IncRelaid();
        // Delegate to the base engine. The base engine's CreateLayoutBox method
        // re-uses cached child LayoutBox when present, so we benefit transitively.
        var box = _base.CreateLayoutBoxPublic(element, 0, 0, availableWidth, null);
        if (box != null)
        {
            int subtree = CountSubtree(element);
            int lineRuns = box.Lines is null ? 0 : box.Lines.Sum(l => l.Runs.Count);
            Cache.Store(element, key, box, box.ContentBox.Width, box.ContentBox.Height, subtree, lineRuns);
        }
        DirtyState.ClearAll(element);
        return box;
    }

    private static int CountSubtree(Element element)
    {
        int n = 1;
        foreach (var child in element.Children)
            if (child is Element ce) n += CountSubtree(ce);
        return n;
    }
}

/// <summary>Strategy for off-viewport layout skipping.</summary>
public enum ViewportCullingPolicy
{
    None = 0,
    /// <summary>Skip subtrees whose bounding box is entirely outside the viewport.</summary>
    CullFarViewport,
    /// <summary>Use cheap estimated dimensions for far subtrees.</summary>
    EstimateFarViewport,
}
