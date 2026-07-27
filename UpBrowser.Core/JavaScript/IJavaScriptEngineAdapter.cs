namespace UpBrowser.Core.JavaScript;

public interface IJavaScriptEngineAdapter : IDisposable
{
    JsEngineType EngineType { get; }

    void Execute(string code);
    object? Evaluate(string expression);
    object? CallFunction(string functionName, params object?[] args);

    void SetGlobal(string name, object? value);
    void EmbedHostObject(string name, object? value);
    T? GetGlobal<T>(string name) where T : class;

    int StoreCallback(object callback);
    void InvokeCallback(int id);
    void InvokeCallbackWith(int id, object? arg);
    void RemoveCallback(int id);
    int CaptureFunction(string globalName);
    void ClearCallbacks();
    void Reset();

    bool SupportsHostObjects { get; }
    bool SupportsES6Proxy { get; }
    object? InnerEngine { get; }
}