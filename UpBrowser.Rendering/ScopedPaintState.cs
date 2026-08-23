using SkiaSharp;

namespace UpBrowser.Rendering;

/// <summary>
/// Object-local paint state translated from scoped_paint_state.cc.
/// Tracks the paint offset and cull rect while balancing display-list state
/// operations in reverse order when the scope ends.
/// </summary>
internal sealed class ScopedPaintState : IDisposable
{
    private readonly DisplayList _displayList;
    private readonly List<StateType> _states = new();
    private bool _disposed;

    public SKPoint PaintOffset { get; }
    public SKRect CullRect { get; private set; }

    public ScopedPaintState(DisplayList displayList, SKPoint paintOffset, SKRect cullRect)
    {
        _displayList = displayList;
        PaintOffset = paintOffset;
        CullRect = cullRect;
    }

    public bool LocalRectIntersectsCullRect(SKRect localRect)
    {
        localRect.Offset(PaintOffset.X, PaintOffset.Y);
        return CullRect.IntersectsWith(localRect);
    }

    public void PushLayer(float opacity, SKImageFilter? imageFilter, SKPath? clipPath, SKRect bounds, SKImage? maskImage = null, SKBlendMode blendMode = SKBlendMode.SrcOver)
    {
        var op = PaintOpPool.GetPushLayerOp();
        op.Opacity = opacity;
        op.ImageFilter = imageFilter;
        op.ClipPath = clipPath;
        op.MaskImage = maskImage;
        op.BlendMode = blendMode;
        op.Bounds = bounds;
        _displayList.Add(op);
        _states.Add(StateType.Layer);
    }

    public bool PushTransform(SKMatrix matrix, SKRect bounds)
    {
        if (matrix.IsIdentity)
            return false;

        var op = PaintOpPool.GetPushTransformOp();
        op.Matrix = matrix;
        op.Bounds = bounds;
        _displayList.Add(op);
        _states.Add(StateType.Transform);
        return true;
    }

    public bool PushClip(SKRect clipRect)
    {
        if (clipRect.Width <= 0 || clipRect.Height <= 0)
            return false;

        var op = PaintOpPool.GetPushClipOp();
        op.ClipRect = clipRect;
        op.Bounds = clipRect;
        _displayList.Add(op);
        _states.Add(StateType.Clip);
        CullRect.Intersect(clipRect);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (int i = _states.Count - 1; i >= 0; i--)
        {
            PaintOp op = _states[i] switch
            {
                StateType.Layer => PaintOpPool.GetPopLayerOp(),
                StateType.Clip => PaintOpPool.GetPopClipOp(),
                _ => PaintOpPool.GetPopTransformOp(),
            };
            _displayList.Add(op);
        }

        _disposed = true;
    }

    private enum StateType
    {
        Layer,
        Transform,
        Clip,
    }
}
