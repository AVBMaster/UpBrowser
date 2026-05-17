using SkiaSharp;
using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout;

namespace UpBrowser.Rendering;

/// <summary>
/// PaintLayer - manages stacking contexts and z-ordering for painting.
/// Inspired by Blink's PaintLayer and cc/layers architecture.
/// Creates a proper paint order based on CSS stacking context rules.
/// </summary>
public class PaintLayer
{
    public Element Element { get; }
    public LayoutBox LayoutBox { get; }
    public ComputedStyle Style { get; }
    public List<PaintLayer> Children { get; } = new();
    public PaintLayer? Parent { get; set; }
    public int ZIndex { get; set; }
    public bool IsStackingContext { get; set; }
    public bool IsTransparent { get; set; }
    public bool HasClip { get; set; }
    public SKRect ClipRect { get; set; }
    public float Opacity { get; set; } = 1.0f;

    public PaintLayer(Element element, LayoutBox layoutBox, ComputedStyle style)
    {
        Element = element;
        LayoutBox = layoutBox;
        Style = style;
        ZIndex = style.ZIndex ?? 0;
        Opacity = style.Opacity;
        IsTransparent = Opacity < 1.0f;

        var overflow = style.Overflow;
        var overflowX = style.OverflowX;
        var overflowY = style.OverflowY;
        HasClip = overflow == OverflowType.Hidden ||
                  overflowX == OverflowType.Hidden ||
                  overflowY == OverflowType.Hidden;

        if (HasClip)
        {
            ClipRect = layoutBox.PaddingBox;
        }

        IsStackingContext = CreatesStackingContext(style);
    }

    private static bool CreatesStackingContext(ComputedStyle style)
    {
        if (style.Position == PositionType.Absolute || style.Position == PositionType.Fixed)
            return true;
        if (style.Position == PositionType.Relative || style.Position == PositionType.Sticky)
            return style.ZIndex.HasValue;
        if (style.Opacity < 1.0f)
            return true;
        if (style.Transform != null && style.Transform != "none")
            return true;
        return false;
    }

    public void AddChild(PaintLayer child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}

/// <summary>
/// PaintLayerTree - builds and manages the layer tree for proper paint ordering.
/// </summary>
public class PaintLayerTree
{
    private PaintLayer? _rootLayer;
    private readonly List<PaintLayer> _allLayers = new();

    public PaintLayer? RootLayer => _rootLayer;

    public void Build(Document document)
    {
        _allLayers.Clear();
        var root = document.DocumentElement ?? document.Body;
        if (root == null || root.LayoutBox == null || root.ComputedStyle == null) return;

        _rootLayer = new PaintLayer(root, root.LayoutBox, root.ComputedStyle);
        _allLayers.Add(_rootLayer);

        BuildChildren(root, _rootLayer);
    }

    private void BuildChildren(Element parent, PaintLayer parentLayer)
    {
        var childLayers = new List<PaintLayer>();
        var nonPositionedLayers = new List<PaintLayer>();

        foreach (var child in parent.Children)
        {
            if (child is not Element childElement) continue;
            if (childElement.LayoutBox == null || childElement.ComputedStyle == null) continue;
            if (childElement.ComputedStyle.Display == DisplayType.None) continue;

            var layer = new PaintLayer(childElement, childElement.LayoutBox, childElement.ComputedStyle);
            _allLayers.Add(layer);

            if (layer.IsStackingContext || childElement.ComputedStyle.Position != PositionType.Static)
                childLayers.Add(layer);
            else
                nonPositionedLayers.Add(layer);

            BuildChildren(childElement, layer);
        }

        foreach (var layer in nonPositionedLayers)
            parentLayer.AddChild(layer);

        foreach (var layer in childLayers.OrderBy(l => l.ZIndex))
            parentLayer.AddChild(layer);
    }

    /// <summary>
    /// Get paint order - returns layers in the correct CSS stacking context order.
    /// </summary>
    public List<PaintLayer> GetPaintOrder()
    {
        var order = new List<PaintLayer>();
        if (_rootLayer == null) return order;

        CollectPaintOrder(_rootLayer, order);
        return order;
    }

    private void CollectPaintOrder(PaintLayer layer, List<PaintLayer> order)
    {
        var negativeZ = layer.Children.Where(c => c.ZIndex < 0).OrderBy(c => c.ZIndex).ToList();
        var autoZ = layer.Children.Where(c => c.ZIndex == 0).ToList();
        var positiveZ = layer.Children.Where(c => c.ZIndex > 0).OrderBy(c => c.ZIndex).ToList();

        foreach (var child in negativeZ)
            CollectPaintOrder(child, order);

        order.Add(layer);

        foreach (var child in autoZ)
            CollectPaintOrder(child, order);

        foreach (var child in positiveZ)
            CollectPaintOrder(child, order);
    }

    public PaintLayer? FindLayerAtPoint(float x, float y)
    {
        var paintOrder = GetPaintOrder();

        for (int i = paintOrder.Count - 1; i >= 0; i--)
        {
            var layer = paintOrder[i];
            if (layer.LayoutBox.BorderBox.Contains(x, y))
                return layer;
        }

        return null;
    }

    public void Clear()
    {
        _allLayers.Clear();
        _rootLayer = null;
    }
}

/// <summary>
/// PaintPropertyTree - manages transform, clip, and effect trees for compositing.
/// Inspired by Blink's PaintPropertyTreeBuilder.
/// </summary>
public class PaintPropertyTree
{
    private readonly List<TransformNode> _transformNodes = new();
    private readonly List<ClipNode> _clipNodes = new();
    private readonly List<EffectNode> _effectNodes = new();

    public TransformNode RootTransform { get; }
    public ClipNode RootClip { get; }
    public EffectNode RootEffect { get; }

    public PaintPropertyTree()
    {
        RootTransform = new TransformNode { Id = 0, ParentId = -1 };
        RootClip = new ClipNode { Id = 0, ParentId = -1, TransformId = 0 };
        RootEffect = new EffectNode { Id = 0, ParentId = -1, TransformId = 0, ClipId = 0 };

        _transformNodes.Add(RootTransform);
        _clipNodes.Add(RootClip);
        _effectNodes.Add(RootEffect);
    }

    public int AddTransform(SKMatrix matrix, int parentId = 0)
    {
        var node = new TransformNode
        {
            Id = _transformNodes.Count,
            ParentId = parentId,
            Matrix = matrix
        };
        _transformNodes.Add(node);
        return node.Id;
    }

    public int AddClip(SKRect rect, int transformId = 0, int parentId = 0)
    {
        var node = new ClipNode
        {
            Id = _clipNodes.Count,
            ParentId = parentId,
            TransformId = transformId,
            ClipRect = rect
        };
        _clipNodes.Add(node);
        return node.Id;
    }

    public int AddEffect(float opacity, int transformId = 0, int clipId = 0, int parentId = 0)
    {
        var node = new EffectNode
        {
            Id = _effectNodes.Count,
            ParentId = parentId,
            TransformId = transformId,
            ClipId = clipId,
            Opacity = opacity
        };
        _effectNodes.Add(node);
        return node.Id;
    }

    public TransformNode? GetTransform(int id) => _transformNodes.FirstOrDefault(n => n.Id == id);
    public ClipNode? GetClip(int id) => _clipNodes.FirstOrDefault(n => n.Id == id);
    public EffectNode? GetEffect(int id) => _effectNodes.FirstOrDefault(n => n.Id == id);

    public void Clear()
    {
        _transformNodes.Clear();
        _clipNodes.Clear();
        _effectNodes.Clear();
        _transformNodes.Add(RootTransform);
        _clipNodes.Add(RootClip);
        _effectNodes.Add(RootEffect);
    }
}

public class TransformNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public SKMatrix Matrix { get; set; } = SKMatrix.Identity;
}

public class ClipNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int TransformId { get; set; }
    public SKRect ClipRect { get; set; }
}

public class EffectNode
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public int TransformId { get; set; }
    public int ClipId { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public SKColorFilter? ColorFilter { get; set; }
}
