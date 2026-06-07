using JavaScriptEngineSwitcher.Core;

namespace UpBrowser.Core.JavaScript;

public abstract class EngineAdapterBase : IJavaScriptEngineAdapter
{
    protected IJsEngine _engine;
    private readonly object _cbLock = new();
    private int _nextCbId = 1;
    private bool _disposed;

    public abstract JsEngineType EngineType { get; }
    public abstract bool SupportsHostObjects { get; }
    public virtual bool SupportsES6Proxy => true;

    public IJsEngine? InnerEngine => _engine;

    protected EngineAdapterBase(IJsEngine engine)
    {
        _engine = engine;
    }

    public virtual void Execute(string code)
    {
        if (_disposed || string.IsNullOrEmpty(code)) return;
        try { _engine.Execute(code); }
        catch (JsScriptException ex)
        {
            Console.WriteLine($"[JS Error] {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS Error] {ex.Message}");
        }
    }

    public virtual object? Evaluate(string expression)
    {
        if (_disposed || string.IsNullOrEmpty(expression)) return null;
        try { return _engine.Evaluate(expression); }
        catch { return null; }
    }

    public virtual object? CallFunction(string functionName, params object?[] args)
    {
        if (_disposed) return null;
        try { return _engine.CallFunction(functionName, args); }
        catch { return null; }
    }

    public virtual void SetGlobal(string name, object? value)
    {
        if (_disposed || value == null) return;
        _engine.EmbedHostObject(name, value);
    }

    public virtual T? GetGlobal<T>(string name) where T : class
    {
        if (_disposed) return null;
        try
        {
            var result = _engine.Evaluate(name);
            return result as T;
        }
        catch { return null; }
    }

    public virtual int StoreCallback(object callback)
    {
        var id = AllocCbId();

        if (_disposed || _engine == null) return id;

        // Use the engine to evaluate JS that stores the callback reference.
        // Different engines handle function references differently:
        // - Jint: callback comes as JsValue (function), EmbedHostObject wraps it
        // - V8/ClearScript: callback comes as ScriptObject, AddHostObject wraps it
        // Both engines support embedding host objects and re-accessing them in eval scope.
        try
        {
            var tmp = $"__g_tmp_{id}";
            _engine.EmbedHostObject(tmp, callback);
            _engine.Evaluate($"__g_cbs[{id}] = {tmp}; delete {tmp};");
        }
        catch
        {
            // Fallback: create a no-op callback if we can't store it
            _engine.Evaluate($"__g_cbs[{id}] = function(){{}};");
        }

        return id;
    }

    public virtual void InvokeCallback(int id)
    {
        if (_disposed) return;
        try { _engine.Evaluate($"__g_invoke({id})"); }
        catch { }
    }

    public virtual void InvokeCallbackWith(int id, object? arg)
    {
        if (_disposed) return;

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
            var json = System.Text.Json.JsonSerializer.Serialize(dict, _jsonOpts);
            try
            {
                _engine.Execute($"__g_invoke({id}, JSON.parse('{EscapeJs(json)}'))");
            }
            catch { }
        }
        else
        {
            var json = System.Text.Json.JsonSerializer.Serialize(arg, _jsonOpts);
            try
            {
                _engine.Execute($"__g_invoke({id}, JSON.parse('{EscapeJs(json)}'))");
            }
            catch { }
        }
    }

    public virtual void RemoveCallback(int id)
    {
        if (_disposed) return;
        try { _engine.Evaluate($"__g_remove({id})"); }
        catch { }
    }

    public virtual int CaptureFunction(string globalName)
    {
        var id = AllocCbId();
        if (_disposed) return id;
        try
        {
            _engine.Evaluate($"__g_cbs[{id}] = {globalName};");
        }
        catch { }
        return id;
    }

    public virtual void ClearCallbacks()
    {
        if (_disposed) return;
        try { _engine.Evaluate("__g_cbs = {}; __g_cbid = 0;"); }
        catch { }
    }

    public virtual void Reset()
    {
        if (_disposed) return;
        try
        {
            ClearCallbacks();
            _engine.Execute("for(var k in this){if(typeof this[k]!='function')delete this[k];}");
        }
        catch { }
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
        GC.SuppressFinalize(this);
    }

    private int AllocCbId()
    {
        lock (_cbLock) return _nextCbId++;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static string EscapeJs(string s)
    {
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}