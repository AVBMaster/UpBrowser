using System;
using System.Collections.Generic;
using System.Diagnostics;
using SkiaSharp;

namespace UpBrowser.Rendering;

public class TaskManagerPage
{
    private bool _visible;
    private float _panelWidth = 420;
    private float _scrollOffset;
    private float _contentHeight;
    private readonly SKTypeface _typeface;
    private int _hoveredRow = -1;
    private bool _closeHovered;
    private float _headerHeight = 40;
    private float _colHeaderHeight = 30;
    private float _rowHeight = 36;

    private long _lastRefreshTick;
    private const long RefreshIntervalMs = 1000;
    private bool _needsRefresh = true;

    // Process tracking for CPU
    private DateTime _lastCpuTime;
    private TimeSpan _lastProcessorTime;

    public bool Visible => _visible;

    public event Action? OnChanged;

    private List<ProcessInfo> _processes = new();

    private struct ProcessInfo
    {
        public string Name;
        public string Title;
        public string Memory;
        public string Cpu;
        public string Status;
        public int Pid;
        public bool IsBrowser;
    }

    public TaskManagerPage()
    {
        _typeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _lastCpuTime = DateTime.UtcNow;
        var p = Process.GetCurrentProcess();
        _lastProcessorTime = p.TotalProcessorTime;
    }

    public void Toggle()
    {
        _visible = !_visible;
        _scrollOffset = 0;
        if (_visible)
        {
            _needsRefresh = true;
            RefreshData();
        }
    }

    public void Show()
    {
        _visible = true;
        _scrollOffset = 0;
        _needsRefresh = true;
        RefreshData();
    }

    public void Hide()
    {
        _visible = false;
        _scrollOffset = 0;
    }

    public void UpdateTabs(List<(string title, string url, bool isLoading)> tabs)
    {
        if (!_visible) return;
        // Check if tabs changed
        _needsRefresh = true;
        RefreshData();
    }

    public void RefreshData()
    {
        var now = DateTime.UtcNow;
        if (!_needsRefresh && (now - _lastCpuTime).TotalMilliseconds < RefreshIntervalMs)
            return;
        _needsRefresh = false;
        _lastRefreshTick = Environment.TickCount64;

        var process = Process.GetCurrentProcess();
        process.Refresh();

        double workingSetMB = process.WorkingSet64 / (1024.0 * 1024.0);
        double privateMB = process.PrivateMemorySize64 / (1024.0 * 1024.0);
        double heapMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        // CPU
        var currentProcessorTime = process.TotalProcessorTime;
        var cpuDelta = (currentProcessorTime - _lastProcessorTime).TotalSeconds;
        var timeDelta = (now - _lastCpuTime).TotalSeconds;
        double cpuPercent = 0;
        if (timeDelta > 0.01 && cpuDelta >= 0)
        {
            cpuPercent = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;
            cpuPercent = Math.Round(cpuPercent, 1);
        }
        _lastCpuTime = now;
        _lastProcessorTime = currentProcessorTime;

        // Rebuild process list
        _processes.Clear();

        // Main browser process
        _processes.Add(new ProcessInfo
        {
            Name = "Browser",
            Title = $"UpBrowser",
            Memory = $"{workingSetMB:F1} MB",
            Cpu = $"{cpuPercent:F1}%",
            Status = "Running",
            Pid = process.Id,
            IsBrowser = true
        });

        // Sub-items for browser process
        _processes.Add(new ProcessInfo
        {
            Name = "‧ Private Memory",
            Title = "",
            Memory = $"{privateMB:F1} MB",
            Cpu = "",
            Status = "",
            Pid = 0,
            IsBrowser = false
        });

        _processes.Add(new ProcessInfo
        {
            Name = "‧ Managed Heap",
            Title = "",
            Memory = $"{heapMB:F1} MB",
            Cpu = "",
            Status = "",
            Pid = 0,
            IsBrowser = false
        });
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentOffset, 
                       List<(string title, string url, bool isLoading)> tabs)
    {
        if (!_visible) return;

        // Auto refresh
        if (Environment.TickCount64 - _lastRefreshTick > RefreshIntervalMs)
        {
            _needsRefresh = true;
            RefreshData();
        }

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;
        float panelBottom = windowHeight;

        canvas.Save();

        // Panel background
        using var bg = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 245),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, bg);

        using var border = new SKPaint
        {
            Color = new SKColor(200, 200, 200, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, border);

        // Header
        using var headerBg = new SKPaint
        {
            Color = new SKColor(32, 33, 36),
            Style = SKPaintStyle.Fill
        };
        using var headerPath = new SKPath();
        headerPath.AddRoundRect(new SKRect(panelLeft, panelTop, panelLeft + _panelWidth, panelTop + _headerHeight), 8, 8);
        canvas.DrawPath(headerPath, headerBg);
        canvas.DrawRect(panelLeft, panelTop + 4, _panelWidth, _headerHeight - 4, headerBg);

        using var headerFont = new SKFont(_typeface, 14);
        using var headerPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("Task Manager", panelLeft + 16, panelTop + 26, SKTextAlign.Left, headerFont, headerPaint);

        // Close button
        float closeBtnSize = 24;
        float closeX = panelLeft + _panelWidth - closeBtnSize - 10;
        float closeY = panelTop + (_headerHeight - closeBtnSize) / 2;
        var closeRect = new SKRect(closeX, closeY, closeX + closeBtnSize, closeY + closeBtnSize);
        if (_closeHovered)
        {
            using var closeBg = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 40),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(closeRect, 4, 4, closeBg);
        }
        using var closeFont = new SKFont(_typeface, 14);
        using var closePaint = new SKPaint { Color = new SKColor(200, 200, 200), IsAntialias = true };
        canvas.DrawText("✕", closeRect.MidX - 6, closeRect.MidY + 5, SKTextAlign.Left, closeFont, closePaint);

        // Column headers
        float colY = panelTop + _headerHeight;
        using var colBg = new SKPaint
        {
            Color = new SKColor(248, 249, 250),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(panelLeft, colY, _panelWidth, _colHeaderHeight, colBg);

        using var colFont = new SKFont(_typeface, 11);
        using var colPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };

        float[] colWidths = { _panelWidth - 250, 80, 70, 80 };
        float[] colStarts = { panelLeft + 12, panelLeft + _panelWidth - 240, panelLeft + _panelWidth - 160, panelLeft + _panelWidth - 80 };

        canvas.DrawText("Process", colStarts[0], colY + 20, SKTextAlign.Left, colFont, colPaint);
        canvas.DrawText("Memory", colStarts[1], colY + 20, SKTextAlign.Left, colFont, colPaint);
        canvas.DrawText("CPU", colStarts[2], colY + 20, SKTextAlign.Left, colFont, colPaint);
        canvas.DrawText("Status", colStarts[3], colY + 20, SKTextAlign.Left, colFont, colPaint);

        // Separator below column header
        using var sepLine = new SKPaint { Color = new SKColor(218, 220, 224), Style = SKPaintStyle.Fill };
        canvas.DrawRect(panelLeft, colY + _colHeaderHeight, _panelWidth, 1, sepLine);

        // Calculate scroll height based on all rows
        int totalRows = _processes.Count + tabs.Count;
        _contentHeight = totalRows * _rowHeight;

        // Clip to content area
        float contentTop = colY + _colHeaderHeight + 1;
        float contentBottom = panelBottom;
        canvas.Save();
        canvas.ClipRect(new SKRect(panelLeft, contentTop, panelLeft + _panelWidth, contentBottom));

        using var rowFont = new SKFont(_typeface, 12);
        using var namePaint = new SKPaint { Color = new SKColor(32, 33, 36), IsAntialias = true };
        using var detailPaint = new SKPaint { Color = new SKColor(95, 99, 104), IsAntialias = true };
        using var urlPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        using var altRowPaint = new SKPaint
        {
            Color = new SKColor(248, 249, 250),
            Style = SKPaintStyle.Fill
        };
        using var hoverRowPaint = new SKPaint
        {
            Color = new SKColor(232, 240, 254),
            Style = SKPaintStyle.Fill
        };

        float yPos = contentTop - _scrollOffset;

        // Draw process rows
        int rowIndex = 0;
        foreach (var proc in _processes)
        {
            float rowTop = yPos + rowIndex * _rowHeight;
            if (rowTop + _rowHeight < contentTop) { rowIndex++; continue; }
            if (rowTop > contentBottom) break;

            bool isHovered = rowIndex == _hoveredRow;

            // Alternating row background
            if (rowIndex % 2 == 1 && !isHovered)
                canvas.DrawRect(panelLeft, rowTop, _panelWidth, _rowHeight, altRowPaint);
            if (isHovered)
                canvas.DrawRect(panelLeft, rowTop, _panelWidth, _rowHeight, hoverRowPaint);

            // Name/title column
            float nameX = proc.IsBrowser ? colStarts[0] : colStarts[0] + 12;
            canvas.DrawText(proc.Name, nameX, rowTop + 20, SKTextAlign.Left, rowFont, proc.IsBrowser ? namePaint : detailPaint);

            // Memory
            if (!string.IsNullOrEmpty(proc.Memory))
                canvas.DrawText(proc.Memory, colStarts[1], rowTop + 20, SKTextAlign.Left, rowFont, namePaint);

            // CPU
            if (!string.IsNullOrEmpty(proc.Cpu))
                canvas.DrawText(proc.Cpu, colStarts[2], rowTop + 20, SKTextAlign.Left, rowFont, namePaint);

            // Status
            if (!string.IsNullOrEmpty(proc.Status))
                canvas.DrawText(proc.Status, colStarts[3], rowTop + 20, SKTextAlign.Left, rowFont, detailPaint);

            rowIndex++;
        }

        // Draw tab rows
        foreach (var tab in tabs)
        {
            float rowTop = yPos + rowIndex * _rowHeight;
            if (rowTop + _rowHeight < contentTop) { rowIndex++; continue; }
            if (rowTop > contentBottom) break;

            bool isHovered = rowIndex == _hoveredRow;

            if (rowIndex % 2 == 1 && !isHovered)
                canvas.DrawRect(panelLeft, rowTop, _panelWidth, _rowHeight, altRowPaint);
            if (isHovered)
                canvas.DrawRect(panelLeft, rowTop, _panelWidth, _rowHeight, hoverRowPaint);

            // Tab icon placeholder + title
            string displayName = string.IsNullOrEmpty(tab.title) ? "New Tab" : tab.title;
            if (rowFont.MeasureText(displayName) > colWidths[0] - 24)
            {
                while (rowFont.MeasureText(displayName + "…") > colWidths[0] - 24 && displayName.Length > 2)
                    displayName = displayName[..^1];
                displayName += "…";
            }

            // Small circle icon
            using var circlePaint = new SKPaint
            {
                Color = new SKColor(26, 115, 232),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(colStarts[0] + 6, rowTop + 14, 4, circlePaint);
            canvas.DrawText(displayName, colStarts[0] + 16, rowTop + 20, SKTextAlign.Left, rowFont, namePaint);

            // Memory (N/A for tabs)
            canvas.DrawText("-", colStarts[1], rowTop + 20, SKTextAlign.Left, rowFont, detailPaint);

            // CPU (N/A for tabs)
            canvas.DrawText("-", colStarts[2], rowTop + 20, SKTextAlign.Left, rowFont, detailPaint);

            // Status
            string status = tab.isLoading ? "Loading" : "Complete";
            canvas.DrawText(status, colStarts[3], rowTop + 20, SKTextAlign.Left, rowFont, status == "Loading" ? urlPaint : detailPaint);

            rowIndex++;
        }

        _contentHeight = rowIndex * _rowHeight;

        canvas.Restore();

        // Bottom scroll indicator if needed
        if (_contentHeight > contentBottom - contentTop)
        {
            float scrollTrackH = contentBottom - contentTop;
            float scrollThumbH = Math.Max(30, scrollTrackH * (contentBottom - contentTop) / _contentHeight);
            float maxScroll = _contentHeight - (contentBottom - contentTop);
            float scrollThumbY = contentTop + (_scrollOffset / maxScroll) * (scrollTrackH - scrollThumbH);
            using var scrollBg = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 20),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(panelLeft + _panelWidth - 6, contentTop, 4, scrollTrackH, 2, 2, scrollBg);
            using var scrollThumb = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 60),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRoundRect(panelLeft + _panelWidth - 6, scrollThumbY, 4, scrollThumbH, 2, 2, scrollThumb);
        }

        canvas.Restore();
    }

    public bool HandleClick(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop) return false;

        // Close button
        float closeBtnSize = 24;
        float closeX = panelLeft + _panelWidth - closeBtnSize - 10;
        float closeY = panelTop + (_headerHeight - closeBtnSize) / 2;
        var closeRect = new SKRect(closeX, closeY, closeX + closeBtnSize, closeY + closeBtnSize);
        if (closeRect.Contains(x, y))
        {
            Hide();
            OnChanged?.Invoke();
            return true;
        }

        return true;
    }

    public bool HandleMouseMove(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;

        int oldHovered = _hoveredRow;
        bool oldClose = _closeHovered;

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop)
        {
            _hoveredRow = -1;
            _closeHovered = false;
            if (oldHovered != -1 || oldClose) OnChanged?.Invoke();
            return false;
        }

        // Close button hover
        float closeBtnSize = 24;
        float closeX = panelLeft + _panelWidth - closeBtnSize - 10;
        float closeY = panelTop + (_headerHeight - closeBtnSize) / 2;
        _closeHovered = new SKRect(closeX, closeY, closeX + closeBtnSize, closeY + closeBtnSize).Contains(x, y);

        if (_closeHovered)
        {
            _hoveredRow = -1;
            if (oldHovered != -1 || !oldClose) OnChanged?.Invoke();
            return true;
        }

        // Row hover
        float colY = panelTop + _headerHeight;
        float contentTop = colY + _colHeaderHeight + 1;
        float relY = y - contentTop + _scrollOffset;
        int newHovered = (int)(relY / _rowHeight);
        if (newHovered < 0) newHovered = -1;

        if (_hoveredRow != newHovered)
        {
            _hoveredRow = newHovered;
            OnChanged?.Invoke();
        }

        return true;
    }

    public bool HandleWheel(float delta, float windowHeight, float contentOffset)
    {
        if (!_visible) return false;

        float maxScroll = Math.Max(0, _contentHeight - (windowHeight - contentOffset - _headerHeight - _colHeaderHeight));
        _scrollOffset = Math.Clamp(_scrollOffset - delta * 0.5f, 0, maxScroll);
        return true;
    }

    public void HandleMouseUp() { }
}
