using System.Text.Json;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using UpBrowser.JsEngineProtocol;

namespace UpBrowser.JsEngineHost;

class Program
{
    static async Task Main(string[] args)
    {
        var channelName = "";
        var engineType = "Jint";
        var tabIndex = 0;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--channel=")) channelName = arg[10..];
            else if (arg.StartsWith("--engine=")) engineType = arg[9..];
            else if (arg.StartsWith("--tab=")) tabIndex = int.Parse(arg[6..]);
        }

        if (string.IsNullOrEmpty(channelName))
        {
            Console.Error.WriteLine("JsEngineHost: --channel required");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"[JsEngineHost] Starting channel={channelName} engine={engineType} tab={tabIndex}");

        JsEngineSwitcher.Current.EngineFactories.AddJint();
        JsEngineSwitcher.Current.DefaultEngineName = JsEngineSwitcher.Current.EngineFactories.First().EngineName;
        using var engine = JsEngineSwitcher.Current.CreateDefaultEngine();
        var manager = new EngineManager(engine, tabIndex);

        using var transport = new MmapTransport(channelName);
        try
        {
            await transport.StartServerAsync();
            Console.WriteLine("[JsEngineHost] MMAP connected");
            manager.ExecuteSetup();
            Console.WriteLine("[JsEngineHost] Setup completed");
            await manager.RunAsync(transport);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JsEngineHost] Fatal: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            try { File.WriteAllText("JsEngineHost_error.log", $"{ex}\n{ex.StackTrace}"); } catch { }
        }
        Console.WriteLine("[JsEngineHost] Exiting");
    }
}

class EngineManager
{
    private readonly IJsEngine _engine;
    private readonly int _tabIndex;
    private MmapTransport? _transport;
    private readonly Dictionary<long, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly object _lock = new();
    private long _nextRequestId;
    private CancellationTokenSource? _cts;

    public EngineManager(IJsEngine engine, int tabIndex)
    {
        _engine = engine;
        _tabIndex = tabIndex;
        DefineIpcFunction();
        ExecuteSetup();
    }

    private void DefineIpcFunction()
    {
        // 注册 __ipc 函数，JS 调用时同步发送 IPC 请求到主进程
        _engine.EmbedHostObject("__ipc", new Func<string, string, string>((method, argsJson) =>
        {
            try
            {
                return SendToMain(method, argsJson);
            }
            catch (Exception ex)
            {
                return $"{{\"error\":\"{ex.Message}\"}}";
            }
        }));
    }

    public async Task RunAsync(MmapTransport transport)
    {
        _transport = transport;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
        try { await Task.Delay(-1, _cts.Token); }
        catch (OperationCanceledException) { }
    }

    public void ExecuteSetup()
    {
        var setup = @"
(function() {
    var g = typeof globalThis !== 'undefined' ? globalThis : this;
    g.__g_cbid = 0; g.__g_cbs = {}; g.__g_fnMap = typeof WeakMap !== 'undefined' ? new WeakMap() : {};
    g.__g_store = function(fn) {
        var id = g.__g_fnMap.get ? g.__g_fnMap.get(fn) : undefined;
        if (id !== undefined) return id;
        id = ++g.__g_cbid; g.__g_cbs[id] = fn;
        if (g.__g_fnMap.set) g.__g_fnMap.set(fn, id);
        return id;
    };
    g.__g_invoke = function(id, arg) {
        var fn = g.__g_cbs[id];
        if (fn) { return arg !== undefined ? fn(arg) : fn(); }
    };
    g.__g_remove = function(id) { delete g.__g_cbs[id]; };

    g.console = {
        log: function() { print('[JS Log] ' + Array.prototype.slice.call(arguments).join(' ')); },
        error: function() { print('[JS Error] ' + Array.prototype.slice.call(arguments).join(' ')); },
        warn: function() { print('[JS Warn] ' + Array.prototype.slice.call(arguments).join(' ')); },
        info: function() { print('[JS Info] ' + Array.prototype.slice.call(arguments).join(' ')); },
        debug: function() { print('[JS Debug] ' + Array.prototype.slice.call(arguments).join(' ')); }
    };

    g.setTimeout = function(fn, ms) { return __ipc('setTimeout', JSON.stringify([__g_store(fn), ms||0])); };
    g.setInterval = function(fn, ms) { return __ipc('setInterval', JSON.stringify([__g_store(fn), ms||0])); };
    g.clearTimeout = function(id) { try { __ipc('clearTimeout', JSON.stringify([id])); } catch(e) {} };
    g.clearInterval = function(id) { try { __ipc('clearInterval', JSON.stringify([id])); } catch(e) {} };

    g.__upbrowser = {
        setTimeout: g.setTimeout, setInterval: g.setInterval,
        clearTimeout: g.clearTimeout, clearInterval: g.clearInterval,
        innerWidth: function() { return parseInt(__ipc('innerWidth', '[]')); },
        innerHeight: function() { return parseInt(__ipc('innerHeight', '[]')); },
        scrollTo: function(x,y) { __ipc('scrollTo', JSON.stringify([x||0,y||0])); },
        scrollBy: function(x,y) { __ipc('scrollBy', JSON.stringify([x||0,y||0])); },
        alert: function(msg) { __ipc('alert', JSON.stringify([msg||''])); },
        confirm: function(msg) { return __ipc('confirm', JSON.stringify([msg||''])) === 'true'; },
        prompt: function(msg,def) { return __ipc('prompt', JSON.stringify([msg||'',def||''])); },
        decodeURI: function(s) { return decodeURI(s); },
        encodeURI: function(s) { return encodeURI(s); },
        decodeURIComponent: function(s) { return decodeURIComponent(s); },
        encodeURIComponent: function(s) { return encodeURIComponent(s); },
        parseInt: function(s,r) { return parseInt(s, r||10); },
        parseFloat: function(s) { return parseFloat(s); },
        isNaN: function(v) { return isNaN(v); },
        isFinite: function(v) { return isFinite(v); },
        atob: function(s) { return __ipc('atob', JSON.stringify([s||''])); },
        btoa: function(s) { return __ipc('btoa', JSON.stringify([s||''])); },
        _fetch: function(url, opts, resolveId, rejectId) {
            __ipc('fetch', JSON.stringify([url, opts||'', resolveId, rejectId]));
        },
        createXMLHttpRequest: function() { return __ipc('createXHR', '[]'); },
        createURL: function(url,base) { return __ipc('createURL', JSON.stringify([url||'',base||''])); },
        createURLSearchParams: function(q) { return __ipc('createURLSearchParams', JSON.stringify([q||''])); }
    };

    g.window = g;
    g.alert = function(m) { __ipc('alert', JSON.stringify([m||''])); };
    g.confirm = function(m) { return __ipc('confirm', JSON.stringify([m||''])) === 'true'; };
    g.prompt = function(m,d) { return __ipc('prompt', JSON.stringify([m||'',d||''])); };
    g.fetch = function(url, opts) {
        return new Promise(function(resolve, reject) {
            var rid = __g_store(resolve);
            var rjid = __g_store(reject);
            __ipc('fetch', JSON.stringify([url, JSON.stringify(opts||{}), rid, rjid]));
        });
    };
    g.requestAnimationFrame = function(fn) { return g.setTimeout(fn, 16); };
    g.cancelAnimationFrame = function(id) { g.clearTimeout(id); };
    g.decodeURI = function(s) { try { return decodeURI(s); } catch(e) { return s; } };
    g.encodeURI = function(s) { try { return encodeURI(s); } catch(e) { return s; } };
    g.decodeURIComponent = function(s) { try { return decodeURIComponent(s); } catch(e) { return s; } };
    g.encodeURIComponent = function(s) { try { return encodeURIComponent(s); } catch(e) { return s; } };
    g.parseInt = function(s,r) { return parseInt(s, r||10); };
    g.parseFloat = function(s) { return parseFloat(s); };
    g.isNaN = function(v) { return isNaN(v); };
    g.isFinite = function(v) { return isFinite(v); };
    g.atob = function(s) { return __ipc('atob', JSON.stringify([s||''])); };
    g.btoa = function(s) { return __ipc('btoa', JSON.stringify([s||''])); };
    try { g.Object.defineProperty(g, 'innerWidth', { configurable: true, get: function() { return parseInt(__ipc('innerWidth', '[]')); } }); } catch(e) {}
    try { g.Object.defineProperty(g, 'innerHeight', { configurable: true, get: function() { return parseInt(__ipc('innerHeight', '[]')); } }); } catch(e) {}
})();
";
        _engine.Execute(setup);
        // Load DOM setup from external JS file
        var domSetupPath = Path.Combine(AppContext.BaseDirectory, "DomSetup.js");
        if (File.Exists(domSetupPath))
        {
            try
            {
                var domSetup = File.ReadAllText(domSetupPath);
                _engine.Execute(domSetup);
                Console.WriteLine("[JsEngineHost] DOM setup loaded from file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JsEngineHost] Failed to load DOM setup: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[JsEngineHost] DomSetup.js not found at " + domSetupPath);
        }
    }

    private string SendToMain(string method, string argsJson)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _pending[requestId] = tcs;
        }

        var request = new IpcRequest
        {
            RequestId = requestId,
            Type = RequestType.DomCall,
            Payload = $"{method}|{argsJson}",
        };

        try
        {
            var data = ProtocolSerializer.Serialize(request);
            _transport!.SendAsync(data).Wait();

            if (!tcs.Task.Wait(5000))
            {
                lock (_lock) _pending.Remove(requestId);
                return "{\"error\":\"timeout\"}";
            }

            var response = tcs.Task.Result;
            return response.Result ?? "null";
        }
        catch (Exception ex)
        {
            lock (_lock) _pending.Remove(requestId);
            return $"{{\"error\":\"{ex.Message}\"}}";
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _transport != null)
        {
        try
        {
            var data = await _transport.ReceiveAsync();
            var msg = ProtocolSerializer.DeserializeAny(data);
            if (msg.Request != null)
            {
                var reqCopy = msg.Request;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await ProcessRequest(reqCopy);
                        var responseData = ProtocolSerializer.Serialize(result);
                        await _transport!.SendAsync(responseData);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[JsEngineHost] Request error: {ex.Message}");
                    }
                });
                continue;
            }

            if (msg.Response != null)
            {
                TaskCompletionSource<IpcResponse>? tcs;
                lock (_lock)
                {
                    if (_pending.TryGetValue(msg.Response.RequestId, out tcs))
                        _pending.Remove(msg.Response.RequestId);
                }
                if (tcs != null)
                {
                    tcs.TrySetResult(msg.Response);
                }
            }
        }
        catch (EndOfStreamException) { break; }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[JsEngineHost] Error: {ex.Message}");
        }
    }
        _cts?.Cancel();
    }

    private async Task<IpcResponse> ProcessRequest(IpcRequest request)
    {
        try
        {
            return request.Type switch
            {
                RequestType.Execute => HandleExecute(request),
                RequestType.Evaluate => HandleEvaluate(request),
                RequestType.CallFunction => HandleCallFunction(request),
                RequestType.InvokeCallback => HandleInvokeCallback(request),
                RequestType.DisposeEngine => HandleDispose(),
                RequestType.Ping => new IpcResponse { RequestId = request.RequestId, Success = true, Type = ResponseType.Pong },
                _ => new IpcResponse { RequestId = request.RequestId, Success = false, Error = $"Unknown: {request.Type}" }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleExecute(IpcRequest request)
    {
        _engine.Execute(request.Payload ?? "");
        return new IpcResponse { RequestId = request.RequestId, Success = true };
    }

    private IpcResponse HandleEvaluate(IpcRequest request)
    {
        var result = _engine.Evaluate(request.Payload ?? "");
        return new IpcResponse
        {
            RequestId = request.RequestId,
            Success = true,
            Result = result?.ToString()
        };
    }

    private IpcResponse HandleCallFunction(IpcRequest request)
    {
        if (request.Payload != null)
        {
            var parts = JsonSerializer.Deserialize<string[]>(request.Payload);
            if (parts != null && parts.Length >= 1)
            {
                var args = parts.Length > 1 ? parts.Skip(1).Cast<object>().ToArray() : Array.Empty<object>();
                var result = _engine.CallFunction(parts[0], args);
                return new IpcResponse { RequestId = request.RequestId, Success = true, Result = result?.ToString() };
            }
        }
        return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid call" };
    }

    private IpcResponse HandleInvokeCallback(IpcRequest request)
    {
        var code = $"__g_invoke({request.CallbackId}, {JsonSerializer.Serialize(request.Payload)})";
        _engine.Evaluate(code);
        return new IpcResponse { RequestId = request.RequestId, Success = true, Type = ResponseType.CallbackInvoked };
    }

    private IpcResponse HandleDispose()
    {
        _cts?.Cancel();
        return new IpcResponse { RequestId = 0, Success = true };
    }
}