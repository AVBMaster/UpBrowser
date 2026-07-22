using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JavaScriptEngineSwitcher.Core;
using DomDocument = UpBrowser.Core.Dom.Document;
using DomElement = UpBrowser.Core.Dom.Element;

namespace UpBrowser.Core.JavaScript;

public class JavaScriptEngine : IDisposable
{
    private IJavaScriptEngineAdapter? _adapter;
    private bool _disposed;
    private DocumentHost? _documentHost;
    private DomDocument? _currentDocument;
    private int _nextTimerId = 1;
    private readonly Dictionary<int, TimerInfo> _timers = new();
    private readonly object _timersLock = new();

    private int _nextFetchId = 1;
    private readonly Dictionary<int, (int resolveId, int rejectId)> _fetchCallbacks = new();
    private readonly Dictionary<int, FetchResult> _fetchResults = new();
    private readonly object _fetchLock = new();

    private LocationHost? _locationHost;
    private UpBrowserBuiltins? _builtins;

    private JsIntegrationService? _integrationService;

    [ThreadStatic]
    public static JavaScriptEngine? Current;

    public JsEngineType EngineType => _adapter?.EngineType ?? JsEngineType.Jint;
    public LocationHost? LocationHost => _locationHost;
    public UpBrowserBuiltins? Builtins => _builtins;
    public DocumentHost? DocumentHost => _documentHost;
    public IJavaScriptEngineAdapter? Adapter => _adapter;
    public JsIntegrationService? IntegrationService => _integrationService;

    public event Action? OnDomChanged;
    public Func<string, string?, string?>? ShowDialog { get; set; }
    public bool HasTimers { get { lock (_timersLock) return _timers.Count > 0; } }
    public int TimerCount { get { lock (_timersLock) return _timers.Count; } }

    public int GetHeapSizeKB()
    {
        return (int)(GC.GetTotalMemory(false) / 1024);
    }

    internal IJsEngine? InnerEngine => _adapter?.InnerEngine;

    public JavaScriptEngine() : this(CreateDefaultAdapter())
    {
    }

    public JavaScriptEngine(IJavaScriptEngineAdapter adapter)
    {
        _adapter = adapter;
        _integrationService = new JsIntegrationService(adapter);
        _integrationService.SetJsEngine(this);
        SetupGlobals();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "CreateAdapterForEngine attribute cascaded; caller takes existing engine, adapter selection is safe")]
    public JavaScriptEngine(IJsEngine existingEngine)
    {
        _adapter = CreateAdapterForEngine(existingEngine);
        _integrationService = new JsIntegrationService(_adapter);
        _integrationService.SetJsEngine(this);
        SetupGlobals();
    }

    private static IJavaScriptEngineAdapter CreateDefaultAdapter()
    {
        JsEngineConfig.Initialize();
        var effectiveType = JsEngineConfig.EffectiveEngineType;
        return effectiveType switch
        {
            JsEngineType.V8 => new V8EngineAdapter(),
            JsEngineType.Jurassic => new JurassicEngineAdapter(),
            _ => new JintEngineAdapter()
        };
    }

    [RequiresUnreferencedCode("Engine type detection uses GetType().Name")]
    private static IJavaScriptEngineAdapter CreateAdapterForEngine(IJsEngine engine)
    {
        var engineName = engine?.GetType().Name ?? "";
        if (engineName.Contains("V8")) return new V8EngineAdapter(engine);
        if (engineName.Contains("Jurassic")) return new JurassicEngineAdapter(engine);
        return new JintEngineAdapter(engine);
    }

    private void SetupGlobals()
    {
        if (_adapter == null) return;

        _adapter.Execute(JsCallbackStore.JsSetup);

        var consoleHost = new ConsoleHost();
        consoleHost.DevToolsConsole = new JsDevToolsConsole();
        _adapter.SetGlobal("console", consoleHost);
        _builtins = new UpBrowserBuiltins(this);
        _adapter.SetGlobal("__upbrowser", _builtins);
        _adapter.SetGlobal("__win", new WindowHost(this));
        _adapter.SetGlobal("navigator", new NavigatorHost());
        _locationHost = new LocationHost();
        _adapter.SetGlobal("location", _locationHost);
        _adapter.SetGlobal("history", new HistoryHost(_locationHost));
        _adapter.SetGlobal("screen", new ScreenHost());
        _adapter.SetGlobal("localStorage", new StorageHost("localStorage"));
        _adapter.SetGlobal("sessionStorage", new StorageHost("sessionStorage"));

        _adapter.SetGlobal("document", new DocumentHost(new UpBrowser.Core.Dom.Document()));

        _adapter.Execute(GetSetupScript());
    }

    public void LoadDocument(DomDocument document)
    {
        _currentDocument = document;
        _documentHost = new DocumentHost(document);
        _documentHost.Engine = this;

        if (_adapter != null)
        {
            ClearState();
            _integrationService?.LoadDocument(document);
            ReapplyGlobals();
            _adapter.SetGlobal("document", _documentHost);
        }

        MarkDirty();
    }

    private void ReapplyGlobals()
    {
        if (_adapter == null) return;

        _adapter.Execute(JsCallbackStore.JsSetup);
        if (_builtins != null)
            _adapter.SetGlobal("__upbrowser", _builtins);
        _adapter.SetGlobal("__win", new WindowHost(this));
        _adapter.SetGlobal("navigator", new NavigatorHost());
        if (_locationHost != null)
            _adapter.SetGlobal("location", _locationHost);
        _adapter.SetGlobal("history", new HistoryHost(_locationHost ?? new LocationHost()));
        _adapter.SetGlobal("screen", new ScreenHost());
        _adapter.SetGlobal("localStorage", new StorageHost("localStorage"));
        _adapter.SetGlobal("sessionStorage", new StorageHost("sessionStorage"));
        _adapter.SetGlobal("console", new ConsoleHost { DevToolsConsole = new JsDevToolsConsole() });

        _adapter.Execute(GetSetupScript());
    }

    public void ClearState()
    {
        if (_adapter == null) return;

        _adapter.ClearCallbacks();

        lock (_timersLock)
        {
            foreach (var info in _timers.Values)
                _adapter.RemoveCallback(info.CallbackId);
            _timers.Clear();
        }

        lock (_fetchLock)
        {
            foreach (var cbs in _fetchCallbacks.Values)
            {
                _adapter.RemoveCallback(cbs.resolveId);
                _adapter.RemoveCallback(cbs.rejectId);
            }
            _fetchCallbacks.Clear();
            _fetchResults.Clear();
        }

        if (_adapter != null)
        {
            try
            {
                var ch = _adapter.GetGlobal<ConsoleHost>("console");
                ch?.Reset();
            }
            catch { }
        }
    }

    public void Execute(string code, string? sourceUrl = null, ScriptType type = ScriptType.Inline, int lineOffset = 0)
    {
        if (_adapter == null || string.IsNullOrEmpty(code)) return;
        Current = this;
        try
        {
            _integrationService?.ExecuteScript(code, sourceUrl, type, lineOffset);
            MarkDirty();
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression)
    {
        if (_adapter == null) return null;
        Current = this;
        try
        {
            return _adapter.Evaluate(expression);
        }
        catch
        {
            return null;
        }
        finally
        {
            Current = null;
        }
    }

    public object? CallJsFunction(string functionName, params object?[] args)
    {
        if (_adapter == null) return null;
        Current = this;
        try
        {
            return _adapter.CallFunction(functionName, args);
        }
        catch
        {
            return null;
        }
        finally
        {
            Current = null;
        }
    }

    public void SetGlobal(string name, object? value)
    {
        _adapter?.SetGlobal(name, value);
    }

    public ElementHost? GetElementHost(DomElement element)
    {
        return new ElementHost(element);
    }

    public bool DispatchEvent(DomElement element, string eventType)
    {
        var previous = Current;
        Current = this;
        try
        {
            var targetHost = new ElementHost(element);
            var evt = new ScriptEvent(eventType, targetHost);
            return targetHost.DispatchEvent(evt);
        }
        finally
        {
            Current = previous;
        }
    }

    public bool DispatchEvent(DomElement element, ScriptEvent evt)
    {
        var previous = Current;
        Current = this;
        try
        {
            var targetHost = new ElementHost(element);
            return targetHost.DispatchEvent(evt);
        }
        finally
        {
            Current = previous;
        }
    }

    internal int StoreCallbackRef(object callback)
    {
        return _adapter?.StoreCallback(callback) ?? 0;
    }

    internal void InvokeCallback(int cbId)
    {
        _adapter?.InvokeCallback(cbId);
    }

    internal void InvokeCallbackWith(int cbId, object arg)
    {
        _adapter?.InvokeCallbackWith(cbId, arg);
    }

    internal void RemoveCallback(int cbId)
    {
        _adapter?.RemoveCallback(cbId);
    }

    public void TickTimers()
    {
        List<int> due = new();
        lock (_timersLock)
        {
            var now = Environment.TickCount64;
            foreach (var kv in _timers.ToList())
            {
                if (now >= kv.Value.DueTime)
                {
                    due.Add(kv.Key);
                    if (kv.Value.Interval > 0)
                        kv.Value.DueTime = now + kv.Value.Interval;
                    else
                        _timers.Remove(kv.Key);
                }
            }
        }

        foreach (var id in due)
        {
            InvokeTimer(id);
        }

        Current = this;
        try { ProcessCompletedFetches(); }
        finally { Current = null; }
    }

    private void InvokeTimer(int timerId)
    {
        int? cbId;
        lock (_timersLock)
        {
            if (_timers.TryGetValue(timerId, out var info))
                cbId = info.CallbackId;
            else
                cbId = null;
        }
        if (cbId == null) return;
        Current = this;
        try
        {
            _adapter?.InvokeCallback(cbId.Value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS Timer] {ex.Message}");
        }
        finally
        {
            Current = null;
        }
    }

    public bool NeedsReLayout { get; set; }

    public void MarkDirty()
    {
        NeedsReLayout = true;
        OnDomChanged?.Invoke();
    }

    public void ClearDirty()
    {
        NeedsReLayout = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_timersLock) _timers.Clear();
        _adapter?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal int SetTimer(int callbackId, int delayMs, bool recurring)
    {
        if (_integrationService != null)
        {
            if (recurring)
                return _integrationService.SetInterval(callbackId, delayMs);
            return _integrationService.SetTimeout(callbackId, delayMs);
        }

        lock (_timersLock)
        {
            var id = _nextTimerId++;
            _timers[id] = new TimerInfo
            {
                CallbackId = callbackId,
                DueTime = Environment.TickCount64 + Math.Max(1, delayMs),
                Interval = recurring ? Math.Max(1, delayMs) : 0
            };
            return id;
        }
    }

    internal void ClearTimer(int id)
    {
        _integrationService?.ClearTimer(id);

        lock (_timersLock)
        {
            if (_timers.TryGetValue(id, out var info))
            {
                _adapter?.RemoveCallback(info.CallbackId);
                _timers.Remove(id);
            }
        }
    }

    internal int AddFetch(int resolveId, int rejectId)
    {
        var id = Interlocked.Increment(ref _nextFetchId);
        lock (_fetchLock)
            _fetchCallbacks[id] = (resolveId, rejectId);
        return id;
    }

    internal void CompleteFetch(int id, FetchResult result)
    {
        lock (_fetchLock)
            _fetchResults[id] = result;
    }

    private void ProcessCompletedFetches()
    {
        List<(int id, FetchResult result)> completed;
        lock (_fetchLock)
        {
            completed = _fetchResults.Select(kv => (kv.Key, kv.Value)).ToList();
            _fetchResults.Clear();
        }

        foreach (var (id, result) in completed)
        {
            lock (_fetchLock)
            {
                if (!_fetchCallbacks.TryGetValue(id, out var cbs)) continue;
                _fetchCallbacks.Remove(id);

                try
                {
                    if (result.Success)
                    {
                        var jsonObj = new JsonObject
                        {
                            ["ok"] = (JsonNode?)JsonValue.Create(result.Status >= 200 && result.Status < 300),
                            ["status"] = (JsonNode?)JsonValue.Create(result.Status),
                            ["statusText"] = (JsonNode?)JsonValue.Create(result.StatusText ?? ""),
                            ["data"] = (JsonNode?)JsonValue.Create(result.Data ?? ""),
                            ["headers"] = result.Headers != null
                                ? JsonSerializer.SerializeToNode(result.Headers, UpBrowserJsonContext.Default.DictionaryStringString)
                                : null
                        };
                        var json = jsonObj.ToJsonString();
                        _adapter?.Execute($"__g_invoke({cbs.resolveId}, JSON.parse('{EscapeJsString(json)}'))");
                    }
                    else
                    {
                        _adapter?.Execute($"__g_invoke({cbs.rejectId}, '{EscapeJsString(result.Error ?? "Unknown error")}')");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fetch] callback error: {ex.Message}");
                }
            }
        }
    }

    private static string GetSetupScript()
    {
        return @"
            var window = this;
            var globalThis = this;
            var self = this;

            function setTimeout(fn, ms) {
                var id = __g_store(fn);
                return __upbrowser.setTimeout(id, ms || 0);
            }
            function setInterval(fn, ms) {
                var id = __g_store(fn);
                return __upbrowser.setInterval(id, ms || 0);
            }
            function clearTimeout(id) { __upbrowser.clearTimeout(id); }
            function clearInterval(id) { __upbrowser.clearInterval(id); }
            function requestAnimationFrame(fn) { return setTimeout(fn, 16); }
            function cancelAnimationFrame(id) { return clearTimeout(id); }
            function alert(msg) { __win.alert(msg); }
            function confirm(msg) { return __win.confirm(msg); }
            function prompt(msg, def) { return __win.prompt(msg, def); }
            function decodeURI(str) { return __upbrowser.decodeURI(str); }
            function decodeURIComponent(str) { return __upbrowser.decodeURIComponent(str); }
            function encodeURI(str) { return __upbrowser.encodeURI(str); }
            function encodeURIComponent(str) { return __upbrowser.encodeURIComponent(str); }
            function parseInt(s, r) { return __upbrowser.parseInt(s, r); }
            function parseFloat(s) { return __upbrowser.parseFloat(s); }
            function isNaN(v) { return __upbrowser.isNaN(v); }
            function isFinite(v) { return __upbrowser.isFinite(v); }
            function escape(str) { return __upbrowser.escape(str); }
            function unescape(str) { return __upbrowser.unescape(str); }
            function atob(str) { return __upbrowser.atob(str); }
            function btoa(str) { return __upbrowser.btoa(str); }

            function fetch(url, opts) {
                return new Promise(function(resolve, reject) {
                    var rid = __g_store(resolve);
                    var rjid = __g_store(reject);
                    __upbrowser._fetch(url, JSON.stringify(opts || {}), rid, rjid);
                });
            }

            function XMLHttpRequest() {
                return __upbrowser.createXMLHttpRequest();
            }

            function URL(url, base) {
                return __upbrowser.createURL(url, base || '');
            }

            function URLSearchParams(query) {
                return __upbrowser.createURLSearchParams(query || '');
            }

            function Image(width, height) {
                var img = document.createElement('img');
                if (width !== undefined) img.width = width;
                if (height !== undefined) img.height = height;
                return img;
            }

            window.addEventListener = function(type, listener, options) {
                if (document && document.addEventListener) {
                    document.addEventListener(type, listener, options);
                }
            };
            window.removeEventListener = function(type, listener, options) {
                if (document && document.removeEventListener) {
                    document.removeEventListener(type, listener, options);
                }
            };

            Object.defineProperty(window, 'innerWidth', { configurable: true, get: function() { return __upbrowser.innerWidth(); } });
            Object.defineProperty(window, 'innerHeight', { configurable: true, get: function() { return __upbrowser.innerHeight(); } });
            Object.defineProperty(window, 'outerWidth', { configurable: true, get: function() { return __upbrowser.innerWidth(); } });
            Object.defineProperty(window, 'outerHeight', { configurable: true, get: function() { return __upbrowser.innerHeight(); } });
            Object.defineProperty(window, 'devicePixelRatio', { configurable: true, get: function() { return __upbrowser.devicePixelRatio(); } });
            Object.defineProperty(window, 'pageXOffset', { configurable: true, get: function() { return __upbrowser.scrollX(); } });
            Object.defineProperty(window, 'pageYOffset', { configurable: true, get: function() { return __upbrowser.scrollY(); } });
            Object.defineProperty(window, 'scrollX', { configurable: true, get: function() { return __upbrowser.scrollX(); } });
            Object.defineProperty(window, 'scrollY', { configurable: true, get: function() { return __upbrowser.scrollY(); } });

            window.scrollTo = function(x, y) { __upbrowser.scrollTo(x || 0, y || 0); };
            window.scrollBy = function(x, y) { __upbrowser.scrollBy(x || 0, y || 0); };
            window.scroll = window.scrollTo;
            window.getComputedStyle = function(el) { return document.getComputedStyle(el); };
            window.matchMedia = function(query) { return { matches: true, media: query, addEventListener: function(){}, removeEventListener: function(){} }};
            window.open = function(url, name, features) {
                if (url) location.href = url;
                return window;
            };
            window.close = function() {};
            window.print = function() {};
            window.stop = function() {};
            window.focus = function() {};
            window.blur = function() {};
            window.moveBy = function(x, y) {};
            window.moveTo = function(x, y) {};
            window.resizeBy = function(x, y) {};
            window.resizeTo = function(x, y) {};
            window.postMessage = function(message, targetOrigin, transfer) {};
            window.getSelection = function() { return null; };
            window.requestIdleCallback = function(cb, opts) { return setTimeout(cb, 50); };
            window.cancelIdleCallback = function(id) { clearTimeout(id); };

            Object.defineProperty(window, 'closed', { configurable: true, get: function() { return false; } });
            Object.defineProperty(window, 'name', { configurable: true, get: function() { return ''; }, set: function(v) {} });
            Object.defineProperty(window, 'opener', { configurable: true, get: function() { return null; } });
            Object.defineProperty(window, 'parent', { configurable: true, get: function() { return window; } });
            window.self = window;
            Object.defineProperty(window, 'top', { configurable: true, get: function() { return window; } });
            Object.defineProperty(window, 'frames', { configurable: true, get: function() { return window; } });
            Object.defineProperty(window, 'length', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'status', { configurable: true, get: function() { return ''; }, set: function(v) {} });
            Object.defineProperty(window, 'defaultStatus', { configurable: true, get: function() { return ''; }, set: function(v) {} });
            Object.defineProperty(window, 'screenLeft', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenTop', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenX', { configurable: true, get: function() { return 0; } });
            Object.defineProperty(window, 'screenY', { configurable: true, get: function() { return 0; } });

            function CustomEvent(type, init) {
                var evt = document.createEvent('customevent');
                evt.type = type;
                if (init) {
                    if (init.detail !== undefined) evt.detail = init.detail;
                    if (init.bubbles !== undefined) evt.bubbles = init.bubbles;
                    if (init.cancelable !== undefined) evt.cancelable = init.cancelable;
                }
                return evt;
            }

            function MouseEvent(type, init) {
                var evt = document.createEvent('mouseevent');
                evt.type = type;
                if (init) {
                    if (init.bubbles !== undefined) evt.bubbles = init.bubbles;
                    if (init.cancelable !== undefined) evt.cancelable = init.cancelable;
                    if (init.detail !== undefined) evt.detail = init.detail;
                    if (init.clientX !== undefined) evt.clientX = init.clientX;
                    if (init.clientY !== undefined) evt.clientY = init.clientY;
                    if (init.screenX !== undefined) evt.screenX = init.screenX;
                    if (init.screenY !== undefined) evt.screenY = init.screenY;
                    if (init.button !== undefined) evt.button = init.button;
                    if (init.ctrlKey !== undefined) evt.ctrlKey = init.ctrlKey;
                    if (init.shiftKey !== undefined) evt.shiftKey = init.shiftKey;
                    if (init.altKey !== undefined) evt.altKey = init.altKey;
                    if (init.metaKey !== undefined) evt.metaKey = init.metaKey;
                }
                return evt;
            }

            if (typeof Promise !== 'undefined') {
                if (!Promise.allSettled) {
                    Promise.allSettled = function(promises) {
                        return Promise.all(promises.map(function(p) {
                            return Promise.resolve(p).then(
                                function(v) { return { status: 'fulfilled', value: v }; },
                                function(e) { return { status: 'rejected', reason: e }; }
                            );
                        }));
                    };
                }
                if (!Promise.any) {
                    Promise.any = function(promises) {
                        return new Promise(function(resolve, reject) {
                            var errors = [];
                            var count = 0;
                            promises.forEach(function(p, i) {
                                Promise.resolve(p).then(resolve, function(e) {
                                    errors[i] = e;
                                    count++;
                                    if (count === promises.length) reject(new Error('All promises rejected'));
                                });
                            });
                        });
                    };
                }
            }
        ";
    }

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}

    public class FetchOptions
    {
        public string? Method { get; set; }
        public string? Body { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
    }

public class FetchResult
{
    public bool Success { get; set; }
    public string? Data { get; set; }
    public int Status { get; set; }
    public string? StatusText { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

internal class TimerInfo
{
    public int CallbackId { get; set; }
    public long DueTime { get; set; }
    public int Interval { get; set; }
}

public class WindowHost
{
    private readonly JavaScriptEngine _engine;

    public WindowHost(JavaScriptEngine engine) => _engine = engine;

    public void alert(object? message)
    {
        var msg = message?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            _engine.ShowDialog(msg, "alert");
        }
        else
        {
            Console.WriteLine($"[Alert] {msg}");
        }
    }

    public bool confirm(object? message)
    {
        var msg = message?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            var result = _engine.ShowDialog(msg, "confirm");
            return result == "true";
        }
        else
        {
            Console.Write($"[Confirm] {msg} (y/N): ");
            var key = Console.ReadLine()?.Trim().ToLowerInvariant();
            return key == "y" || key == "yes";
        }
    }

    public string? prompt(object? message, object? defaultValue)
    {
        var msg = message?.ToString() ?? "";
        var def = defaultValue?.ToString() ?? "";
        if (_engine.ShowDialog != null)
        {
            var result = _engine.ShowDialog(msg, "prompt:" + def);
            return result ?? def;
        }
        else
        {
            Console.Write($"[Prompt] {msg} [{def}]: ");
            var input = Console.ReadLine();
            return string.IsNullOrEmpty(input) ? def : input;
        }
    }
}

public class UpBrowserBuiltins
{
    private readonly JavaScriptEngine _engine;

    public UpBrowserBuiltins(JavaScriptEngine engine) => _engine = engine;

    public int setTimeout(int cbId, int ms) => _engine.SetTimer(cbId, ms, false);
    public int setInterval(int cbId, int ms) => _engine.SetTimer(cbId, ms, true);
    public void clearTimeout(int id) => _engine.ClearTimer(id);
    public void clearInterval(int id) => _engine.ClearTimer(id);

    public string decodeURIComponent(string str) => Uri.UnescapeDataString(str ?? "null");
    public string encodeURIComponent(string str) => Uri.EscapeDataString(str ?? "null");
    public string escape(string str) => System.Net.WebUtility.UrlEncode(str ?? "null");
    public string unescape(string str) => System.Net.WebUtility.UrlDecode(str ?? "null");
    public string atob(string str) => System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(str ?? "null"));
    public string btoa(string str) => System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(str ?? "null"));
    public Func<int>? GetInnerWidth { get; set; }
    public Func<int>? GetInnerHeight { get; set; }
    public Func<double>? GetDevicePixelRatio { get; set; }
    public Func<int>? GetScrollX { get; set; }
    public Func<int>? GetScrollY { get; set; }
    public Action<int, int>? OnScrollTo { get; set; }
    public Action<int, int>? OnScrollBy { get; set; }

    public int innerWidth() => GetInnerWidth?.Invoke() ?? 1024;
    public int innerHeight() => GetInnerHeight?.Invoke() ?? 768;
    public double devicePixelRatio() => GetDevicePixelRatio?.Invoke() ?? 1.0;
    public int scrollX() => GetScrollX?.Invoke() ?? 0;
    public int scrollY() => GetScrollY?.Invoke() ?? 0;
    public void scrollTo(int x, int y) { OnScrollTo?.Invoke(x, y); }
    public void scrollBy(int x, int y) { OnScrollBy?.Invoke(x, y); }
    public XMLHttpRequestHost createXMLHttpRequest() => new XMLHttpRequestHost(_engine);
    public URLHost createURL(string url, string? baseUrl) => new URLHost(url, baseUrl);
    public URLSearchParamsHost createURLSearchParams(string query) => new URLSearchParamsHost(query);

    public int _fetch(string url, string optionsJson, int resolveId, int rejectId)
    {
        var id = _engine.AddFetch(resolveId, rejectId);

        Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

                var options = string.IsNullOrEmpty(optionsJson) ? null :
                    JsonSerializer.Deserialize(optionsJson, UpBrowserJsonContext.Default.FetchOptions);

                var method = options?.Method ?? "GET";
                var req = new HttpRequestMessage(new HttpMethod(method), url);

                string? contentType = null;
                if (options?.Headers != null)
                {
                    foreach (var h in options.Headers)
                    {
                        if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                            contentType = h.Value?.ToString();
                        else
                            req.Headers.TryAddWithoutValidation(h.Key, h.Value?.ToString() ?? "");
                    }
                }

                if (options?.Body != null && (method == "POST" || method == "PUT" || method == "PATCH"))
                {
                    req.Content = new StringContent(options.Body, Encoding.UTF8);
                    if (contentType != null)
                        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }

                var response = await http.SendAsync(req);
                var text = await response.Content.ReadAsStringAsync();

                var headers = new Dictionary<string, string>();
                foreach (var h in response.Headers)
                    headers[h.Key] = string.Join(", ", h.Value);

                _engine.CompleteFetch(id, new FetchResult
                {
                    Success = true,
                    Data = text,
                    Status = (int)response.StatusCode,
                    StatusText = response.ReasonPhrase ?? "",
                    Headers = headers
                });
            }
            catch (Exception ex)
            {
                _engine.CompleteFetch(id, new FetchResult { Success = false, Error = ex.Message });
            }
        });

        return id;
    }

    public string decodeURI(string str) => Uri.UnescapeDataString(str);
    public string encodeURI(string str) => Uri.EscapeDataString(str);
    public int parseInt(string s, object? radix = null)
    {
        var r = radix is int ri ? ri : 10;
        try { return Convert.ToInt32(s, r); }
        catch { return int.TryParse(s, out var res) ? res : 0; }
    }
    public double parseFloat(string s) => double.TryParse(s, out var r) ? r : double.NaN;
    public bool isNaN(object? v)
    {
        if (v == null) return true;
        if (v is double dv) return double.IsNaN(dv);
        if (v is float fv) return double.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed)) return double.IsNaN(parsed);
        return true;
    }
    public bool isFinite(object? v)
    {
        if (v == null) return false;
        if (v is double dv) return !double.IsInfinity(dv) && !double.IsNaN(dv);
        if (v is float fv) return !float.IsInfinity(fv) && !float.IsNaN(fv);
        if (double.TryParse(v.ToString(), out var parsed))
            return !double.IsInfinity(parsed) && !double.IsNaN(parsed);
        return false;
    }
}

public class ConsoleHost
{
    private readonly Dictionary<string, int> _counters = new();
    private readonly Stack<(string label, long time)> _timers = new();
    private int _groupLevel = 0;
    private JsDevToolsConsole? _devToolsConsole;

    public JsDevToolsConsole? DevToolsConsole
    {
        get => _devToolsConsole;
        set => _devToolsConsole = value;
    }

    public void log(params object?[] args)
    {
        _devToolsConsole?.Log("log", args);
        WriteLine("[JS]", args);
    }
    public void error(params object?[] args)
    {
        _devToolsConsole?.Log("error", args);
        WriteLine("[JS Error]", args);
    }
    public void warn(params object?[] args)
    {
        _devToolsConsole?.Log("warn", args);
        WriteLine("[JS Warning]", args);
    }
    public void info(params object?[] args)
    {
        _devToolsConsole?.Log("info", args);
        WriteLine("[JS Info]", args);
    }
    public void debug(params object?[] args)
    {
        _devToolsConsole?.Log("debug", args);
        WriteLine("[JS Debug]", args);
    }
    public void trace(params object?[] args)
    {
        _devToolsConsole?.Log("trace", args);
        WriteLine("[JS Trace]", args);
        Console.WriteLine(new System.Diagnostics.StackTrace(true).ToString());
    }
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "dir is a debug console helper; object is user-provided")]
    public void dir(object? obj)
    {
        if (obj == null)
        {
            var msg = "null";
            _devToolsConsole?.Log("log", msg);
            Console.WriteLine(msg);
            return;
        }
        if (obj is System.Collections.IDictionary dict)
        {
            foreach (var key in dict.Keys)
                Console.WriteLine($"  {key}: {dict[key]}");
        }
        else
        {
            foreach (var prop in obj.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                try { Console.WriteLine($"  {prop.Name}: {prop.GetValue(obj)}"); }
                catch { }
            }
        }
    }
    public void table(params object?[] args)
    {
        if (args.Length == 0) return;
        if (args[0] is System.Collections.IEnumerable enumerable && args[0] is not string)
        {
            Console.WriteLine("[Table output]");
            foreach (var item in enumerable)
                Console.WriteLine($"  {item}");
        }
        else
        {
            WriteLine("[Table]", args);
        }
    }
    public void group(params object?[] args)
    {
        _groupLevel++;
        var prefix = new string(' ', _groupLevel * 2);
        var msg = $"{prefix}Group: {string.Join(" ", args.Select(a => a?.ToString() ?? "null"))}";
        Console.WriteLine(msg);
    }
    public void groupEnd()
    {
        if (_groupLevel > 0) _groupLevel--;
    }
    public void count(string? label = null)
    {
        var key = label ?? "default";
        if (!_counters.ContainsKey(key))
            _counters[key] = 0;
        _counters[key]++;
        var msg = $"{key}: {_counters[key]}";
        Console.WriteLine(msg);
    }
    public void countReset(string? label = null)
    {
        var key = label ?? "default";
        _counters[key] = 0;
        Console.WriteLine($"{key}: 0");
    }
    public void time(string? label = null)
    {
        var key = label ?? "default";
        _timers.Push((key, Environment.TickCount64));
        Console.WriteLine($"Timer '{key}' started");
    }
    public void timeLog(string? label = null, params object?[] args)
    {
        var key = label ?? "default";
        long startTime = 0;
        bool found = false;
        foreach (var (l, t) in _timers)
        {
            if (l == key) { startTime = t; found = true; break; }
        }
        if (found)
        {
            var elapsed = Environment.TickCount64 - startTime;
            var msg = $"{key}: {elapsed}ms";
            if (args.Length > 0) msg += " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined"));
            Console.WriteLine(msg);
        }
        else
        {
            Console.WriteLine($"Timer '{key}' not found");
        }
    }
    public void timeEnd(string? label = null)
    {
        var key = label ?? "default";
        while (_timers.Count > 0)
        {
            var (timerLabel, startTime) = _timers.Pop();
            if (timerLabel == key)
            {
                var elapsed = Environment.TickCount64 - startTime;
                Console.WriteLine($"Timer '{key}': {elapsed}ms");
                return;
            }
        }
        Console.WriteLine($"Timer '{key}' not found");
    }
    public void timeStamp(string? label = null)
    {
        Console.WriteLine($"Timestamp: {label ?? "unnamed"} at {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}ms");
    }
    public void assert(bool condition, params object?[] args)
    {
        if (!condition)
        {
            _devToolsConsole?.Log("error", args.Prepend("Assertion failed: ").ToArray());
            WriteLine("[JS Assertion Failed]", args);
        }
    }
    public void Reset()
    {
        _counters.Clear();
        _timers.Clear();
        _groupLevel = 0;
        _devToolsConsole?.Clear();
    }

    public void clear()
    {
        _devToolsConsole?.Clear();
        Console.Clear();
    }

    private void WriteLine(string prefix, object?[] args)
    {
        var indent = new string(' ', _groupLevel * 2);
        Console.WriteLine(indent + prefix + " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }
}

public class NavigatorHost
{
    public string userAgent => "UpBrowser/1.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    public string appCodeName => "Mozilla";
    public string appName => "UpBrowser";
    public string appVersion => "1.0";
    public string platform => "Win32";
    public string product => "Gecko";
    public string language => "zh-CN";
    public string[] languages => new[] { "zh-CN", "en" };
    public bool cookieEnabled => true;
    public bool onLine => true;
    public bool javaEnabled() => false;
}

public class LocationHost
{
    public Action<string>? OnNavigate { get; set; }
    public Action? OnReload { get; set; }
    
    public string href { get; set; } = "upbrowser://local";
    public string protocol { get; set; } = "upbrowser:";
    public string hostname { get; set; } = "local";
    public string host { get; set; } = "local";
    public string port { get; set; } = "";
    public string pathname { get; set; } = "/";
    public string search { get; set; } = "";
    public string hash { get; set; } = "";
    public string origin { get; set; } = "upbrowser://local";

    public void assign(string url)
    {
        href = url;
        OnNavigate?.Invoke(url);
    }
    public void replace(string url)
    {
        href = url;
        OnNavigate?.Invoke(url);
    }
    public void reload()
    {
        OnReload?.Invoke();
    }
}

public class HistoryHost
{
    private readonly List<object?> _stack = new();
    private int _currentIndex = -1;
    private readonly LocationHost _location;

    public HistoryHost(LocationHost location) => _location = location;

    public int length => Math.Max(0, _stack.Count);
    public object? state => _currentIndex >= 0 && _currentIndex < _stack.Count ? _stack[_currentIndex] : null;

    public void back() { if (_currentIndex > 0) _currentIndex--; }
    public void forward() { if (_currentIndex < _stack.Count - 1) _currentIndex++; }
    public void go(int delta) { _currentIndex = Math.Max(0, Math.Min(_stack.Count - 1, _currentIndex + delta)); }
    public void pushState(object? state, string title, string? url)
    {
        while (_stack.Count > _currentIndex + 1) _stack.RemoveAt(_stack.Count - 1);
        _stack.Add(state);
        _currentIndex = _stack.Count - 1;
        if (!string.IsNullOrEmpty(url))
        {
            _location.assign(url);
        }
    }
    public void replaceState(object? state, string title, string? url)
    {
        if (_currentIndex >= 0 && _currentIndex < _stack.Count)
            _stack[_currentIndex] = state;
        if (!string.IsNullOrEmpty(url))
        {
            _location.replace(url);
        }
    }
}

public class ScreenHost
{
    public int width => 1920;
    public int height => 1080;
    public int availWidth => 1920;
    public int availHeight => 1040;
    public int availTop => 0;
    public int availLeft => 0;
    public int colorDepth => 24;
    public int pixelDepth => 24;
    public int top => 0;
    public int left => 0;
    public object? orientation => new ScreenOrientationHost();
}

public class ScreenOrientationHost
{
    public string type => "landscape-primary";
    public string angle => "0";
    public void lock_(string orientation) { }
    public void unlock() { }
}

public class StorageHost
{
    private readonly Dictionary<string, string> _data = new();
    private readonly string _name;

    public StorageHost(string name = "storage")
    {
        _name = name;
    }

    public string? getItem(string key) => _data.GetValueOrDefault(key);
    public void setItem(string key, string value) => _data[key] = value;
    public void removeItem(string key) => _data.Remove(key);
    public void clear() => _data.Clear();
    public string? key(int index) => index >= 0 && index < _data.Count ? _data.ElementAt(index).Key : null;
    public int length => _data.Count;
}