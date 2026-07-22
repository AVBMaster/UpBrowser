using SkiaSharp;
using UpBrowser.Core.Dom;

namespace UpBrowser.Rendering;

public class ScrollManager
{
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public float MaxScrollY { get; set; }
    public float MaxScrollX { get; set; }
    public bool HasVerticalScroll { get; set; }
    public bool HasHorizontalScroll { get; set; }
    public float ViewportWidth => _viewportWidth;
    public float ViewportHeight => _viewportHeight;
    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    public bool CanScrollY => MaxScrollY > 0;
    public bool CanScrollX => MaxScrollX > 0;
    public float ScrollableHeight => Math.Max(0, ContentHeight - ViewportHeight);
    public float ScrollableWidth => Math.Max(0, ContentWidth - ViewportWidth);

    public const float ScrollbarWidth = 12;
    public const float ScrollbarMinThumbSize = 20;

    // Physics constants
    private const float DecayLambda = 3.5f;
    private const float WheelVelScale = 3.0f;
    private const float MinVel = 1f;
    private const float BounceK = 150f;
    private const float BounceDamp = 25f;
    private const float SnapK = 60f;
    private const float SnapDamp = 16f;

    // State
    public bool IsSmoothScrollingY { get; private set; }
    public bool IsSmoothScrollingX { get; private set; }
    private float _velY, _velX;
    private bool _bounceY, _bounceX;
    private float _snapTargetY, _snapTargetX;
    private bool _snapY, _snapX;

    private Dictionary<string, ScrollContainer> _scrollContainers = new();
    private float _viewportWidth;
    private float _viewportHeight;

    public void UpdateScroll(float contentWidth, float contentHeight, float viewportWidth, float viewportHeight)
    {
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        MaxScrollX = Math.Max(0, contentWidth - viewportWidth);
        MaxScrollY = Math.Max(0, contentHeight - viewportHeight);

        HasHorizontalScroll = MaxScrollX > 0;
        HasVerticalScroll = MaxScrollY > 0;

        if (!IsSmoothScrollingY)
            ScrollY = Math.Max(0, Math.Min(ScrollY, MaxScrollY));
        if (!IsSmoothScrollingX)
            ScrollX = Math.Max(0, Math.Min(ScrollX, MaxScrollX));
    }

    public void UpdateScroll(float contentHeight, float viewportHeight)
    {
        UpdateScroll(ContentWidth, contentHeight, ViewportWidth, viewportHeight);
    }

    public bool UpdateSmoothScroll(float dt)
    {
        bool moving = false;
        dt = Math.Min(dt, 0.05f);

        // ── Vertical ──
        if (_snapY)
        {
            _snapTargetY = Math.Clamp(_snapTargetY, 0, MaxScrollY);
            float diff = _snapTargetY - ScrollY;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velY) < 1f)
            {
                ScrollY = _snapTargetY;
                _velY = 0;
                _snapY = false;
                IsSmoothScrollingY = false;
            }
            else
            {
                float accel = diff * SnapK - _velY * SnapDamp;
                _velY += accel * dt;
                _velY = Math.Clamp(_velY, -2000f, 2000f);
                ScrollY += _velY * dt;
                if (ScrollY < 0) { _velY *= -0.3f; ScrollY = 0; }
                else if (ScrollY > MaxScrollY) { _velY *= -0.3f; ScrollY = MaxScrollY; }
                moving = true;
            }
        }
        else if (_bounceY)
        {
            float boundary = ScrollY < 0 ? 0 : MaxScrollY;
            float diff = boundary - ScrollY;
            float force = diff * BounceK - _velY * BounceDamp;
            _velY += force * dt;
            _velY *= 0.97f;
            ScrollY += _velY * dt;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velY) < 5f)
            {
                ScrollY = boundary; _velY = 0; _bounceY = false; IsSmoothScrollingY = false;
            }
            else moving = true;
        }
        else if (Math.Abs(_velY) > MinVel)
        {
            _velY *= MathF.Exp(-DecayLambda * dt);
            ScrollY += _velY * dt;
            if (ScrollY < 0) { _bounceY = true; _velY *= 0.5f; }
            else if (ScrollY > MaxScrollY) { _bounceY = true; _velY *= 0.5f; }
            else if (Math.Abs(_velY) < MinVel) { _velY = 0; IsSmoothScrollingY = false; }
            else moving = true;
        }
        else
        {
            IsSmoothScrollingY = false;
        }

        // ── Horizontal ──
        if (_snapX)
        {
            _snapTargetX = Math.Clamp(_snapTargetX, 0, MaxScrollX);
            float diff = _snapTargetX - ScrollX;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velX) < 1f)
            {
                ScrollX = _snapTargetX;
                _velX = 0;
                _snapX = false;
                IsSmoothScrollingX = false;
            }
            else
            {
                float accel = diff * SnapK - _velX * SnapDamp;
                _velX += accel * dt;
                _velX = Math.Clamp(_velX, -2000f, 2000f);
                ScrollX += _velX * dt;
                if (ScrollX < 0) { _velX *= -0.3f; ScrollX = 0; }
                else if (ScrollX > MaxScrollX) { _velX *= -0.3f; ScrollX = MaxScrollX; }
                moving = true;
            }
        }
        else if (_bounceX)
        {
            float boundary = ScrollX < 0 ? 0 : MaxScrollX;
            float diff = boundary - ScrollX;
            float force = diff * BounceK - _velX * BounceDamp;
            _velX += force * dt;
            _velX *= 0.97f;
            ScrollX += _velX * dt;
            if (Math.Abs(diff) < 0.5f && Math.Abs(_velX) < 5f)
            {
                ScrollX = boundary; _velX = 0; _bounceX = false; IsSmoothScrollingX = false;
            }
            else moving = true;
        }
        else if (Math.Abs(_velX) > MinVel)
        {
            _velX *= MathF.Exp(-DecayLambda * dt);
            ScrollX += _velX * dt;
            if (ScrollX < 0) { _bounceX = true; _velX *= 0.5f; }
            else if (ScrollX > MaxScrollX) { _bounceX = true; _velX *= 0.5f; }
            else if (Math.Abs(_velX) < MinVel) { _velX = 0; IsSmoothScrollingX = false; }
            else moving = true;
        }
        else
        {
            IsSmoothScrollingX = false;
        }

        return moving;
    }

    // ── Wheel / impulse scrolling (velocity injection) ──
    public void ScrollBy(float delta, bool smooth = true)
    {
        float scrollAmount = -delta / 120.0f * 60.0f;
        if (smooth)
        {
            _velY += scrollAmount * WheelVelScale;
            _snapY = false;
            _bounceY = false;
            IsSmoothScrollingY = true;
        }
        else
        {
            ScrollY = Math.Clamp(ScrollY + scrollAmount, 0, MaxScrollY);
            _velY = 0;
            IsSmoothScrollingY = false;
        }
    }

    public void ScrollBy(float deltaX, float deltaY, bool smooth = true)
    {
        if (smooth)
        {
            _velX += deltaX;
            _velY += deltaY;
            _snapX = _snapY = false;
            _bounceX = _bounceY = false;
            if (Math.Abs(deltaX) > 0.5f) IsSmoothScrollingX = true;
            if (Math.Abs(deltaY) > 0.5f) IsSmoothScrollingY = true;
        }
        else
        {
            ScrollX = Math.Clamp(ScrollX + deltaX, 0, MaxScrollX);
            ScrollY = Math.Clamp(ScrollY + deltaY, 0, MaxScrollY);
            _velX = _velY = 0;
            IsSmoothScrollingX = IsSmoothScrollingY = false;
        }
    }

    // ── Explicit target scrolling (PageUp/Down, Home/End, arrows) ──
    public void ScrollTo(float y, bool smooth = true)
    {
        float target = Math.Clamp(y, 0, MaxScrollY);
        if (smooth && Math.Abs(target - ScrollY) > 0.5f)
        {
            _snapTargetY = target;
            _velY = 0;
            _snapY = true;
            _bounceY = false;
            IsSmoothScrollingY = true;
        }
        else
        {
            ScrollY = target;
            _velY = 0;
            _snapY = _bounceY = false;
            IsSmoothScrollingY = false;
        }
    }

    public void ScrollTo(float x, float y, bool smooth = true)
    {
        _snapTargetX = Math.Clamp(x, 0, MaxScrollX);
        _snapTargetY = Math.Clamp(y, 0, MaxScrollY);
        if (smooth && (Math.Abs(_snapTargetX - ScrollX) > 0.5f || Math.Abs(_snapTargetY - ScrollY) > 0.5f))
        {
            if (Math.Abs(_snapTargetX - ScrollX) > 0.5f) { _snapX = true; _velX = 0; }
            if (Math.Abs(_snapTargetY - ScrollY) > 0.5f) { _snapY = true; _velY = 0; }
            _bounceX = _bounceY = false;
            IsSmoothScrollingX = _snapX;
            IsSmoothScrollingY = _snapY;
        }
        else
        {
            ScrollX = _snapTargetX; ScrollY = _snapTargetY;
            _velX = _velY = 0;
            _snapX = _snapY = _bounceX = _bounceY = false;
            IsSmoothScrollingX = IsSmoothScrollingY = false;
        }
    }

    public void ScrollToInstant(float y)
    {
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        _velY = 0; _snapY = _bounceY = false; IsSmoothScrollingY = false;
    }

    public void ScrollToInstant(float x, float y)
    {
        ScrollX = Math.Clamp(x, 0, MaxScrollX);
        ScrollY = Math.Clamp(y, 0, MaxScrollY);
        _velX = _velY = 0;
        _snapX = _snapY = _bounceX = _bounceY = false;
        IsSmoothScrollingX = IsSmoothScrollingY = false;
    }

    public void PageUp(bool smooth = true) => ScrollTo(ScrollY - _viewportHeight * 0.85f, smooth);
    public void PageDown(bool smooth = true) => ScrollTo(ScrollY + _viewportHeight * 0.85f, smooth);
    public void PageLeft(bool smooth = true) => ScrollTo(ScrollX - _viewportWidth * 0.85f, ScrollY, smooth);
    public void PageRight(bool smooth = true) => ScrollTo(ScrollX + _viewportWidth * 0.85f, ScrollY, smooth);
    public void ScrollHome(bool smooth = true) => ScrollTo(0f, smooth);
    public void ScrollEnd(bool smooth = true) => ScrollTo(MaxScrollY, smooth);
    public void ScrollToTop(bool smooth = true) => ScrollHome(smooth);
    public void ScrollToBottom(bool smooth = true) => ScrollEnd(smooth);

    public bool HitTestVerticalScrollbar(float x, float y, float containerHeight, float containerWidth)
    {
        if (!HasVerticalScroll) return false;
        float scrollbarLeft = containerWidth - ScrollbarWidth;
        return x >= scrollbarLeft && x <= containerWidth && y >= 0 && y <= containerHeight;
    }

    public bool HitTestHorizontalScrollbar(float x, float y, float containerWidth, float containerHeight)
    {
        if (!HasHorizontalScroll) return false;
        float scrollbarTop = containerHeight - ScrollbarWidth;
        float rightEdge = HasVerticalScroll ? containerWidth - ScrollbarWidth : containerWidth;
        return y >= scrollbarTop && y <= containerHeight && x >= 0 && x <= rightEdge;
    }
}

public class ScrollContainer
{
    public string ElementId { get; set; } = "";
    public float ScrollX { get; set; }
    public float ScrollY { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
    public float ViewportWidth { get; set; }
    public float ViewportHeight { get; set; }
    public OverflowType OverflowX { get; set; }
    public OverflowType OverflowY { get; set; }

    public float MaxScrollX => Math.Max(0, ContentWidth - ViewportWidth);
    public float MaxScrollY => Math.Max(0, ContentHeight - ViewportHeight);
    public bool CanScrollX => OverflowX is OverflowType.Scroll or OverflowType.Auto && MaxScrollX > 0;
    public bool CanScrollY => OverflowY is OverflowType.Scroll or OverflowType.Auto && MaxScrollY > 0;

    public bool SetScroll(float x, float y)
    {
        float newX = Math.Clamp(x, 0, MaxScrollX);
        float newY = Math.Clamp(y, 0, MaxScrollY);
        if (Math.Abs(newX - ScrollX) < 0.5f && Math.Abs(newY - ScrollY) < 0.5f)
            return false;
        ScrollX = newX;
        ScrollY = newY;
        return true;
    }

    public void ScrollBy(float deltaX, float deltaY) => SetScroll(ScrollX + deltaX, ScrollY + deltaY);
}
