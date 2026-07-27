using System.Diagnostics;
using System.Text.Json;
using UpBrowser.JsEngineProtocol;

namespace UpBrowser.Core.JavaScript;

/// <summary>
/// 远程 JS 引擎适配器，通过 IPC 与 UpBrowser.JsEngineHost 进程通信。
/// JsEngineHost 进程不进行 AOT 编译，因此可以正常使用反射。
/// </summary>
public class RemoteJsEngineAdapter : IJavaScriptEngineAdapter, IDisposable
{
    private MmapTransport? _transport;
    private System.Diagnostics.Process? _hostProcess;
    private long _nextRequestId;
    private readonly string _channelName;
    private readonly string _engineType;
    private readonly int _tabIndex;
    private bool _disposed;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly Dictionary<long, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly object _lock = new();

    /// <summary>浏览器回调，由 BrowserApp 设置</summary>
    public Action<string>? OnAlert { get; set; }
    public Func<string, bool>? OnConfirm { get; set; }
    public Func<string, string, string?>? OnPrompt { get; set; }
    public Func<int>? OnGetInnerWidth { get; set; }
    public Func<int>? OnGetInnerHeight { get; set; }
    public Action<int, int>? OnScrollTo { get; set; }
    public Action<int, int>? OnScrollBy { get; set; }
    public string? OnFetch { get; set; } // JSON 格式的 fetch 结果

    public JsEngineType EngineType => JsEngineType.Jint;
    public object? InnerEngine => null;
    public bool SupportsHostObjects => true;
    public bool SupportsES6Proxy => true;

    public RemoteJsEngineAdapter(string channelName, string engineType, int tabIndex)
    {
        _channelName = channelName;
        _engineType = engineType;
        _tabIndex = tabIndex;
    }

    /// <summary>
    /// 同步启动（供 EngineProcessManager 调用）。
    /// </summary>
    public void Start()
    {
        StartAsync().Wait();
    }

    public async Task StartAsync()
    {
        // 启动 JsEngineHost 进程
        var hostPath = FindHostExe();
        if (hostPath == null)
            throw new InvalidOperationException("JsEngineHost.exe not found");

        var isDll = hostPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : hostPath,
            Arguments = isDll
                ? $"\"{hostPath}\" --channel={_channelName} --engine={_engineType} --tab={_tabIndex}"
                : $"--channel={_channelName} --engine={_engineType} --tab={_tabIndex}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        _hostProcess = System.Diagnostics.Process.Start(startInfo);
        if (_hostProcess == null)
            throw new InvalidOperationException($"Failed to start JsEngineHost process: {hostPath}");

        _hostProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine($"[JsEngineHost] {e.Data}");
        };
        _hostProcess.BeginErrorReadLine();

        System.Threading.Thread.Sleep(500);
        if (_hostProcess.HasExited)
        {
            var exitCode = _hostProcess.ExitCode;
            _hostProcess.Dispose();
            _hostProcess = null;
            throw new InvalidOperationException($"JsEngineHost exited immediately (code {exitCode}). " +
                "Check that all dependencies are available in the output directory.");
        }

        _hostProcess.EnableRaisingEvents = true;
        _hostProcess.Exited += (_, _) =>
        {
            Console.WriteLine($"[JsEngineHost] Process {_hostProcess?.Id} exited");
            _hostProcess?.Dispose();
            _hostProcess = null;
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        Console.CancelKeyPress += (_, _) => Cleanup();

        // 连接内存映射 IPC
        _transport = new MmapTransport(_channelName);
        await _transport.ConnectClientAsync();

        // 启动后台线程接收远程引擎的 DomCall 请求
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
    }

    private static string? FindHostExe()
    {
        // 1. 当前目录（发布后）
        var exe = Path.Combine(AppContext.BaseDirectory, "UpBrowser.JsEngineHost.exe");
        if (File.Exists(exe)) return exe;
        var dll = Path.Combine(AppContext.BaseDirectory, "UpBrowser.JsEngineHost.dll");
        if (File.Exists(dll)) return dll;

        // 2. 开发环境：从项目输出目录查找
        var baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            baseDir = Path.GetDirectoryName(baseDir);
            if (baseDir == null) break;

            // DLL（无 RuntimeIdentifier 的 build 输出）
            var devDll = Path.Combine(baseDir, "UpBrowser.JsEngineHost", "bin", "Debug", "net10.0", "UpBrowser.JsEngineHost.dll");
            if (File.Exists(devDll)) return devDll;
            var releaseDll = Path.Combine(baseDir, "UpBrowser.JsEngineHost", "bin", "Release", "net10.0", "UpBrowser.JsEngineHost.dll");
            if (File.Exists(releaseDll)) return releaseDll;

            // EXE（带 RuntimeIdentifier 的 build 输出）
            var ridExe = Path.Combine(baseDir, "UpBrowser.JsEngineHost", "bin", "Debug", "net10.0", "win-x64", "UpBrowser.JsEngineHost.exe");
            if (File.Exists(ridExe)) return ridExe;
            var ridReleaseExe = Path.Combine(baseDir, "UpBrowser.JsEngineHost", "bin", "Release", "net10.0", "win-x64", "UpBrowser.JsEngineHost.exe");
            if (File.Exists(ridReleaseExe)) return ridReleaseExe;
        }

        return null;
    }

    private void ExecuteSetupScript()
    {
        // 设置 JS 引擎的全局对象
        // 这些对象在远程引擎中作为 JS 对象存在，方法调用通过 IPC 转发到主进程
        var setupCode = @"
var __ipc_console = {
    log: function() { },
    error: function() { },
    warn: function() { },
    info: function() { },
    debug: function() { }
};
var console = __ipc_console;

(function() {
    var g = typeof globalThis !== 'undefined' ? globalThis : this;
    g.__g_cbid = 0;
    g.__g_cbs = {};
    g.__g_fnMap = typeof WeakMap !== 'undefined' ? new WeakMap() : {};
    
    g.__g_store = function(fn) {
        var id = g.__g_fnMap.get ? g.__g_fnMap.get(fn) : undefined;
        if (id !== undefined) return id;
        id = ++g.__g_cbid;
        g.__g_cbs[id] = fn;
        if (g.__g_fnMap.set) g.__g_fnMap.set(fn, id);
        return id;
    };
    
    g.__g_invoke = function(id, arg) {
        var fn = g.__g_cbs[id];
        if (fn) {
            if (arg !== undefined) return fn(arg);
            return fn();
        }
    };
    
    g.__g_remove = function(id) {
        delete g.__g_cbs[id];
    };
})();
";
        // 直接执行 Execute 请求来设置全局
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.Execute,
            Payload = setupCode,
        };
        var response = SendRequest(request);
        if (!response.Success)
            Console.WriteLine($"[JsEngineHost] Setup script failed: {response.Error}");
    }

    private void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveCts?.Cancel();
        _transport?.Dispose();

        if (_hostProcess != null && !_hostProcess.HasExited)
        {
            try
            {
                _hostProcess.Kill(entireProcessTree: true);
                if (!_hostProcess.WaitForExit(3000))
                    _hostProcess.Kill();
            }
            catch { }
            _hostProcess.Dispose();
            _hostProcess = null;
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _transport != null)
        {
        try
        {
            var data = await _transport.ReceiveAsync();
            var bytes = data;

            var msg = ProtocolSerializer.DeserializeAny(bytes);
            if (msg.Request != null)
            {
                if (msg.Request.Type == RequestType.DomCall)
                {
                    var result = HandleDomCall(msg.Request);
                    var responseData = ProtocolSerializer.Serialize(result);
                    await _transport.SendAsync(responseData);
                }
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
        catch (OperationCanceledException) { break; }
        catch (EndOfStreamException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[RemoteJsEngine] Receive error: {ex.Message}");
        }
    }
    }

    private IpcResponse HandleDomCall(IpcRequest request)
    {
        try
        {
            var payload = request.Payload ?? "";
            var sep = payload.IndexOf('|');
            if (sep < 0) return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid payload" };

            var method = payload[..sep];
            var argsJson = payload[(sep + 1)..];

            return method switch
            {
                "console.log" or "console.error" or "console.warn"
                    or "console.info" or "console.debug" => HandleConsoleLog(method, argsJson),
                "setTimeout" => HandleSetTimeout(request, argsJson),
                "setInterval" => HandleSetInterval(request, argsJson),
                "clearTimeout" or "clearInterval" => RespondOk(request),
                "innerWidth" => JsonResult(request, (OnGetInnerWidth?.Invoke() ?? 1024).ToString()),
                "innerHeight" => JsonResult(request, (OnGetInnerHeight?.Invoke() ?? 768).ToString()),
                "scrollTo" => HandleAction(request, argsJson, a => OnScrollTo?.Invoke(a[0], a[1])),
                "scrollBy" => HandleAction(request, argsJson, a => OnScrollBy?.Invoke(a[0], a[1])),
                "alert" => HandleAlert(request, argsJson),
                "confirm" => JsonResult(request, OnConfirm?.Invoke(GetStringArg(argsJson, 0) ?? "") == true ? "true" : "false"),
                "prompt" => JsonResult(request, OnPrompt?.Invoke(GetStringArg(argsJson, 0) ?? "", GetStringArg(argsJson, 1) ?? "") ?? ""),
                "atob" => JsonResult(request, DecodeBase64(GetStringArg(argsJson, 0) ?? "")),
                "btoa" => JsonResult(request, EncodeBase64(GetStringArg(argsJson, 0) ?? "")),
                "fetch" => HandleFetch(request, argsJson),
                _ => new IpcResponse { RequestId = request.RequestId, Success = false, Error = $"Unknown: {method}" }
            };
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse RespondOk(IpcRequest request) =>
        new() { RequestId = request.RequestId, Success = true };

    private IpcResponse JsonResult(IpcRequest request, string value) =>
        new() { RequestId = request.RequestId, Success = true, Result = value };

    private static string? GetStringArg(string argsJson, int index)
    {
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(argsJson);
            return arr != null && index < arr.Length ? arr[index] : null;
        }
        catch { return null; }
    }

    private static string DecodeBase64(string s)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return s; }
    }

    private static string EncodeBase64(string s)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
    }

    private IpcResponse HandleConsoleLog(string method, string argsJson)
    {
        Console.WriteLine($"[{method}] {argsJson}");
        return RespondOk(new IpcRequest { RequestId = 0 });
    }

    private IpcResponse HandleAction(IpcRequest request, string argsJson, Action<int[]> action)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<int[]>(argsJson);
            if (args != null) action(args);
            return RespondOk(request);
        }
        catch { return RespondOk(request); }
    }

    private IpcResponse HandleAlert(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(argsJson);
            if (args != null && args.Length > 0) OnAlert?.Invoke(args[0] ?? "");
            return RespondOk(request);
        }
        catch { return RespondOk(request); }
    }

    private IpcResponse HandleSetTimeout(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<int[]>(argsJson);
            if (args == null || args.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var callbackId = args[0];
            var delayMs = Math.Max(1, args[1]);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs);
                    InvokeCallback(callbackId);
                }
                catch { }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleSetInterval(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<int[]>(argsJson);
            if (args == null || args.Length < 2)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var callbackId = args[0];
            var intervalMs = Math.Max(16, args[1]);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_disposed)
                    {
                        await Task.Delay(intervalMs);
                        InvokeCallback(callbackId);
                    }
                }
                catch { }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private IpcResponse HandleFetch(IpcRequest request, string argsJson)
    {
        try
        {
            var args = System.Text.Json.JsonSerializer.Deserialize<object[]>(argsJson);
            if (args == null || args.Length < 4)
                return new IpcResponse { RequestId = request.RequestId, Success = false, Error = "Invalid args" };

            var url = args[0]?.ToString() ?? "";
            var optsJson = args[1]?.ToString() ?? "{}";
            var resolveId = Convert.ToInt32(args[2]);
            var rejectId = Convert.ToInt32(args[3]);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    var options = string.IsNullOrEmpty(optsJson) ? null :
                        System.Text.Json.JsonSerializer.Deserialize<FetchOptions>(optsJson);
                    var method = options?.Method ?? "GET";
                    var req = new HttpRequestMessage(new HttpMethod(method), url);

                    if (options?.Body != null && (method == "POST" || method == "PUT" || method == "PATCH"))
                        req.Content = new StringContent(options.Body, System.Text.Encoding.UTF8, "application/json");

                    var response = await http.SendAsync(req);
                    var text = await response.Content.ReadAsStringAsync();

                    var result = new FetchResult
                    {
                        Success = true,
                        Data = text,
                        Status = (int)response.StatusCode,
                        StatusText = response.ReasonPhrase ?? ""
                    };

                    var resultJson = System.Text.Json.JsonSerializer.Serialize(result);
                    InvokeCallbackWith(resolveId, resultJson);
                }
                catch (Exception ex)
                {
                    var error = new FetchResult { Success = false, Error = ex.Message };
                    var errorJson = System.Text.Json.JsonSerializer.Serialize(error);
                    InvokeCallbackWith(rejectId, errorJson);
                }
            });

            return RespondOk(request);
        }
        catch (Exception ex)
        {
            return new IpcResponse { RequestId = request.RequestId, Success = false, Error = ex.Message };
        }
    }

    private class FetchOptions
    {
        public string? Method { get; set; }
        public string? Body { get; set; }
    }

    private class FetchResult
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public int Status { get; set; }
        public string? StatusText { get; set; }
        public string? Error { get; set; }
    }

    public void Execute(string code)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.Execute,
            Payload = code,
        };
        var response = SendRequest(request);
        if (!response.Success)
            throw new Exception(response.Error ?? "JS execution failed");
    }

    public object? Evaluate(string expression)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.Evaluate,
            Payload = expression,
        };
        var response = SendRequest(request);
        if (!response.Success)
            throw new Exception(response.Error ?? "JS evaluation failed");
        return response.Result;
    }

    public object? CallFunction(string functionName, params object?[] args)
    {
        var argStrings = new List<string> { functionName };
        foreach (var arg in args)
            argStrings.Add(arg?.ToString() ?? "");
        var payload = System.Text.Json.JsonSerializer.Serialize(argStrings);

        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.CallFunction,
            Payload = payload,
        };
        var response = SendRequest(request);
        if (!response.Success)
            throw new Exception(response.Error ?? "JS function call failed");
        return response.Result;
    }

    public void EmbedHostObject(string name, object? value) => SetGlobal(name, value);

    public void SetGlobal(string name, object? value)
    {
        // 远程引擎的初始化脚本已经定义了所有全局对象（console, __upbrowser 等）
        // 不需要再发送 .NET 对象到远程引擎
        // 只有简单值（字符串、数字）才需要设置
        if (value is string strValue)
        {
            var escaped = strValue.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");
            Execute($"var {name} = '{escaped}';");
        }
        else if (value is int || value is long || value is float || value is double)
        {
            Execute($"var {name} = {value};");
        }
        else if (value is bool b)
        {
            Execute($"var {name} = {(b ? "true" : "false")};");
        }
        // 复杂对象（ConsoleHost, DocumentHost 等）已在远程引擎的初始化脚本中定义
    }

    public int StoreCallback(object callback)
    {
        // 回调在远程引擎中注册，返回 ID
        return 0;
    }

    public void InvokeCallback(int id)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.InvokeCallback,
            CallbackId = id,
        };
        SendRequest(request);
    }

    public void InvokeCallbackWith(int id, object? arg)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.InvokeCallback,
            CallbackId = id,
            Payload = arg?.ToString(),
        };
        SendRequest(request);
    }

    public void RemoveCallback(int id)
    {
        var request = new IpcRequest
        {
            RequestId = Interlocked.Increment(ref _nextRequestId),
            Type = RequestType.RemoveCallback,
            CallbackId = id,
        };
        SendRequest(request);
    }

    public void ClearCallbacks()
    {
        // 远程引擎不支持批量清除，逐个清除由主进程管理
    }

    public T? GetGlobal<T>(string name) where T : class
    {
        return null;
    }

    public void SetGlobal(string name, object? value, bool rewriteExisting)
    {
        SetGlobal(name, value);
    }

    private IpcResponse SendRequest(IpcRequest request)
    {
        if (_transport == null)
            throw new InvalidOperationException("IPC transport not initialized");

        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending[request.RequestId] = tcs;
        }

        try
        {
            var data = ProtocolSerializer.Serialize(request);
            _transport.SendAsync(data).GetAwaiter().GetResult();

            if (!tcs.Task.Wait(10000))
            {
                lock (_lock) _pending.Remove(request.RequestId);
                throw new TimeoutException($"IPC request {request.RequestId} timed out");
            }

            return tcs.Task.Result;
        }
        catch (Exception)
        {
            lock (_lock) _pending.Remove(request.RequestId);
            throw;
        }
    }

    public void Dispose()
    {
        Cleanup();
    }

    // IJavaScriptEngineAdapter 必需的其他方法
    public int CaptureFunction(string globalName)
    {
        // 远程引擎不支持直接捕获函数，返回 0
        return 0;
    }

    public void Reset()
    {
        // 远程引擎不支持重置，由主进程管理
    }

    }