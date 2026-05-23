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

        ScrollX = Math.Max(0, Math.Min(ScrollX, MaxScrollX));
        ScrollY = Math.Max(0, Math.Min(ScrollY, MaxScrollY));
    }

    public void UpdateScroll(float contentHeight, float viewportHeight)
    {
        UpdateScroll(ContentWidth, contentHeight, ViewportWidth, viewportHeight);
    }

    public void ScrollTo(float x, float y)
    {
        ScrollX = Math.Max(0, Math.Min(x, MaxScrollX));
        ScrollY = Math.Max(0, Math.Min(y, MaxScrollY));
    }

    public void ScrollTo(float y)
    {
        ScrollY = Math.Max(0, Math.Min(y, MaxScrollY));
    }

    public void ScrollBy(float delta)
    {
        float scrollAmount = -delta / 120.0f * 40.0f;
        ScrollTo(ScrollY + scrollAmount);
    }

    public void ScrollBy(float deltaX, float deltaY)
    {
        ScrollTo(ScrollX + deltaX, ScrollY + deltaY);
    }

    public void PageUp() => ScrollTo(ScrollY - _viewportHeight * 0.9f);
    public void PageDown() => ScrollTo(ScrollY + _viewportHeight * 0.9f);
    public void PageLeft() => ScrollTo(ScrollX - _viewportWidth * 0.9f, ScrollY);
    public void PageRight() => ScrollTo(ScrollX + _viewportWidth * 0.9f, ScrollY);
    public void ScrollHome() => ScrollY = 0;
    public void ScrollEnd() => ScrollY = MaxScrollY;
    public void ScrollToTop() => ScrollY = 0;
    public void ScrollToBottom() => ScrollY = MaxScrollY;

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
        return y >= scrollbarTop && y <= containerHeight && x >= 0 && x <= containerWidth;
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