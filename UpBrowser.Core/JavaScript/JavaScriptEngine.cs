using System.Text;
using System.Text.Json;
using JavaScriptEngineSwitcher.Core;
using DomDocument = UpBrowser.Core.Dom.Document;
using DomElement = UpBrowser.Core.Dom.Element;

namespace UpBrowser.Core.JavaScript;

public class JavaScriptEngine : IDisposable
{
    private IJsEngine? _engine;
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

    private int _nextCbId = 1;
    private readonly object _cbLock = new();
    private LocationHost? _locationHost;
    private UpBrowserBuiltins? _builtins;

    [ThreadStatic]
    public static JavaScriptEngine? Current;

    public LocationHost? LocationHost => _locationHost;
    public UpBrowserBuiltins? Builtins => _builtins;
    public DocumentHost? DocumentHost => _documentHost;

    public event Action? OnDomChanged;
    public Func<string, string?, string?>? ShowDialog { get; set; }
    public bool HasTimers { get { lock (_timersLock) return _timers.Count > 0; } }
    public int TimerCount { get { lock (_timersLock) return _timers.Count; } }

    /// <summary>Returns approximate JS heap usage in KB, or 0 if unavailable.</summary>
    public int GetHeapSizeKB()
    {
        // JavaScript engines in .NET don't typically expose heap size.
        // Return managed heap delta as a rough proxy.
        return (int)(GC.GetTotalMemory(false) / 1024);
    }

    internal IJsEngine? InnerEngine => _engine;

    public JavaScriptEngine()
    {
        JsEngineConfig.Initialize();
        _engine = JsEngineConfig.CreateEngine();
        SetupGlobals();
    }

    public JavaScriptEngine(IJsEngine existingEngine)
    {
        _engine = existingEngine;
        SetupGlobals();
    }

    internal int AllocCallbackId()
    {
        lock (_cbLock) return _nextCbId++;
    }

    private void SetupGlobals()
    {
        if (_engine == null) return;

        _engine.Evaluate(JsCallbackStore.JsSetup);

        // Embed ALL host objects BEFORE the setup script that references them
        _engine.EmbedHostObject("console", new ConsoleHost());
        _builtins = new UpBrowserBuiltins(this);
        _engine.EmbedHostObject("__upbrowser", _builtins);
        _engine.EmbedHostObject("__win", new WindowHost(this));
        _engine.EmbedHostObject("navigator", new NavigatorHost());
        _locationHost = new LocationHost();
        _engine.EmbedHostObject("location", _locationHost);
        _engine.EmbedHostObject("history", new HistoryHost(_locationHost));
        _engine.EmbedHostObject("screen", new ScreenHost());
        _engine.EmbedHostObject("localStorage", new StorageHost());
        _engine.EmbedHostObject("sessionStorage", new StorageHost());

        // Embed a minimal stub document so 'var document = document;' resolves correctly
        // Real document is set later via LoadDocument()
        _engine.EmbedHostObject("document", new Dictionary<string, object?>());

        _engine.Execute(@"
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

            // window is the global object (= this), and EmbedHostObject sets properties
            // directly on the global object, so window[name] === name automatically.

            Object.defineProperty(window, 'innerWidth', { get: function() { return __upbrowser.innerWidth(); } });
            Object.defineProperty(window, 'innerHeight', { get: function() { return __upbrowser.innerHeight(); } });
            Object.defineProperty(window, 'outerWidth', { get: function() { return __upbrowser.innerWidth(); } });
            Object.defineProperty(window, 'outerHeight', { get: function() { return __upbrowser.innerHeight(); } });
            Object.defineProperty(window, 'devicePixelRatio', { get: function() { return __upbrowser.devicePixelRatio(); } });
            Object.defineProperty(window, 'pageXOffset', { get: function() { return __upbrowser.scrollX(); } });
            Object.defineProperty(window, 'pageYOffset', { get: function() { return __upbrowser.scrollY(); } });
            Object.defineProperty(window, 'scrollX', { get: function() { return __upbrowser.scrollX(); } });
            Object.defineProperty(window, 'scrollY', { get: function() { return __upbrowser.scrollY(); } });

            window.scrollTo = function(x, y) { __upbrowser.scrollTo(x || 0, y || 0); };
            window.scrollBy = function(x, y) { __upbrowser.scrollBy(x || 0, y || 0); };
            window.scroll = window.scrollTo;
            window.getComputedStyle = function(el) { return document.getComputedStyle(el); };
            window.matchMedia = function(query) { return { matches: true, media: query, addEventListener: function(){}, removeEventListener: function(){} }};
            window.open = function(url, name, features) {
                if (url) location.href = url;
                return window;
            };

            function CustomEvent(type, detail) {
                var evt = document.createEvent('customevent');
                evt.type = type;
                if (detail) evt.detail = detail;
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
        ");
    }

    public void LoadDocument(DomDocument document)
    {
        _currentDocument = document;
        _documentHost = new DocumentHost(document);

        if (_engine != null)
        {
            // Clear old JS callbacks to prevent memory leak across page navigations
            ClearState();

            _engine.EmbedHostObject("document", _documentHost);
        }

        MarkDirty();
    }

    public void ClearState()
    {
        if (_engine == null) return;

        // Clear all stored callbacks from __g_cbs to prevent memory leaks
        try { _engine.Evaluate("__g_cbs = {}; __g_cbid = 0;"); }
        catch { }

        // Clear all timers
        lock (_timersLock)
        {
            foreach (var info in _timers.Values)
                RemoveCallback(info.CallbackId);
            _timers.Clear();
        }

        // Clear all fetch callbacks
        lock (_fetchLock)
        {
            foreach (var cbs in _fetchCallbacks.Values)
            {
                RemoveCallback(cbs.resolveId);
                RemoveCallback(cbs.rejectId);
            }
            _fetchCallbacks.Clear();
            _fetchResults.Clear();
        }

        // Reset ConsoleHost counters and timers
        if (_engine != null)
        {
            try
            {
                var console = _engine.Evaluate("console");
                if (console is ConsoleHost ch)
                    ch.Reset();
            }
            catch { }
        }
    }

    public void Execute(string code)
    {
        if (_engine == null || string.IsNullOrEmpty(code)) return;
        Current = this;
        try
        {
            _engine.Execute(code);
            MarkDirty();
        }
        catch (JsScriptException ex)
        {
            Console.WriteLine($"[JS Error] {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS Error] {ex.Message}");
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression)
    {
        if (_engine == null) return null;
        Current = this;
        try
        {
            return _engine.Evaluate(expression);
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
        if (_engine == null) return null;
        Current = this;
        try
        {
            return _engine.CallFunction(functionName, args);
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
        if (_engine == null || value == null) return;
        _engine.EmbedHostObject(name, value);
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
        var id = AllocCallbackId();

        if (_engine != null)
        {
            var tmpName = $"__tmpc_{id}";
            try
            {
                _engine.EmbedHostObject(tmpName, callback);
                _engine.Evaluate($"__g_cbs[{id}] = {tmpName}; delete {tmpName};");
            }
            catch
            {
                _engine.Evaluate($$"""
                    (function() {
                        var id = {{id}};
                        __g_cbs[id] = function() {};
                    })();
                """);
            }
        }

        return id;
    }

    internal void InvokeCallback(int cbId)
    {
        if (_engine == null) return;
        Current = this;
        try
        {
            _engine.Evaluate($"__g_invoke({cbId})");
        }
        catch { }
        finally { Current = null; }
    }

    internal void InvokeCallbackWith(int cbId, object arg)
    {
        if (_engine == null) return;
        Current = this;

        string jsEventJson;
        if (arg is ScriptEvent evt)
        {
            var dict = new Dictionary<string, object?>
            {
                ["type"] = evt.type,
                ["bubbles"] = evt.bubbles,
                ["cancelable"] = evt.cancelable,
                ["defaultPrevented"] = evt.DefaultPrevented,
                ["timeStamp"] = evt.timeStamp
            };
            jsEventJson = JsonSerializer.Serialize(dict, _jsonOpts);
        }
        else
        {
            jsEventJson = JsonSerializer.Serialize(arg, _jsonOpts);
        }

        try
        {
            _engine.Execute($"__g_invoke({cbId}, JSON.parse('{EscapeJsString(jsEventJson)}'))");
        }
        catch { }
        finally { Current = null; }
    }

    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    internal void RemoveCallback(int cbId)
    {
        if (_engine == null) return;
        try
        {
            _engine.Evaluate($"__g_remove({cbId})");
        }
        catch { }
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
            if (_engine != null)
                _engine.Evaluate($"__g_invoke({cbId})");
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
        _engine?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal int SetTimer(int callbackId, int delayMs, bool recurring)
    {
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
        lock (_timersLock)
        {
            if (_timers.TryGetValue(id, out var info))
            {
                RemoveCallback(info.CallbackId);
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
                        var resp = new Dictionary<string, object?>
                        {
                            ["ok"] = result.Status >= 200 && result.Status < 300,
                            ["status"] = result.Status,
                            ["statusText"] = result.StatusText ?? "",
                            ["data"] = result.Data ?? "",
                            ["headers"] = result.Headers ?? new Dictionary<string, string>()
                        };
                        var json = JsonSerializer.Serialize(resp, _jsonOpts);
                        _engine?.Execute($"__g_invoke({cbs.resolveId}, JSON.parse('{EscapeJsString(json)}'))");
                    }
                    else
                    {
                        _engine?.Execute($"__g_invoke({cbs.rejectId}, '{EscapeJsString(result.Error ?? "Unknown error")}')");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Fetch] callback error: {ex.Message}");
                }
            }
        }
    }
}

public class FetchOptions
{
    public string? Method { get; set; }
    public string? Body { get; set; }
    public Dictionary<string, object?>? Headers { get; set; }
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
                    System.Text.Json.JsonSerializer.Deserialize<FetchOptions>(optionsJson);

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

    public void log(params object?[] args) => WriteLine("[JS]", args);
    public void error(params object?[] args) => WriteLine("[JS Error]", args);
    public void warn(params object?[] args) => WriteLine("[JS Warning]", args);
    public void info(params object?[] args) => WriteLine("[JS Info]", args);
    public void debug(params object?[] args) => WriteLine("[JS Debug]", args);
    public void trace(params object?[] args)
    {
        WriteLine("[JS Trace]", args);
        Console.WriteLine(new System.Diagnostics.StackTrace(true).ToString());
    }
    public void dir(object? obj)
    {
        if (obj == null)
        {
            Console.WriteLine("null");
            return;
        }
        if (obj is System.Collections.IDictionary dict)
        {
            foreach (var key in dict.Keys)
            {
                Console.WriteLine($"  {key}: {dict[key]}");
            }
        }
        else
        {
            foreach (var prop in obj.GetType().GetProperties())
            {
                try
                {
                    Console.WriteLine($"  {prop.Name}: {prop.GetValue(obj)}");
                }
                catch { }
            }
        }
    }
    public void table(params object?[] args)
    {
        if (args.Length == 0) return;
        if (args[0] is System.Collections.IEnumerable enumerable && args[0] is not string)
        {
            Console.WriteLine("[Table output - tabular data]");
            foreach (var item in enumerable)
            {
                Console.WriteLine($"  {item}");
            }
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
        Console.WriteLine($"{prefix}Group: {string.Join(" ", args.Select(a => a?.ToString() ?? "null"))}");
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
        Console.WriteLine($"{key}: {_counters[key]}");
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
            WriteLine("[JS Assertion Failed]", args);
        }
    }
    public void Reset()
    {
        _counters.Clear();
        _timers.Clear();
        _groupLevel = 0;
    }

    public void clear() => Console.Clear();

    private void WriteLine(string prefix, object?[] args)
    {
        var indent = new string(' ', _groupLevel * 2);
        Console.WriteLine(indent + prefix + " " + string.Join(" ", args.Select(a => a?.ToString() ?? "undefined")));
    }
}

public class NavigatorHost
{
    public string userAgent => "UpBrowser/1.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    public string appName => "UpBrowser";
    public string appVersion => "1.0";
    public string platform => "Win32";
    public string language => "zh-CN";
    public string[] languages => new[] { "zh-CN", "en" };
    public bool cookieEnabled => true;
    public bool onLine => true;
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
    public int colorDepth => 24;
    public int pixelDepth => 24;
    public int top => 0;
    public int left => 0;
}

public class StorageHost
{
    private readonly Dictionary<string, string> _data = new();

    public string? getItem(string key) => _data.GetValueOrDefault(key);
    public void setItem(string key, string value) => _data[key] = value;
    public void removeItem(string key) => _data.Remove(key);
    public void clear() => _data.Clear();
    public string? key(int index) => index >= 0 && index < _data.Count ? _data.ElementAt(index).Key : null;
    public int length => _data.Count;
}
