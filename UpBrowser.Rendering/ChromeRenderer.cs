using SkiaSharp;
using UpBrowser.Core;

namespace UpBrowser.Rendering;

public class ChromeRenderer : IImeSupport
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
    private SKPaint _tabCloseHoverPaint = null!;
    private SKPaint _tabCloseActivePaint = null!;
    private SKTypeface? _chineseTypeface;

    // 缓存的 paint 对象
    private SKPaint _cachedTabActivePaint = null!;
    private SKPaint _cachedTabInactivePaint = null!;
    private SKPaint _cachedLockPaint = null!;
    private SKPaint _cachedBrowserPaint = null!;
    private SKPaint _cachedInfoPaint = null!;
    private SKPaint _cachedNewTabPaint = null!;
    private SKPaint _cachedTitlePaint = null!;
    private SKPaint _cachedStatusPaint = null!;
    private SKPaint _cachedSymbolPaint = null!;
    private SKPaint _cachedCursorPaint = null!;

    private SKFont _textFont = null!;
    private SKFont _urlTextFont = null!;
    private SKFont _cachedLockFont = null!;
    private SKFont _cachedBrowserFont = null!;
    private SKFont _cachedInfoFont = null!;
    private SKFont _cachedNewTabFont = null!;
    private SKFont _cachedTitleFont = null!;
    private SKFont _cachedStatusFont = null!;
    private SKFont _cachedSymbolFont = null!;
    private SKFont _cachedCloseFont = null!;

    // Cached per-frame paints
    private SKPaint _scrollbarTrackPaint = null!;
    private SKPaint _scrollbarThumbPaint = null!;
    private SKPaint _closeBtnHoverPaint = null!;
    private SKPaint _closeBtnHoverBgPaint = null!;

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

    // 标签页相关
    private List<TabInfo> _tabs = new();
    private int _activeTabIndex = 0;
    private List<SKRect> _tabRects = new();       // 每个标签页的矩形区域
    private List<SKRect> _tabCloseRects = new();   // 每个标签页关闭按钮的矩形区域
    private SKRect _newTabButtonRect;              // 新建标签按钮区域
    private int _hoveredTabIndex = -1;             // 鼠标悬停的标签索引
    private int _hoveredCloseIndex = -1;           // 鼠标悬停的关闭按钮索引
    private bool _newTabHovered;                   // 新建标签按钮悬停

    private List<string> _history = new();
    private int _historyIndex = -1;

    private string _currentUrl = "";
    private string _urlBarText = "";
    private int _cursorPosition = 0;
    private bool _showCursor = true;
    private long _lastCursorBlinkTick = Environment.TickCount64;

    private bool _isImeComposing;
    private string _imeCompositionString = "";
    private int _imeCompositionCursor = 0;

    private bool _isLoading;
    private float _loadingProgress;
    private long _loadingStartTime;

    public Action<string>? OnNavigate { get; set; }
    public Action? OnRefresh { get; set; }
    public Action? OnBack { get; set; }
    public Action? OnForward { get; set; }
    public Action? OnHome { get; set; }
    public Action<string>? OnTabChanged { get; set; }      // 标签切换回调
    public Action? OnNewTab { get; set; }                  // 新建标签页回调
    public Action<int>? OnCloseTab { get; set; }           // 关闭标签页回调
    public Action? OnUrlBarFocus { get; set; }    // URL 栏获得焦点回调
    public Action? OnUrlBarBlur { get; set; }    // URL 栏失去焦点回调

    public class TabInfo
    {
        public string Title { get; set; } = "New Tab";
        public string Url { get; set; } = "upbrowser://newtab";
        public bool IsActive { get; set; }
        public bool IsLoading { get; set; }
        public float LoadingProgress { get; set; }
    }

    public void Initialize()
    {
        _chineseTypeface = FontHelper.GetChineseTypeface();

        _backgroundPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        _borderPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        _textFont = new SKFont(_chineseTypeface, 13) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _urlTextPaint = new SKPaint { Color = SKColor.Parse("#3C4043"), IsAntialias = true };
        _urlTextFont = new SKFont(_chineseTypeface, 14) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _buttonPaint = new SKPaint { Color = SKColor.Parse("#E8EAED"), Style = SKPaintStyle.Fill };
        _buttonHoverPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _buttonActivePaint = new SKPaint { Color = SKColor.Parse("#BDC1C6"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _urlBarPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        _tabCloseHoverPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _tabCloseActivePaint = new SKPaint { Color = SKColor.Parse("#BDC1C6"), Style = SKPaintStyle.Fill, IsAntialias = true };

        _cachedTabActivePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        _cachedTabInactivePaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill };
        _cachedLockPaint = new SKPaint { Color = SKColor.Parse("#34A853") };
        _cachedLockFont = new SKFont(_chineseTypeface, 14) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedBrowserPaint = new SKPaint { Color = SKColor.Parse("#1A73E8") };
        _cachedBrowserFont = new SKFont(_chineseTypeface, 14) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedInfoPaint = new SKPaint { Color = SKColor.Parse("#F9AB00") };
        _cachedInfoFont = new SKFont(_chineseTypeface, 14) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedNewTabPaint = new SKPaint { Color = SKColor.Parse("#5F6368"), IsAntialias = true };
        _cachedNewTabFont = new SKFont(_chineseTypeface, 22) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedTitlePaint = new SKPaint { Color = SKColor.Parse("#202124") };
        _cachedTitleFont = new SKFont(_chineseTypeface, 12) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedStatusPaint = new SKPaint { Color = SKColor.Parse("#5F6368") };
        _cachedStatusFont = new SKFont(_chineseTypeface, 11) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedSymbolPaint = new SKPaint { Color = SKColor.Parse("#3C4043"), IsAntialias = true };
        _cachedSymbolFont = new SKFont(_chineseTypeface, 14) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedCloseFont = new SKFont(_chineseTypeface ?? SKTypeface.Default, 11) { Hinting = SKFontHinting.Normal, Edging = SKFontEdging.Antialias };
        _cachedCursorPaint = new SKPaint { Color = SKColor.Parse("#1A73E8"), Style = SKPaintStyle.Fill, StrokeWidth = 1 };

        _scrollbarTrackPaint = new SKPaint { Color = new SKColor(230, 230, 230), Style = SKPaintStyle.Fill };
        _scrollbarThumbPaint = new SKPaint { Color = new SKColor(180, 180, 180), Style = SKPaintStyle.Fill, IsAntialias = true };
        _closeBtnHoverBgPaint = new SKPaint { Color = SKColor.Parse("#DADCE0"), Style = SKPaintStyle.Fill, IsAntialias = true };
        _closeBtnHoverPaint = new SKPaint { Color = SKColor.Parse("#202124"), IsAntialias = true };

        // 默认打开一个新标签页
        _tabs.Add(new TabInfo { Title = "New Tab", Url = "upbrowser://newtab", IsActive = true });
    }

    // ==================== 渲染方法 ====================

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
        // 背景
        canvas.DrawRect(0, 0, width, TabBarHeight, _backgroundPaint);

        // 清除上次的 tab 矩形记录
        _tabRects.Clear();
        _tabCloseRects.Clear();

        float newTabButtonWidth = 36;
        float controlArea = 135;
        float tabsArea = width - controlArea - newTabButtonWidth - 5;

        float tabX = 5;
        float tabY = 5;
        float tabHeight = TabBarHeight - 5;
        float maxTabWidth = 180;

        // 计算每个标签页的宽度
        int tabCount = _tabs.Count;
        float tabWidth = maxTabWidth;
        if (tabCount > 0 && tabX + tabCount * tabWidth > tabsArea)
        {
            tabWidth = Math.Max(80, (tabsArea - tabX) / tabCount);
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (tabX + tabWidth > tabsArea)
                tabWidth = tabsArea - tabX;
            if (tabWidth < 40) break;

            var tab = _tabs[i];
            var tabRect = new SKRect(tabX, tabY, tabX + tabWidth, tabY + tabHeight);
            _tabRects.Add(tabRect);

            // 活动标签页稍微高一点
            float actualTabY = (i == _activeTabIndex) ? tabY - 1 : tabY;
            float actualTabHeight = (i == _activeTabIndex) ? tabHeight + 1 : tabHeight;

            var drawRect = new SKRect(tabX, actualTabY, tabX + tabWidth, actualTabY + actualTabHeight);

            // 标签背景
            SKPaint tabBgPaint;
            if (i == _activeTabIndex)
            {
                tabBgPaint = _cachedTabActivePaint;
            }
            else if (i == _hoveredTabIndex && i != _activeTabIndex)
            {
                tabBgPaint = _buttonHoverPaint;
            }
            else
            {
                tabBgPaint = _cachedTabInactivePaint;
            }

            canvas.DrawRoundRect(drawRect, new SKSize(BorderRadius, BorderRadius), tabBgPaint);

            // 标签底部不显示圆角（与工具栏连接）
            if (i == _activeTabIndex)
            {
                canvas.DrawRect(drawRect.Left, drawRect.Bottom - BorderRadius, drawRect.Width, BorderRadius, _cachedTabActivePaint);
            }

            // 标签标题
            string tabTitle = tab.Title;
            float closeButtonWidth = 20;
            float textMaxWidth = tabWidth - 25 - closeButtonWidth;

            _textFont.Size = 12;
            _textPaint.Color = (i == _activeTabIndex) ? SKColor.Parse("#1A73E8") : SKColor.Parse("#5F6368");

            // 截断过长标题
            while (tabTitle.Length > 0 && _textFont.MeasureText(tabTitle + "…") > textMaxWidth)
            {
                tabTitle = tabTitle[..^1];
            }
            if (tabTitle.Length < tab.Title.Length)
                tabTitle += "…";

            float textX = tabRect.Left + 10;
            float textY = tabRect.Top + tabHeight * 0.65f;
            canvas.DrawText(tabTitle, textX, textY, SKTextAlign.Left, _textFont, _textPaint);

            // 关闭按钮
            float closeBtnSize = 14;
            float closeX = tabRect.Right - closeBtnSize - 6;
            float closeY = tabRect.Top + (tabHeight - closeBtnSize) / 2;
            var closeRect = new SKRect(closeX, closeY, closeX + closeBtnSize, closeY + closeBtnSize);
            _tabCloseRects.Add(closeRect);

            // 关闭按钮背景
            if (i == _hoveredCloseIndex)
            {
                canvas.DrawRoundRect(closeRect, new SKSize(2, 2), _tabCloseActivePaint);
            }

            // 关闭按钮 X
            _closeBtnHoverPaint.Color = (i == _hoveredCloseIndex) ? SKColor.Parse("#202124") : SKColor.Parse("#80868B");
            float cx = closeRect.Left + closeBtnSize / 2;
            float cy = closeRect.Top + closeBtnSize * 0.7f;
            string closeSymbol = "✕";
            float symWidth = _cachedCloseFont.MeasureText(closeSymbol);
            canvas.DrawText(closeSymbol, cx - symWidth / 2, cy, SKTextAlign.Left, _cachedCloseFont, _closeBtnHoverPaint);

            tabX += tabWidth + 3;
        }

        // 新建标签按钮
        float newTabX = tabX + 2;
        float newTabY = tabY + 4;
        float newTabSize = tabHeight - 8;
        _newTabButtonRect = new SKRect(newTabX, newTabY, newTabX + newTabSize, newTabY + newTabSize);

        var newTabBgPaint = _newTabHovered ? _buttonHoverPaint : _buttonPaint;
        canvas.DrawRoundRect(_newTabButtonRect, new SKSize(BorderRadius, BorderRadius), newTabBgPaint);

        _cachedNewTabPaint.Color = _newTabHovered ? SKColor.Parse("#202124") : SKColor.Parse("#5F6368");
        string plusSymbol = "+";
        float plusWidth = _cachedNewTabFont.MeasureText(plusSymbol);
        canvas.DrawText(plusSymbol,
            _newTabButtonRect.Left + (_newTabButtonRect.Width - plusWidth) / 2,
            _newTabButtonRect.Top + _newTabButtonRect.Height * 0.7f,
            SKTextAlign.Left, _cachedNewTabFont, _cachedNewTabPaint);

        // 窗口标题
        _cachedTitleFont.Size = 12;
        _cachedTitlePaint.Color = SKColor.Parse("#202124");
        string windowTitle = "UpBrowser";
        float titleWidth = _cachedTitleFont.MeasureText(windowTitle);
        canvas.DrawText(windowTitle, width - controlArea + 5, TabBarHeight * 0.65f, SKTextAlign.Left, _cachedTitleFont, _cachedTitlePaint);
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
            canvas.DrawText("🔒", iconX, iconY, SKTextAlign.Left, _cachedLockFont, _cachedLockPaint);
        else if (url.StartsWith("upbrowser://"))
            canvas.DrawText("🌐", iconX, iconY, SKTextAlign.Left, _cachedBrowserFont, _cachedBrowserPaint);
        else
            canvas.DrawText("ℹ", iconX, iconY, SKTextAlign.Left, _cachedInfoFont, _cachedInfoPaint);

        float textX = iconX + 20;
        float textY = toolbarCenter + 5;

        string displayUrl = _urlBarFocused ? _urlBarText : url;
        if (!_urlBarFocused && displayUrl.Length > 60)
            displayUrl = displayUrl[..60] + "...";

        _urlTextPaint.Color = _urlBarFocused ? SKColor.Parse("#1A73E8") : SKColor.Parse("#3C4043");
        canvas.DrawText(displayUrl, textX, textY, SKTextAlign.Left, _urlTextFont, _urlTextPaint);

        if (_urlBarFocused && _showCursor)
        {
            string textBeforeCursor = _urlBarText[..Math.Min(_cursorPosition, _urlBarText.Length)];
            float cursorX = textX + _urlTextFont.MeasureText(textBeforeCursor);
            canvas.DrawLine(cursorX, iconY - 12, cursorX, iconY + 2, _cachedCursorPaint);
        }
    }

    private void RenderNavButton(SKCanvas canvas, SKRect rect, string symbol, bool hovered, bool enabled)
    {
        var paint = enabled ? (hovered ? _buttonHoverPaint : _buttonPaint) : _buttonPaint;
        canvas.DrawRoundRect(rect, new SKSize(BorderRadius, BorderRadius), paint);

        _cachedSymbolPaint.Color = enabled ? SKColor.Parse("#3C4043") : SKColor.Parse("#BDC1C6");
        float symWidth = _cachedSymbolFont.MeasureText(symbol);
        canvas.DrawText(symbol,
            rect.Left + (rect.Width - symWidth) / 2,
            rect.Top + rect.Height * 0.7f,
            SKTextAlign.Left, _cachedSymbolFont, _cachedSymbolPaint);
    }

    private void RenderStatusBar(SKCanvas canvas, float width, float height)
    {
        float statusTop = height - StatusBarHeight;

        canvas.DrawRect(0, statusTop, width, StatusBarHeight, _backgroundPaint);
        canvas.DrawLine(0, statusTop, width, statusTop, _borderPaint);

        string statusText = string.IsNullOrEmpty(_currentUrl) ? "Ready" : _currentUrl;
        if (statusText.Length > 80)
            statusText = statusText[..80] + "...";
        canvas.DrawText(statusText, 10, statusTop + StatusBarHeight * 0.7f, SKTextAlign.Left, _cachedStatusFont, _cachedStatusPaint);

        string tabCount = $"Tab {_activeTabIndex + 1}/{_tabs.Count}";
        float tcWidth = _cachedStatusFont.MeasureText(tabCount);
        canvas.DrawText(tabCount, width - tcWidth - 10, statusTop + StatusBarHeight * 0.7f, SKTextAlign.Left, _cachedStatusFont, _cachedStatusPaint);
    }

    // ==================== 输入处理方法 ====================

    public void HandleMouseMove(float x, float y)
    {
        _backHovered = _backButtonRect.Contains(x, y);
        _forwardHovered = _forwardButtonRect.Contains(x, y);
        _refreshHovered = _refreshButtonRect.Contains(x, y);
        _homeHovered = _homeButtonRect.Contains(x, y);
        _newTabHovered = _newTabButtonRect.Contains(x, y);

        // 检测标签页悬停
        _hoveredTabIndex = -1;
        _hoveredCloseIndex = -1;

        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                _hoveredTabIndex = i;
                break;
            }
        }

        for (int i = 0; i < _tabCloseRects.Count; i++)
        {
            if (_tabCloseRects[i].Contains(x, y))
            {
                _hoveredCloseIndex = i;
                break;
            }
        }
    }

    public bool HandleMouseClick(float x, float y)
    {
        // 检查新建标签按钮
        if (_newTabButtonRect.Contains(x, y))
        {
            AddTab();
            return true;
        }

        // 检查标签关闭按钮
        for (int i = 0; i < _tabCloseRects.Count; i++)
        {
            if (_tabCloseRects[i].Contains(x, y))
            {
                CloseTab(i);
                return true;
            }
        }

        // 检查标签切换
        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (_tabRects[i].Contains(x, y))
            {
                if (i != _activeTabIndex)
                {
                    SwitchToTab(i);
                }
                return true;
            }
        }

        // 检查 URL 栏点击
        if (_urlBarRect.Contains(x, y))
        {
            if (!_urlBarFocused)
            {
                _urlBarFocused = true;
                _urlBarText = _currentUrl;
                _cursorPosition = _urlBarText.Length;
                OnUrlBarFocus?.Invoke();
            }
            return true;
        }

        // 检查导航按钮点击
        if (_backButtonRect.Contains(x, y) && CanGoBack())
        {
            BlurUrlBar();
            GoBack();
            return true;
        }

        if (_forwardButtonRect.Contains(x, y) && CanGoForward())
        {
            BlurUrlBar();
            GoForward();
            return true;
        }

        if (_refreshButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnRefresh?.Invoke();
            return true;
        }

        if (_homeButtonRect.Contains(x, y))
        {
            BlurUrlBar();
            OnHome?.Invoke();
            return true;
        }

        BlurUrlBar();
        return false;
    }

    private void BlurUrlBar()
    {
        if (_urlBarFocused)
        {
            _urlBarFocused = false;
            OnUrlBarBlur?.Invoke();
        }
    }

    public bool HandleKeyPress(char keyChar, SKKey key)
    {
        if (!_urlBarFocused) return false;

        if (_isImeComposing)
        {
            if (key == SKKey.Escape)
            {
                _isImeComposing = false;
                _imeCompositionString = "";
                _imeCompositionCursor = 0;
                return true;
            }
            return true;
        }

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

    // ==================== 导航方法 ====================

    public void NavigateToUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.Contains("://") && !url.StartsWith("upbrowser://"))
        {
            if (url.Contains('.') || url.Contains('/'))
                url = "https://" + url;
            else
                url = "https://www.baidu.com/search?q=" + Uri.EscapeDataString(url);
        }

        _currentUrl = url;
        _urlBarText = "";

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        _history.Add(url);
        _historyIndex = _history.Count - 1;

        // 更新当前标签页信息
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].Url = url;
            _tabs[_activeTabIndex].Title = url;
        }

        OnNavigate?.Invoke(url);
    }

    /// <summary>
    /// Update the displayed URL in the URL bar and tab without triggering a navigation.
    /// Used to reflect the effective URL after HTTP redirects.
    /// </summary>
    public void UpdateUrl(string url)
    {
        _currentUrl = url;
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            _tabs[_activeTabIndex].Url = url;
        }
        _urlBarText = "";
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

    // ==================== 标签页管理 ====================

    public void AddTab(string url = "upbrowser://newtab")
    {
        _tabs.Add(new TabInfo { Title = "New Tab", Url = url });
        _activeTabIndex = _tabs.Count - 1;
        _urlBarFocused = false;

        _currentUrl = url;
        OnTabChanged?.Invoke(url);
    }

    public void CloseTab(int index)
    {
        if (_tabs.Count <= 1)
        {
            _tabs[0] = new TabInfo { Title = "New Tab", Url = "upbrowser://newtab" };
            _activeTabIndex = 0;
            _currentUrl = "upbrowser://newtab";
            OnTabChanged?.Invoke("upbrowser://newtab");
            return;
        }

        if (index < 0 || index >= _tabs.Count) return;

        _tabs.RemoveAt(index);

        if (index == _activeTabIndex)
        {
            if (_activeTabIndex >= _tabs.Count)
                _activeTabIndex = _tabs.Count - 1;

            _currentUrl = _tabs[_activeTabIndex].Url;
            OnTabChanged?.Invoke(_currentUrl);
        }
        else if (index < _activeTabIndex)
        {
            _activeTabIndex--;
        }
    }

    public void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeTabIndex) return;

        _tabs[_activeTabIndex].IsActive = false;
        _activeTabIndex = index;
        _tabs[_activeTabIndex].IsActive = true;

        _urlBarFocused = false;
        _currentUrl = _tabs[_activeTabIndex].Url;

        OnTabChanged?.Invoke(_currentUrl);
    }

    public void NextTab()
    {
        if (_tabs.Count <= 1) return;
        int next = (_activeTabIndex + 1) % _tabs.Count;
        SwitchToTab(next);
    }

    public void PreviousTab()
    {
        if (_tabs.Count <= 1) return;
        int prev = (_activeTabIndex - 1 + _tabs.Count) % _tabs.Count;
        SwitchToTab(prev);
    }

    // ==================== 工具方法 ====================

    public bool UpdateCursorBlink()
    {
        if (Environment.TickCount64 - _lastCursorBlinkTick > 500)
        {
            _showCursor = !_showCursor;
            _lastCursorBlinkTick = Environment.TickCount64;
            return true;
        }
        return false;
    }

    public string GetCurrentUrl() => _currentUrl;
    public bool IsUrlBarFocused() => _urlBarFocused;
    public int TabCount => _tabs.Count;
    public int ActiveTabIndex => _activeTabIndex;
    public bool IsLoading => _isLoading;

    public void SetLoadingState(bool loading)
    {
        _isLoading = loading;
        if (loading)
        {
            _loadingProgress = 0;
            _loadingStartTime = Environment.TickCount64;
        }
        else
        {
            _loadingProgress = 1.0f;
        }
    }

    public void UpdateLoadingProgress()
    {
        if (!_isLoading) return;
        var elapsed = Environment.TickCount64 - _loadingStartTime;
        _loadingProgress = Math.Min(0.95f, elapsed / 3000f);
    }

    public float GetContentOffset() => TabBarHeight + ToolbarHeight;
    public float GetStatusBarHeight() => StatusBarHeight;
    public float GetTabBarHeight() => TabBarHeight;
    public float GetToolbarHeight() => ToolbarHeight;

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

            canvas.DrawRect(scrollbarLeft, trackTop, ScrollManager.ScrollbarWidth, trackHeight, _scrollbarTrackPaint);

            float thumbHeight = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackHeight * viewportHeight / contentHeight);

            float maxScrollY = contentHeight - viewportHeight;
            float thumbTop = trackTop;
            if (maxScrollY > 0)
                thumbTop += (scrollManager.ScrollY / maxScrollY) * (trackHeight - thumbHeight);

            var thumbRect = new SKRect(scrollbarLeft + 2, thumbTop,
                scrollbarLeft + ScrollManager.ScrollbarWidth - 2, thumbTop + thumbHeight);
            canvas.DrawRoundRect(thumbRect, 4, 4, _scrollbarThumbPaint);
        }

        if (scrollManager.CanScrollX && viewportWidth > 0)
        {
            float contentBottom = height - StatusBarHeight;
            float scrollbarTop = contentBottom - ScrollManager.ScrollbarWidth;
            float trackLeft = 0;
            float trackWidth = viewportWidth;
            float contentWidth = scrollManager.ContentWidth;

            if (contentWidth <= viewportWidth) return;

            canvas.DrawRect(trackLeft, scrollbarTop, trackWidth, ScrollManager.ScrollbarWidth, _scrollbarTrackPaint);

            float thumbWidth = Math.Max(ScrollManager.ScrollbarMinThumbSize,
                trackWidth * viewportWidth / contentWidth);

            float maxScrollX = contentWidth - viewportWidth;
            float thumbLeft = trackLeft;
            if (maxScrollX > 0)
                thumbLeft += (scrollManager.ScrollX / maxScrollX) * (trackWidth - thumbWidth);

            var thumbRect = new SKRect(thumbLeft, scrollbarTop + 2,
                thumbLeft + thumbWidth, scrollbarTop + ScrollManager.ScrollbarWidth - 2);
            canvas.DrawRoundRect(thumbRect, 4, 4, _scrollbarThumbPaint);
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
        _tabCloseHoverPaint?.Dispose();
        _tabCloseActivePaint?.Dispose();

        _cachedTabActivePaint.Dispose();
        _cachedTabInactivePaint.Dispose();
        _cachedLockPaint.Dispose();
        _cachedBrowserPaint.Dispose();
        _cachedInfoPaint.Dispose();
        _cachedNewTabPaint.Dispose();
        _cachedTitlePaint.Dispose();
        _cachedStatusPaint.Dispose();
        _cachedSymbolPaint.Dispose();
        _cachedCursorPaint.Dispose();

        _textFont.Dispose();
        _urlTextFont.Dispose();
        _cachedLockFont.Dispose();
        _cachedBrowserFont.Dispose();
        _cachedInfoFont.Dispose();
        _cachedNewTabFont.Dispose();
        _cachedTitleFont.Dispose();
        _cachedStatusFont.Dispose();
        _cachedSymbolFont.Dispose();
        _cachedCloseFont.Dispose();

        _scrollbarTrackPaint.Dispose();
        _scrollbarThumbPaint.Dispose();
        _closeBtnHoverPaint.Dispose();
        _closeBtnHoverBgPaint.Dispose();
    }

    #region IImeSupport Implementation

    public Point GetImeCaretPosition()
    {
        if (!_urlBarFocused)
            return new Point(0, 0);

        float textX = 165;
        float textY = TabBarHeight + ToolbarHeight / 2 + 5;
        string textBeforeCursor = _urlBarText[..Math.Min(_cursorPosition, _urlBarText.Length)];
        float cursorX = textX + _urlTextFont.MeasureText(textBeforeCursor);

        return new Point(cursorX, textY);
    }

    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = "";
        _imeCompositionCursor = 0;
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        _isImeComposing = true;
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = "";
        _imeCompositionCursor = 0;

        if (resultString != null)
        {
            _urlBarText = _urlBarText[.._cursorPosition] + resultString + _urlBarText[_cursorPosition..];
            _cursorPosition += resultString.Length;
        }
    }

    public bool IsImeComposing => _isImeComposing;
    public string ImeCompositionString => _imeCompositionString;

    #endregion
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