using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using JavaScriptEngineSwitcher.Core;

namespace UpBrowser.Core.JavaScript;

public class V8EngineAdapter : EngineAdapterBase
{
    private readonly ConcurrentDictionary<int, object?> _directCallbacks = new();
    private int _nextDirectCbId;

    public override JsEngineType EngineType => JsEngineType.V8;
    public override bool SupportsHostObjects => true;
    public override bool SupportsES6Proxy => true;

    public V8EngineAdapter() : base(CreateV8Engine())
    {
    }

    public V8EngineAdapter(IJsEngine engine) : base(engine)
    {
    }

    private static IJsEngine CreateV8Engine()
    {
        if (!JsEngineConfig.IsV8Available)
        {
            Console.WriteLine("[JS] V8 engine not available, falling back to Jint");
            return JsEngineConfig.CreateEngine(JsEngineType.Jint);
        }
        return JsEngineConfig.CreateEngine(JsEngineType.V8);
    }

    public override object? CallFunction(string functionName, params object?[] args)
    {
        // V8 does not support re-entrant CallFunction/EmbedHostObject/Execute/Evaluate
        // from within a host callback. Handle __g_store in C# to avoid engine re-entry.
        if (functionName == "__g_store" && args.Length > 0 && IsScriptObject(args[0]))
        {
            return StoreScriptObject(args[0]);
        }
        return base.CallFunction(functionName, args);
    }

    public override int StoreCallback(object callback)
    {
        if (IsScriptObject(callback))
            return StoreScriptObject(callback);
        return base.StoreCallback(callback);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "V8 assemblies are rooted - dynamic dispatch is safe")]
    public override void InvokeCallbackWith(int id, object? arg)
    {
        if (_directCallbacks.TryGetValue(id, out var cb) && IsScriptObject(cb))
        {
            try
            {
                dynamic dyn = cb;
                if (arg != null)
                    dyn.Invoke(false, arg);
                else
                    dyn.Invoke(false);
                return;
            }
            catch { }
        }
        base.InvokeCallbackWith(id, arg);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "V8 assemblies are rooted - dynamic dispatch is safe")]
    public override void InvokeCallback(int id)
    {
        if (_directCallbacks.TryGetValue(id, out var cb) && IsScriptObject(cb))
        {
            try
            {
                dynamic dyn = cb;
                dyn.Invoke(false);
                return;
            }
            catch { }
        }
        base.InvokeCallback(id);
    }

    public override void RemoveCallback(int id)
    {
        if (_directCallbacks.TryRemove(id, out _))
            return;
        base.RemoveCallback(id);
    }

    public override void ClearCallbacks()
    {
        _directCallbacks.Clear();
        base.ClearCallbacks();
    }

    private static bool IsScriptObject(object? obj)
    {
        if (obj == null) return false;
        var name = obj.GetType().FullName;
        return name != null && (name.Contains("ScriptObject") || name.Contains("V8ScriptItem"));
    }

    private int StoreScriptObject(object callback)
    {
        var id = Interlocked.Increment(ref _nextDirectCbId);
        _directCallbacks[id] = callback;
        return id;
    }
}
