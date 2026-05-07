using SkiaSharp;

namespace UpBrowser.Rendering;

public class ChromeRenderer
{
    private const float UrlBarHeight = 30;
    private const float TabBarHeight = 35;
    private const float ToolbarHeight = 40;
    private const float StatusBarHeight = 20;
    private const float BorderRadius = 4;

    private SKPaint _backgroundPaint = null!;
    private SKPaint _borderPaint = null!;
    private SKPaint _textPaint = null!;
    private SKPaint _buttonPaint = null!;
    private SKPaint _urlBarPaint = null!;
    private SKPaint _urlTextPaint = null!;
    private SKPaint _buttonHoverPaint = null!;
    private SKPaint _buttonActivePaint = null!;
    private SKTypeface? _chineseTypeface;

    // 缓存的 paint 对象
    private SKPaint _cachedTabActivePaint = null!;
    private SKPaint _cachedTabInactivePaint = null!;
    private SKPaint _cachedLockPaint = null!;
    private SKPaint _cachedBrowserPaint = null!;
    private SKPaint _cachedInfoPaint = null!;
    private SKPaint _cachedClosePaint = null!;
    private SKPaint _cachedNewTabPaint = null!;
    private SKPaint _cachedTitlePaint = null!;
    private SKPaint _cachedStatusPaint = null!;
    private SKPaint _cachedSymbolPaint = null!;
    private SKPaint _cachedCursorPaint = null!;

    private bool _backHovered;
    private bool _forwardHovered;
    private bool _refreshHovered;
    private bool _homeHovered;
    private bool _urlBarFocused;

    private SKRect _backButtonRect;
    private SKRect _forwardButtonRect;
    private SKRect _refreshButtonRect;
    private SKRect _homeButtonRect;
    private SKRect _urlBarRect;

    private List<string> _history = new();
    private int _historyIndex = -1;

    private List<TabInfo> _tabs = new();
    private int _activeTabIndex = 0;

    private string _currentUrl = "";
    private string _urlBarText = "";
    private int _cursorPosition = 0;
    private bool _showCursor = true;
    private DateTime _lastCursorBlink = DateTime.Now;

    public Action<string>? OnNavigate { get; set; }
    public Action? OnRefresh { get; set; }
    public Action? OnBack { get; set; }
    public Action? OnForward { get; set; }
    public Action? OnHome { get; set; }

    public class TabInfo
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "";
        public bool IsActive { get; set; }
    }

    private SKTypeface? GetChineseTypeface()
    {
        var fontFamilies = SKFontManager.Default.FontFamilies.ToArray();
        string[] chineseFonts = { "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "FangSong", "KaiTi", "Segoe UI" };

        foreach (var fontName in chineseFonts)
        {
            var index = Array.IndexOf(fontFamilies, fontName);
            if (index >= 0)
            {
                var tf = SKFontManager.Default.GetFontStyles(index).CreateTypeface(0);
                if (tf != null && tf.FamilyName != null)
                    return tf;
            }
        }

        return null;
    }

    public void Initialize()
    {
        _chineseTypeface = GetChineseTypeface();

        _backgroundPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _textPaint = new SKPaint { Color = SKColors.Black, TextSize = 13, IsAntialias = true, Typeface = _chineseTypeface };
        _urlTextPaint = new SKPaint { Color = SKColor.Parse("#3C4043"), TextSize = 14, IsAntialias = true, Typeface = _chineseTypeface };
        _buttonPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        _buttonHoverPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill };
        _buttonActivePaint = new SKPaint { Color = SKColor.Parse("#BDC1C6"), Style = SKPaintStyle.Fill };
        _urlBarPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };

        _cachedTabActivePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        _cachedTabInactivePaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill };
        _cachedLockPaint = new SKPaint { Color = SKColor.Parse("#34A853"), TextSize = 14, Typeface = _chineseTypeface };
        _cachedBrowserPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), TextSize = 14, Typeface = _chineseTypeface };
        _cachedInfoPaint = new SKPaint { Color = SKColor.Parse("#F9AB00"), TextSize = 14, Typeface = _chineseTypeface };
        _cachedClosePaint = new SKPaint { Color = SKColor.Parse("#80868B"), TextSize = 12, Typeface = _chineseTypeface };
        _cachedNewTabPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), TextSize = 20, Typeface = _chineseTypeface };
        _cachedTitlePaint = new SKPaint { Color = SKColor.Parse("#202124"), TextSize = 12, Typeface = _chineseTypeface };
        _cachedStatusPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), TextSize = 11, Typeface = _chineseTypeface };
        _cachedSymbolPaint = new SKPaint { Color = SKColor.Parse("#3C4043"), TextSize = 14, Typeface = _chineseTypeface, IsAntialias = true };
        _cachedCursorPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill, StrokeWidth = 1 };

        _tabs.Add(new TabInfo { Title = "New Tab", Url = "upbrowser://newtab", IsActive = true });
    }

    public void RenderChrome(SKCanvas canvas, float width, float height, string url, string title)
    {
        _currentUrl = url;

        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].Title = title;
            _tabs[_activeTabIndex].Url = url;
        }

        RenderTabBar(canvas, width, title);
        RenderToolbar(canvas, width, url);
        RenderStatusBar(canvas, width, height);
    }

    private void RenderTabBar(SKCanvas canvas, float width, string title)
    {
        canvas.DrawRect(0, 0, width, TabBarHeight, _backgroundPaint);

        float controlArea = 135;
        float tabsArea = width - controlArea - 80;
        float tabX = 5;
        float tabY = 5;
        float tabHeight = TabBarHeight - 5;

        for (int i = 0; i < _tabs.Count && tabX < tabsArea; i++)
        {
            var tab = _tabs[i];
            float tabWidth = Math.Min(180, tabsArea / _tabs.Count);
            var tabRect = new SKRect(tabX, tabY, tabX + tabWidth, tabY + tabHeight);

            var tabPaint = (i == _activeTabIndex) ? _cachedTabActivePaint : _cachedTabInactivePaint;
            canvas.DrawRoundRect(tabRect, new SKSize(BorderRadius, BorderRadius), tabPaint);

            string tabTitle = tab.Title;
            if (tabTitle.Length > 12)
                tabTitle = tabTitle[..12] + "...";

            _textPaint.Color = i == _activeTabIndex ? SKColor.Parse("#1A73E8") : SKColor.Parse("#5F6368");
            canvas.DrawText(tabTitle, tabRect.Left + 10, tabRect.Top + tabHeight * 0.6f, _textPaint);

            canvas.DrawText("×", tabRect.Right - 18, tabRect.Top + tabHeight * 0.6f, _cachedClosePaint);

            tabX += tabWidth + 2;
        }

        canvas.DrawText("+", tabX + 5, tabY + tabHeight * 0.3f + 5, _cachedNewTabPaint);
        canvas.DrawText("UpBrowser", width - controlArea + 5, TabBarHeight * 0.6f, _cachedTitlePaint);
    }

    private void RenderToolbar(SKCanvas canvas, float width, string url)
    {
        float toolbarTop = TabBarHeight;
        float toolbarCenter = toolbarTop + ToolbarHeight / 2;

        canvas.DrawRect(0, toolbarTop, width, ToolbarHeight, _backgroundPaint);

        float btnY = toolbarTop + 8;
        float btnSize = 24;

        _backButtonRect = new SKRect(10, btnY, 10 + btnSize, btnY + btnSize);
        RenderNavButton(canvas, _backButtonRect, "◀", _backHovered, CanGoBack());

        _forwardButtonRect = new SKRect(40, btnY, 40 + btnSize, btnY + btnSize);
        RenderNavButton(canvas, _forwardButtonRect, "▶", _forwardHovered, CanGoForward());

        _refreshButtonRect = new SKRect(70, btnY, 70 + btnSize, btnY + btnSize);
        RenderNavButton(canvas, _refreshButtonRect, "⟳", _refreshHovered, true);

        _homeButtonRect = new SKRect(100, btnY, 100 + btnSize, btnY + btnSize);
        RenderNavButton(canvas, _homeButtonRect, "🏠", _homeHovered, true);

        float urlBarLeft = 135;
        float urlBarWidth = width - urlBarLeft - 10;
        _urlBarRect = new SKRect(urlBarLeft, toolbarTop + 5, urlBarLeft + urlBarWidth, toolbarTop + ToolbarHeight - 5);

        canvas.DrawRoundRect(_urlBarRect, new SKSize(BorderRadius, BorderRadius), _urlBarPaint);
        canvas.DrawRoundRect(_urlBarRect, new SKSize(BorderRadius, BorderRadius), _borderPaint);

        float iconX = urlBarLeft + 10;
        float iconY = toolbarCenter + 5;

        if (url.StartsWith("https"))
            canvas.DrawText("🔒", iconX, iconY, _cachedLockPaint);
        else if (url.StartsWith("upbrowser://"))
            canvas.DrawText("🌐", iconX, iconY, _cachedBrowserPaint);
        else
            canvas.DrawText("ℹ", iconX, iconY, _cachedInfoPaint);

        float textX = iconX + 20;
        float textY = toolbarCenter + 5;

        string displayUrl = _urlBarFocused ? _urlBarText : url;
        if (!_urlBarFocused && displayUrl.Length > 60)
            displayUrl = displayUrl[..60] + "...";

        _urlTextPaint.Color = _urlBarFocused ? SKColor.Parse("#1A73E8") : SKColor.Parse("#3C4043");
        canvas.DrawText(displayUrl, textX, textY, _urlTextPaint);

        if (_urlBarFocused && _showCursor)
        {
            string textBeforeCursor = _urlBarText[..Math.Min(_cursorPosition, _urlBarText.Length)];
            float cursorX = textX + _urlTextPaint.MeasureText(textBeforeCursor);
            canvas.DrawLine(cursorX, iconY - 12, cursorX, iconY + 2, _cachedCursorPaint);
        }
    }

    private void RenderNavButton(SKCanvas canvas, SKRect rect, string symbol, bool hovered, bool enabled)
    {
        var paint = enabled ? (hovered ? _buttonHoverPaint : _buttonPaint) : _buttonPaint;
        canvas.DrawRoundRect(rect, new SKSize(BorderRadius, BorderRadius), paint);

        _cachedSymbolPaint.Color = enabled ? SKColor.Parse("#3C4043") : SKColor.Parse("#BDC1C6");
        float symWidth = _cachedSymbolPaint.MeasureText(symbol);
        canvas.DrawText(symbol,
            rect.Left + (rect.Width - symWidth) / 2,
            rect.Top + rect.Height * 0.7f,
            _cachedSymbolPaint);
    }

    private void RenderStatusBar(SKCanvas canvas, float width, float height)
    {
        float statusTop = height - StatusBarHeight;

        canvas.DrawRect(0, statusTop, width, StatusBarHeight, _backgroundPaint);
        canvas.DrawLine(0, statusTop, width, statusTop, _borderPaint);

        string statusText = string.IsNullOrEmpty(_currentUrl) ? "Ready" : $"Loading {_currentUrl}...";
        canvas.DrawText(statusText, 10, statusTop + StatusBarHeight * 0.7f, _cachedStatusPaint);

        string securityText = _currentUrl.StartsWith("https") ? "🔒 Secure" : "🌐 Not Secure";
        float secWidth = _cachedStatusPaint.MeasureText(securityText);
        canvas.DrawText(securityText, width - secWidth - 10, statusTop + StatusBarHeight * 0.7f, _cachedStatusPaint);
    }

    public void HandleMouseMove(float x, float y)
    {
        _backHovered = _backButtonRect.Contains(x, y);
        _forwardHovered = _forwardButtonRect.Contains(x, y);
        _refreshHovered = _refreshButtonRect.Contains(x, y);
        _homeHovered = _homeButtonRect.Contains(x, y);
    }

    public bool HandleMouseClick(float x, float y)
    {
        if (_urlBarRect.Contains(x, y))
        {
            _urlBarFocused = true;
            _urlBarText = _currentUrl;
            _cursorPosition = _urlBarText.Length;
            return true;
        }

        if (_backButtonRect.Contains(x, y) && CanGoBack())
        {
            _urlBarFocused = false;
            GoBack();
            return true;
        }

        if (_forwardButtonRect.Contains(x, y) && CanGoForward())
        {
            _urlBarFocused = false;
            GoForward();
            return true;
        }

        if (_refreshButtonRect.Contains(x, y))
        {
            _urlBarFocused = false;
            OnRefresh?.Invoke();
            return true;
        }

        if (_homeButtonRect.Contains(x, y))
        {
            _urlBarFocused = false;
            OnHome?.Invoke();
            return true;
        }

        _urlBarFocused = false;
        return false;
    }

    public bool HandleKeyPress(char keyChar, SKKey key)
    {
        if (!_urlBarFocused) return false;

        switch (key)
        {
            case SKKey.Enter:
                NavigateToUrl(_urlBarText);
                _urlBarFocused = false;
                return true;

            case SKKey.Escape:
                _urlBarFocused = false;
                _urlBarText = _currentUrl;
                return true;

            case SKKey.Left:
                if (_cursorPosition > 0) _cursorPosition--;
                return true;

            case SKKey.Right:
                if (_cursorPosition < _urlBarText.Length) _cursorPosition++;
                return true;

            case SKKey.Home:
                _cursorPosition = 0;
                return true;

            case SKKey.End:
                _cursorPosition = _urlBarText.Length;
                return true;

            case SKKey.Backspace:
                if (_cursorPosition > 0)
                {
                    _urlBarText = _urlBarText[..(_cursorPosition - 1)] + _urlBarText[_cursorPosition..];
                    _cursorPosition--;
                }
                return true;

            case SKKey.Delete:
                if (_cursorPosition < _urlBarText.Length)
                    _urlBarText = _urlBarText[.._cursorPosition] + _urlBarText[(_cursorPosition + 1)..];
                return true;

            default:
                if (!char.IsControl(keyChar))
                {
                    _urlBarText = _urlBarText[.._cursorPosition] + keyChar + _urlBarText[_cursorPosition..];
                    _cursorPosition++;
                }
                return true;
        }
    }

    public void NavigateToUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.Contains("://") && !url.StartsWith("upbrowser://"))
        {
            if (url.Contains('.') || url.Contains('/'))
                url = "https://" + url;
            else
                url = "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
        }

        _currentUrl = url;

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(url);
        _historyIndex = _history.Count - 1;

        OnNavigate?.Invoke(url);
    }

    public void GoBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            _currentUrl = _history[_historyIndex];
            OnBack?.Invoke();
        }
    }

    public void GoForward()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            _currentUrl = _history[_historyIndex];
            OnForward?.Invoke();
        }
    }

    public bool CanGoBack() => _historyIndex > 0;
    public bool CanGoForward() => _historyIndex < _history.Count - 1;

    public void AddTab(string url = "upbrowser://newtab")
    {
        _tabs.Add(new TabInfo { Title = "New Tab", Url = url });
        _activeTabIndex = _tabs.Count - 1;
    }

    public void CloseTab(int index)
    {
        if (_tabs.Count <= 1) return;
        if (index >= 0 && index < _tabs.Count)
        {
            _tabs.RemoveAt(index);
            if (_activeTabIndex >= _tabs.Count)
                _activeTabIndex = _tabs.Count - 1;
        }
    }

    public void SwitchToTab(int index)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            _activeTabIndex = index;
            var tab = _tabs[index];
            NavigateToUrl(tab.Url);
        }
    }

    public bool UpdateCursorBlink()
    {
        if ((DateTime.Now - _lastCursorBlink).TotalMilliseconds > 500)
        {
            _showCursor = !_showCursor;
            _lastCursorBlink = DateTime.Now;
            return true;
        }
        return false;
    }

    public string GetCurrentUrl() => _currentUrl;
    public bool IsUrlBarFocused() => _urlBarFocused;
    public float GetContentOffset() => TabBarHeight + ToolbarHeight;
    public float GetStatusBarHeight() => StatusBarHeight;

    public void RenderScrollbars(SKCanvas canvas, float width, float height, ScrollManager scrollManager)
    {
        float contentTop = GetContentOffset();
        float viewportHeight = scrollManager.ViewportHeight;
        float viewportWidth = scrollManager.ViewportWidth;

        if (scrollManager.CanScrollY && viewportHeight > 0)
        {
            float scrollbarLeft = width - ScrollManager.ScrollbarWidth;
            float trackTop = contentTop;
            float trackHeight = viewportHeight;
            float contentHeight = scrollManager.ContentHeight;

            if (contentHeight <= viewportHeight) return;

            using var trackPaint = new SKPaint { Color = new SKColor(230, 230, 230), Style = SKPaintStyle.Fill };
            canvas.DrawRect(scrollbarLeft, trackTop, ScrollManager.ScrollbarWidth, trackHeight, trackPaint);

            float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackHeight * viewportHeight / contentHeight);

            float maxScrollY = contentHeight - viewportHeight;
            float thumbTop = trackTop;
            if (maxScrollY > 0)
                thumbTop += (scrollManager.ScrollY / maxScrollY) * (trackHeight - thumbHeight);

            var thumbRect = new SKRect(scrollbarLeft + 2, thumbTop,
                scrollbarLeft + ScrollManager.ScrollbarWidth - 2, thumbTop + thumbHeight);
            using var thumbPaint = new SKPaint
            {
                Color = new SKColor(180, 180, 180),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(thumbRect, 4, 4, thumbPaint);
        }

        if (scrollManager.CanScrollX && viewportWidth > 0)
        {
            float contentBottom = height - StatusBarHeight;
            float scrollbarTop = contentBottom - ScrollManager.ScrollbarWidth;
            float trackLeft = 0;
            float trackWidth = viewportWidth;
            float contentWidth = scrollManager.ContentWidth;

            if (contentWidth <= viewportWidth) return;

            using var trackPaint = new SKPaint { Color = new SKColor(230, 230, 230), Style = SKPaintStyle.Fill };
            canvas.DrawRect(trackLeft, scrollbarTop, trackWidth, ScrollManager.ScrollbarWidth, trackPaint);

            float thumbWidth = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackWidth * viewportWidth / contentWidth);

            float maxScrollX = contentWidth - viewportWidth;
            float thumbLeft = trackLeft;
            if (maxScrollX > 0)
                thumbLeft += (scrollManager.ScrollX / maxScrollX) * (trackWidth - thumbWidth);

            var thumbRect = new SKRect(thumbLeft, scrollbarTop + 2,
                thumbLeft + thumbWidth, scrollbarTop + ScrollManager.ScrollbarWidth - 2);
            using var thumbPaint = new SKPaint
            {
                Color = new SKColor(180, 180, 180),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(thumbRect, 4, 4, thumbPaint);
        }
    }

    public void Dispose()
    {
        _backgroundPaint.Dispose();
        _borderPaint.Dispose();
        _textPaint.Dispose();
        _urlTextPaint.Dispose();
        _buttonPaint.Dispose();
        _buttonHoverPaint.Dispose();
        _buttonActivePaint.Dispose();
        _urlBarPaint.Dispose();

        _cachedTabActivePaint.Dispose();
        _cachedTabInactivePaint.Dispose();
        _cachedLockPaint.Dispose();
        _cachedBrowserPaint.Dispose();
        _cachedInfoPaint.Dispose();
        _cachedClosePaint.Dispose();
        _cachedNewTabPaint.Dispose();
        _cachedTitlePaint.Dispose();
        _cachedStatusPaint.Dispose();
        _cachedSymbolPaint.Dispose();
        _cachedCursorPaint.Dispose();
    }
}

public enum SKKey
{
    None,
    Enter,
    Escape,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Backspace,
    Delete,
    Tab,
    Space
}