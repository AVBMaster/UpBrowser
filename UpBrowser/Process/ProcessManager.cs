using System.Collections.Concurrent;
using UpBrowser.Core.EventLoop;
using UpBrowser.Core.Process;
using UpBrowser.Rendering;

namespace UpBrowser.Process;

public class ProcessManager : IDisposable
{
    private readonly string[] _fontFamilies;
    private readonly EventLoop _eventLoop;
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private readonly ConcurrentDictionary<int, TabProcess> _processes = new();
    private readonly object _lock = new();

    public int ActiveProcessCount => _processes.Count;

    public event Action<TabProcess>? OnProcessUpdated;

    public ProcessManager(string[] fontFamilies, EventLoop eventLoop,
        float dpiScale = 1f, float contentOffset = 0)
    {
        _fontFamilies = fontFamilies;
        _eventLoop = eventLoop;
        _dpiScale = dpiScale;
        _contentOffset = contentOffset;
    }

    public TabProcess CreateProcess(int tabIndex, string url = "upbrowser://newtab")
    {
        lock (_lock)
        {
            if (_processes.TryGetValue(tabIndex, out var existing))
            {
                if (existing.IsAlive)
                    return existing;
                existing.Dispose();
                _processes.TryRemove(tabIndex, out _);
            }

            var proc = new TabProcess(tabIndex, url, _fontFamilies,
                _eventLoop, _dpiScale, _contentOffset);
            proc.OnUpdated += OnProcessUpdated;
            _processes[tabIndex] = proc;
            proc.Start();
            return proc;
        }
    }

    public TabProcess? GetProcess(int tabIndex)
    {
        _processes.TryGetValue(tabIndex, out var proc);
        return proc;
    }

    public void DestroyProcess(int tabIndex)
    {
        if (_processes.TryRemove(tabIndex, out var proc))
        {
            proc.OnUpdated -= OnProcessUpdated;
            proc.Dispose();
        }
    }

    public TabProcess? GetOrCreate(int tabIndex, string url = "upbrowser://newtab")
    {
        var proc = GetProcess(tabIndex);
        if (proc != null && proc.IsAlive)
            return proc;
        return CreateProcess(tabIndex, url);
    }

    public void DestroyAll()
    {
        foreach (var kv in _processes.ToArray())
        {
            DestroyProcess(kv.Key);
        }
    }

    public DisplayList? GetDisplayList(int tabIndex)
    {
        var proc = GetProcess(tabIndex);
        return proc?.GetDisplayList();
    }

    public TabProcessMetrics? GetMetrics(int tabIndex)
    {
        var proc = GetProcess(tabIndex);
        return proc?.GetMetrics();
    }

    public List<TabProcess> GetAllProcesses()
    {
        return _processes.Values.Where(p => p.IsAlive).ToList();
    }

    public List<TabProcessMetrics> GetAllMetrics()
    {
        return _processes.Values
            .Where(p => p.IsAlive)
            .Select(p => p.GetMetrics())
            .ToList();
    }

    public void Navigate(int tabIndex, string html, string? baseUrl = null)
    {
        var proc = GetOrCreate(tabIndex);
        if (proc == null) return;
        proc.UpdateUrl(baseUrl ?? html);
        proc.NavigateToHtml(html, baseUrl);
    }

    public void Dispose()
    {
        DestroyAll();
    }
}
