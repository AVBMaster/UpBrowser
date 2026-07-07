using JavaScriptEngineSwitcher.Core;
using System.Collections.Concurrent;

namespace UpBrowser.Core.JavaScript;

public enum ScriptType
{
    Inline,
    External,
    Module,
    Defer,
    Async
}

public class BrowserJsException : Exception
{
    public string SourceUrl { get; }
    public int LineNumber { get; }
    public int ColumnNumber { get; }
    public string JsStackTrace { get; }
    public JsErrorType ErrorType { get; }

    public BrowserJsException(string message, string sourceUrl = "",
        int lineNumber = 0, int columnNumber = 0,
        string stackTrace = "", JsErrorType errorType = JsErrorType.Error)
        : base(message)
    {
        SourceUrl = sourceUrl;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        JsStackTrace = stackTrace;
        ErrorType = errorType;
    }

    public BrowserJsException(JsScriptException ex)
        : base(ex.Message)
    {
        SourceUrl = ex.Source ?? "";
        LineNumber = ex.LineNumber;
        ColumnNumber = ex.ColumnNumber;
        JsStackTrace = ex.StackTrace ?? "";
        ErrorType = MapErrorType(ex);
    }

    private static JsErrorType MapErrorType(JsScriptException ex)
    {
        if (ex is JsCompilationException compEx)
        {
            return compEx.Type switch
            {
                "SyntaxError" => JsErrorType.SyntaxError,
                "TypeError" => JsErrorType.TypeError,
                "ReferenceError" => JsErrorType.ReferenceError,
                "RangeError" => JsErrorType.RangeError,
                "EvalError" => JsErrorType.Error,
                _ => JsErrorType.SyntaxError
            };
        }

        if (ex is JsRuntimeException rtEx)
        {
            return rtEx.Type switch
            {
                "TypeError" => JsErrorType.TypeError,
                "ReferenceError" => JsErrorType.ReferenceError,
                "RangeError" => JsErrorType.RangeError,
                "SyntaxError" => JsErrorType.SyntaxError,
                _ => JsErrorType.Error
            };
        }

        return ex.Category switch
        {
            "Interrupted error" => JsErrorType.Interrupted,
            _ => JsErrorType.Error
        };
    }
}

public enum JsErrorType
{
    Error,
    SyntaxError,
    TypeError,
    ReferenceError,
    RangeError,
    EvalError,
    URIError,
    Interrupted
}

public class JsPropertyDescriptor
{
    public string JsName { get; set; } = "";
    public Func<object?, object?>? Getter { get; set; }
    public Action<object?, object?>? Setter { get; set; }
    public Type? ReturnType { get; set; }
    public bool Writable { get; set; } = true;
    public bool Configurable { get; set; } = true;
    public bool Enumerable { get; set; } = true;
}

public class JsMethodDescriptor
{
    public string JsName { get; set; } = "";
    public Delegate? Method { get; set; }
    public string[]? ArgumentNames { get; set; }
}

public class JsDomDescriptor
{
    public string JsInterfaceName { get; set; } = "";
    public List<JsPropertyDescriptor> Properties { get; set; } = new();
    public List<JsMethodDescriptor> Methods { get; set; } = new();
    public List<string> PrototypeChain { get; set; } = new();
}

public class BrowserJsEngineFacade : IDisposable
{
    private readonly IJavaScriptEngineAdapter _adapter;
    private bool _disposed;
    private int _realmIdCounter;
    private readonly Dictionary<int, RealmInfo> _realms = new();
    private int _activeRealmId;
    private readonly ConcurrentDictionary<string, object?> _lazyBindings = new();
    private readonly List<Action> _postExecutionActions = new();
    private readonly object _lock = new();

    public IJavaScriptEngineAdapter Adapter => _adapter;
    public JsEngineType EngineType => _adapter.EngineType;
    public int ActiveRealmId => _activeRealmId;

    public event Action<BrowserJsException>? OnScriptError;

    public BrowserJsEngineFacade(IJavaScriptEngineAdapter adapter)
    {
        _adapter = adapter;
        Initialize();
    }

    private void Initialize()
    {
        _activeRealmId = CreateRealmInternal("main", null);
    }

    public int CreateRealm(string name = "", int? parentRealmId = null)
    {
        return CreateRealmInternal(name, parentRealmId);
    }

    private int CreateRealmInternal(string name, int? parentRealmId)
    {
        var id = Interlocked.Increment(ref _realmIdCounter);
        lock (_lock)
        {
            _realms[id] = new RealmInfo
            {
                Id = id,
                Name = name,
                ParentRealmId = parentRealmId,
                CreatedAt = DateTime.UtcNow
            };
        }
        return id;
    }

    public void SwitchToRealm(int realmId)
    {
        lock (_lock)
        {
            if (!_realms.ContainsKey(realmId))
                throw new ArgumentException($"Realm {realmId} not found");
            _activeRealmId = realmId;
        }
    }

    public void DestroyRealm(int realmId)
    {
        lock (_lock)
        {
            _realms.Remove(realmId);
            if (_activeRealmId == realmId)
                _activeRealmId = _realms.Keys.FirstOrDefault();
        }
    }

    public void SetGlobalObject(string name, object? hostObject)
    {
        if (hostObject == null) return;
        _adapter.SetGlobal(name, hostObject);
    }

    public T? GetGlobalObject<T>(string name) where T : class
    {
        return _adapter.GetGlobal<T>(name);
    }

    public void Execute(string code, string? sourceUrl = null, int lineOffset = 0)
    {
        if (_disposed || string.IsNullOrEmpty(code)) return;

        var wrappedCode = code;
        if (!string.IsNullOrEmpty(sourceUrl) && EngineType == JsEngineType.V8)
        {
            wrappedCode = $"//# sourceURL={sourceUrl}\n{code}";
        }

        Current = _adapter;

        try
        {
            _adapter.Execute(wrappedCode);
            FlushPostExecution();
        }
        catch (JsScriptException ex)
        {
            var browserEx = new BrowserJsException(ex);
            if (!string.IsNullOrEmpty(sourceUrl))
                browserEx = new BrowserJsException(ex.Message, sourceUrl, ex.LineNumber + lineOffset, ex.ColumnNumber, ex.StackTrace ?? "");
            OnScriptError?.Invoke(browserEx);
            throw browserEx;
        }
        catch (Exception ex)
        {
            var browserEx = new BrowserJsException(ex.Message, sourceUrl ?? "", lineOffset, 0, ex.StackTrace ?? "");
            OnScriptError?.Invoke(browserEx);
            throw browserEx;
        }
        finally
        {
            Current = null;
        }
    }

    public object? Evaluate(string expression, string? sourceUrl = null)
    {
        if (_disposed || string.IsNullOrEmpty(expression)) return null;

        Current = _adapter;
        try
        {
            var result = _adapter.Evaluate(expression);
            FlushPostExecution();
            return result;
        }
        catch (JsScriptException ex)
        {
            var browserEx = new BrowserJsException(ex);
            if (!string.IsNullOrEmpty(sourceUrl))
                browserEx = new BrowserJsException(ex.Message, sourceUrl, ex.LineNumber, ex.ColumnNumber, ex.StackTrace ?? "");
            OnScriptError?.Invoke(browserEx);
            return null;
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

    public object? CallFunction(string functionName, params object?[] args)
    {
        if (_disposed) return null;
        Current = _adapter;
        try
        {
            var result = _adapter.CallFunction(functionName, args);
            FlushPostExecution();
            return result;
        }
        catch (JsScriptException ex)
        {
            OnScriptError?.Invoke(new BrowserJsException(ex));
            return null;
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

    public int StoreJsFunction(object callback)
    {
        return _adapter.StoreCallback(callback);
    }

    public void InvokeJsFunction(int callbackId, params object?[] args)
    {
        if (_disposed) return;
        Current = _adapter;

        try
        {
            if (args == null || args.Length == 0)
                _adapter.InvokeCallback(callbackId);
            else if (args.Length == 1)
                _adapter.InvokeCallbackWith(callbackId, args[0]);
            else
            {
                var json = System.Text.Json.JsonSerializer.Serialize(args, _jsonOpts);
                _adapter.Execute($"__g_invoke({callbackId}, JSON.parse('{EscapeJsString(json)}'))");
            }
            FlushPostExecution();
        }
        catch { }
        finally
        {
            Current = null;
        }
    }

    public void RemoveJsFunction(int callbackId)
    {
        _adapter.RemoveCallback(callbackId);
    }

    public void RegisterLazyBinding(string name, Func<object?> factory)
    {
        _lazyBindings[name] = factory;
    }

    public object? ResolveLazyBinding(string name)
    {
        if (_lazyBindings.TryGetValue(name, out var value))
        {
            if (value is Func<object?> factory)
            {
                var resolved = factory();
                _lazyBindings[name] = resolved;
                return resolved;
            }
            return value;
        }
        return null;
    }

    public void RegisterPostExecution(Action action)
    {
        lock (_lock)
            _postExecutionActions.Add(action);
    }

    private void FlushPostExecution()
    {
        List<Action> actions;
        lock (_lock)
        {
            actions = new List<Action>(_postExecutionActions);
            _postExecutionActions.Clear();
        }
        foreach (var action in actions)
        {
            try { action(); }
            catch { }
        }
    }

    public void CollectGarbage()
    {
        if (_adapter is EngineAdapterBase adapter)
        {
            try
            {
                _adapter.Execute(@"
                    if (typeof gc !== 'undefined') gc();
                    if (typeof CollectGarbage !== 'undefined') CollectGarbage();
                ");
            }
            catch { }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    public void Reset()
    {
        _adapter.Reset();
        _lazyBindings.Clear();
        lock (_lock) _postExecutionActions.Clear();
    }

    public void ClearCallbacks()
    {
        _adapter.ClearCallbacks();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _adapter.Dispose();
        _lazyBindings.Clear();
        lock (_lock) _postExecutionActions.Clear();
        GC.SuppressFinalize(this);
    }

    [ThreadStatic]
    public static IJavaScriptEngineAdapter? Current;

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static string EscapeJsString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private class RealmInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentRealmId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
