using JavaScriptEngineSwitcher.Core;

namespace UpBrowser.Core.JavaScript;

public interface IJavaScriptEngineAdapter : IDisposable
{
    JsEngineType EngineType { get; }

    void Execute(string code);
    object? Evaluate(string expression);
    object? CallFunction(string functionName, params object?[] args);

    void SetGlobal(string name, object? value);
    T? GetGlobal<T>(string name) where T : class;

    /// <summary>Store a JS function reference by ID for later invocation.</summary>
    int StoreCallback(object callback);
    void InvokeCallback(int id);
    void InvokeCallbackWith(int id, object? arg);
    void RemoveCallback(int id);

    /// <summary>Capture a JS function reference from the global scope by name.</summary>
    int CaptureFunction(string globalName);

    /// <summary>Clear all stored callbacks.</summary>
    void ClearCallbacks();

    /// <summary>Reset engine state (clear all globals, callbacks, etc).</summary>
    void Reset();

    bool SupportsHostObjects { get; }
    bool SupportsES6Proxy { get; }

    /// <summary>The underlying engine (for engine-specific operations).</summary>
    IJsEngine? InnerEngine { get; }
}