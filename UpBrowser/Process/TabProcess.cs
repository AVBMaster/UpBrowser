using System.Collections.Concurrent;
using SkiaSharp;
using UpBrowser.Core;
using UpBrowser.Core.Dom;
using UpBrowser.Core.EventLoop;
using UpBrowser.Core.JavaScript;
using UpBrowser.Core.Layout;
using UpBrowser.Core.Performance.Resources;
using UpBrowser.Core.Process;
using UpBrowser.Rendering;

namespace UpBrowser.Process;

/// <summary>
/// TabProcess represents a tab running in its own background thread.
/// Each process has its own DocumentManager, LayoutEngine, JavaScriptEngine,
/// and caches — enabling true parallel page loading across tabs.
/// The main thread communicates via a command queue and receives DisplayList
/// results through lock-protected shared state.
/// </summary>
public class TabProcess : IDisposable
{
    private readonly int _tabIndex;
    private readonly string[] _fontFamilies;
    private readonly EventLoop _eventLoop;
    private readonly float _dpiScale;
    private readonly float _contentOffset;

    private Thread? _workerThread;
    private CancellationTokenSource _cts = new();
    private BlockingCollection<Action> _commandQueue = new();

    private readonly object _sync = new();
    private DisplayList _displayList = new();
    private TabProcessMetrics _metrics;

    // Worker thread-local state (only accessed on the worker thread)
    private DocumentManager? _docManager;
    private LayoutEngine? _layoutEngine;
    private JavaScriptEngine? _jsEngine;
    private Dictionary<string, SKTypeface>? _typefaceCache;
    private ImageCache? _imageCache;

    private volatile float _viewportWidth = 1024;
    private volatile float _viewportHeight = 768;
    private volatile bool _isLoading;
    private volatile bool _hasNewContent;

    public int TabIndex => _tabIndex;
    public bool IsAlive => _workerThread?.IsAlive == true;
    public bool IsLoading => _isLoading;
    public bool HasNewContent
    {
        get => _hasNewContent;
        set => _hasNewContent = value;
    }

    public event Action<TabProcess>? OnUpdated;

    public TabProcess(int tabIndex, string initialUrl, string[] fontFamilies,
        EventLoop eventLoop, float dpiScale = 1f, float contentOffset = 0)
    {
        _tabIndex = tabIndex;
        _fontFamilies = fontFamilies;
        _eventLoop = eventLoop;
        _dpiScale = dpiScale;
        _contentOffset = contentOffset;
        _metrics = new TabProcessMetrics { TabIndex = tabIndex, Url = initialUrl };
    }

    public void Start()
    {
        if (_workerThread?.IsAlive == true) return;
        _cts = new CancellationTokenSource();
        _commandQueue = new BlockingCollection<Action>();
        _workerThread = new Thread(WorkerMain)
        {
            Name = $"TabProc-{_tabIndex}",
            IsBackground = true
        };
        _workerThread.Start();
    }

    private void WorkerMain()
    {
        try
        {
            JsEngineConfig.DefaultEngineType = JsEngineType.Jint;
            JsEngineConfig.Initialize();

            _docManager = new DocumentManager();
            _layoutEngine = new LayoutEngine();
            _jsEngine = new JavaScriptEngine();
            _typefaceCache = new Dictionary<string, SKTypeface>();
            _imageCache = new ImageCache();

            _jsEngine.ShowDialog = (msg, type) =>
            {
                _eventLoop.PostTask(() => OnUpdated?.Invoke(this));
                return null;
            };

            lock (_sync) { _metrics.Status = "Running"; }

            foreach (var cmd in _commandQueue.GetConsumingEnumerable(_cts.Token))
            {
                if (_cts.Token.IsCancellationRequested) break;
                try { cmd(); }
                catch (OperationCanceledException) { break; }
                catch (ThreadInterruptedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TabProc-{_tabIndex}] Error: {ex.Message}");
                }
            }

            lock (_sync) { _metrics.Status = "Terminated"; }
            _jsEngine.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (ThreadInterruptedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[TabProc-{_tabIndex}] Fatal: {ex.Message}");
        }
    }

    public void Post(Action action)
    {
        if (!_cts.IsCancellationRequested)
            _commandQueue.Add(action);
    }

    /// <summary>Navigate to HTML content on the worker thread.</summary>
    public void NavigateToHtml(string html, string? baseUrl = null)
    {
        Post(() => WorkerNavigate(html, baseUrl));
    }

    private void WorkerNavigate(string html, string? baseUrl)
    {
        if (_docManager == null || _layoutEngine == null || _jsEngine == null) return;

        _isLoading = true;
        _imageCache?.Clear();
        _typefaceCache?.Clear();

        try
        {
            var loadResult = _docManager.LoadHtmlAsync(html, baseUrl,
                _viewportWidth, _viewportHeight, _dpiScale).GetAwaiter().GetResult();

            _jsEngine.LoadDocument(loadResult.Document);

            if (loadResult.StyleComputer != null)
            {
                loadResult.StyleComputer.ComputeStyles(
                    loadResult.Document, _viewportWidth, _viewportHeight);
            }

            _layoutEngine.Layout(loadResult.Document, _viewportWidth, _viewportHeight, _dpiScale);

            RunPageScripts(loadResult, baseUrl);

            var visitor = new PaintVisitor(_contentOffset, _typefaceCache,
                _imageCache, _fontFamilies, baseUrl);
            visitor.VisitDocument(loadResult.Document);
            var newDl = visitor.GetDisplayList();
            newDl.SortByZIndex();
            newDl.BuildSpatialGrid();

            var title = loadResult.Document.Title ?? "New Tab";
            int domCount = CountDocNodes(loadResult.Document);
            int boxCount = CountLayoutBoxes(loadResult.Document);

            long memBytes = GC.GetTotalMemory(false);

            lock (_sync)
            {
                _displayList = newDl;
                _metrics.Title = title;
                _metrics.Url = baseUrl ?? "";
                _metrics.DomNodeCount = domCount;
                _metrics.LayoutBoxCount = boxCount;
                _metrics.JsHeapSizeKB = _jsEngine.GetHeapSizeKB();
                _metrics.JsTimerCount = _jsEngine.TimerCount;
                _metrics.MemoryBytes = memBytes;
                _metrics.Status = "Running";
            }

            _hasNewContent = true;
            _eventLoop.PostTask(() => OnUpdated?.Invoke(this));
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Resize the viewport (affects layout on next navigation).</summary>
    public void SetViewport(float width, float height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>Get the latest DisplayList (thread-safe, main thread consumer).</summary>
    public DisplayList GetDisplayList()
    {
        lock (_sync) return _displayList;
    }

    /// <summary>Get current metrics snapshot (thread-safe).</summary>
    public TabProcessMetrics GetMetrics()
    {
        lock (_sync) return _metrics;
    }

    /// <summary>Update URL metadata (thread-safe).</summary>
    public void UpdateUrl(string url)
    {
        lock (_sync) _metrics.Url = url;
    }

    /// <summary>Update title metadata (thread-safe).</summary>
    public void UpdateTitle(string title)
    {
        lock (_sync) _metrics.Title = title;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _commandQueue.CompleteAdding();
        if (_workerThread?.IsAlive == true)
        {
            _workerThread.Interrupt();
            if (!_workerThread.Join(2000))
                Console.WriteLine($"[TabProc-{_tabIndex}] Force abort");
        }
        _cts.Dispose();
        _commandQueue.Dispose();
    }

    private void RunPageScripts(DocumentManager.DocumentLoadResult loadResult, string? baseUrl)
    {
        var angleDoc = loadResult.AngleSharpDoc;
        if (angleDoc == null || _jsEngine == null) return;

        var scripts = angleDoc.All.Where(e =>
            e.LocalName?.ToLowerInvariant() == "script").ToList();

        foreach (var el in scripts)
        {
            var src = el.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                try
                {
                    var url = ResolveUrl(src, baseUrl) ?? src;
                    var http = new StreamingHttpFetcher();
                    var resp = http.FetchAsync(new ResourceRequest
                    {
                        Url = url,
                        Kind = ResourceKind.Script,
                        Priority = ResourcePriority.High,
                        Timeout = TimeSpan.FromSeconds(10),
                    }).GetAwaiter().GetResult();
                    var code = System.Text.Encoding.UTF8.GetString(resp.Body);
                    _jsEngine.Execute(code);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TabProc] Script load failed: {ex.Message}");
                }
            }
            else
            {
                var code = el.TextContent;
                if (!string.IsNullOrWhiteSpace(code))
                    _jsEngine.Execute(code);
            }
        }
    }

    private static string? ResolveUrl(string url, string? baseUrl)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("data:"))
            return url;
        if (url.StartsWith("//"))
        {
            if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("https://"))
                return "https:" + url;
            return "http:" + url;
        }
        if (string.IsNullOrEmpty(baseUrl)) return null;
        try
        {
            var baseUri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/');
            return new Uri(baseUri, url).ToString();
        }
        catch { return null; }
    }

    private static int CountDocNodes(Core.Dom.Node node)
    {
        int count = node is Element ? 1 : 0;
        foreach (var child in node.Children)
            count += CountDocNodes(child);
        return count;
    }

    private static int CountLayoutBoxes(Core.Dom.Node node)
    {
        int count = (node is Element el && el.LayoutBox != null) ? 1 : 0;
        foreach (var child in node.Children)
            count += CountLayoutBoxes(child);
        return count;
    }
}
