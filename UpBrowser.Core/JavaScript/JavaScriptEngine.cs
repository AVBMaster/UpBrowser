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

    [ThreadStatic]
    internal static JavaScriptEngine? Current;

    public event Action? OnDomChanged;
    public Func<string, string?, string?>? ShowDialog { get; set; }
    public bool HasTimers { get { lock (_timersLock) return _timers.Count > 0; } }

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

        _engine.EmbedHostObject("console", new ConsoleHost());
        _engine.EmbedHostObject("__upbrowser", new UpBrowserBuiltins(this));

        _engine.Execute(@"
            var window = this;
            var globalThis = this;
            var self = this;

            // Stub DOM objects
            var document = {
                createElement: function(tag) { return {}; },
                getElementById: function(id) { return null; },
                getElementsByTagName: function(tag) { return []; },
                createTextNode: function(text) { return {}; },
                querySelector: function(sel) { return null; },
                querySelectorAll: function(sel) { return []; },
                body: null,
                head: null,
                documentElement: null
            };
            var navigator = {
                userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
                cookieEnabled: true,
                platform: 'Win32',
                appName: 'Netscape',
                appVersion: '5.0',
                language: 'zh-CN'
            };
            var location = { href: '', protocol: 'https:', host: '', hostname: '', port: '', pathname: '/', search: '', hash: '', assign: function(u) {}, replace: function(u) {}, reload: function() {} };
            var history = { length: 0, state: null, back: function() {}, forward: function() {}, go: function(d) {}, pushState: function(s,t,u) {}, replaceState: function(s,t,u) {} };
            var screen = { width: 1920, height: 1080, availWidth: 1920, availHeight: 1040, colorDepth: 24, pixelDepth: 24 };
            var localStorage = { getItem: function(k) { return null; }, setItem: function(k,v) {}, removeItem: function(k) {}, clear: function() {}, length: 0 };
            var sessionStorage = { getItem: function(k) { return null; }, setItem: function(k,v) {}, removeItem: function(k) {}, clear: function() {}, length: 0 };
            window.define = function() {};
            window.define.amd = {};
            window.F = { _setMod: function() {}, _fileMap: function() {}, module: function() {}, addLog: function() {} };
            window.$ = function(sel) { return { on: function() {}, off: function() {}, val: function() { return ''; }, html: function() { return ''; }, text: function() { return ''; }, attr: function() { return ''; }, data: function() { return {}; }, addClass: function() {}, removeClass: function() {}, toggleClass: function() {}, css: function() {}, show: function() {}, hide: function() {}, fadeIn: function() {}, fadeOut: function() {}, ajax: function() {}, get: function() {}, post: function() {}, Deferred: function() { return { resolve: function() {}, reject: function() {}, promise: function() {} }; }, extend: function() { return {}; }, each: function() {}, map: function() {} }; };
            window.jQuery = window.$;

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
            function parseInt(s, r) { return __upbrowser.parseInt(s, r === undefined ? 10 : r); }
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


Object.defineProperty(this, 'addEventListener', { value: function(type, cb) { /* no-op */ } });
            Object.defineProperty(this, 'removeEventListener', { value: function(type, cb) { /* no-op */ } });
            Object.defineProperty(this, 'dispatchEvent', { value: function(evt) { return true; } });
            this.name = '';
this.opera = undefined;
            this.performance = { timing: {}, navigation: {}, now: function() { return 0; } };
        ");

        _engine.EmbedHostObject("__win", new WindowHost(this));
        _engine.EmbedHostObject("navigator", new NavigatorHost());
        _engine.EmbedHostObject("location", new LocationHost());
        _engine.EmbedHostObject("history", new HistoryHost());
        _engine.EmbedHostObject("screen", new ScreenHost());
        _engine.EmbedHostObject("localStorage", new StorageHost());
        _engine.EmbedHostObject("sessionStorage", new StorageHost());

        _engine.Execute(@"
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
            _engine.EmbedHostObject("_document", _documentHost);
            _engine.Execute(@"
                (function() {
                    var overrides = {};
                    document = new Proxy(_document, {
                        get: function(t, p) {
                            if (p === 'then' || p === 'toJSON') return undefined;
                            if (p in overrides) return overrides[p];
                            var v = t[p];
                            if (typeof v === 'function' && v.call) {
                                return function() { return v.apply(t, arguments); };
                            }
                            return v;
                        },
                        set: function(t, p, v) {
                            try { t[p] = v; }
                            catch(e) { overrides[p] = v; }
                            return true;
                        }
                    });
                })();
            ");
        }

        MarkDirty();
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

    public void DispatchEvent(DomElement element, string eventType)
    {
        var targetHost = new ElementHost(element);
        var evt = new ScriptEvent(eventType, targetHost);
        targetHost.DispatchEvent(evt);
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
        Current = this;
        try
        {
            if (_engine != null)
                _engine.Evaluate($"__g_invoke({timerId})");
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

    public string decodeURIComponent(string str) => Uri.UnescapeDataString(str);
    public string encodeURIComponent(string str) => Uri.EscapeDataString(str);
    public string escape(string str) => System.Net.WebUtility.UrlEncode(str);
    public string unescape(string str) => System.Net.WebUtility.UrlDecode(str);
    public string atob(string str) => System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(str));
    public string btoa(string str) => System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(str));
    public int innerWidth() => 1024;
    public int innerHeight() => 768;
    public double devicePixelRatio() => 1.0;
    public int scrollX() => 0;
    public int scrollY() => 0;
    public void scrollTo(int x, int y) { }
    public void scrollBy(int x, int y) { }
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
    public int parseInt(string s, int? radix)
    {
        var r = radix ?? 10;
        try { return Convert.ToInt32(s, r); }
        catch { return int.TryParse(s, out var result) ? result : 0; }
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
    public bool javaEnabled() => false;
    public object[] mimeTypes => Array.Empty<object>();
    public object[] plugins => Array.Empty<object>();
    public int maxTouchPoints => 0;
    public int hardwareConcurrency => 4;
}

public class LocationHost
{
    internal static LocationHost Instance { get; private set; } = new();
    
    public string href { get; set; } = "upbrowser://local";
    public string protocol { get; set; } = "upbrowser:";
    public string hostname { get; set; } = "local";
    public string host { get; set; } = "local";
    public string port { get; set; } = "";
    public string pathname { get; set; } = "/";
    public string search { get; set; } = "";
    public string hash { get; set; } = "";
    public string origin { get; set; } = "upbrowser://local";

    public void assign(string url) { href = url; }
    public void replace(string url) { href = url; }
    public void reload() { }
}

public class HistoryHost
{
    private readonly List<object?> _stack = new();
    private int _currentIndex = -1;

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
            var loc = LocationHost.Instance;
            loc.assign(url);
        }
    }
    public void replaceState(object? state, string title, string? url)
    {
        if (_currentIndex >= 0 && _currentIndex < _stack.Count)
            _stack[_currentIndex] = state;
        if (!string.IsNullOrEmpty(url))
        {
            var loc = LocationHost.Instance;
            loc.replace(url);
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
