using JavaScriptEngineSwitcher.Core;

namespace UpBrowser.Core.JavaScript;

public class V8EngineAdapter : EngineAdapterBase
{
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

    public override void InvokeCallbackWith(int id, object? arg)
    {
        // V8/ClearScript supports re-entrant CallFunction from within
        // host callbacks, but NOT EmbedHostObject + Execute/Evaluate.
        // Use direct CallFunction to pass the argument to the JS callback.
        try
        {
            if (arg != null)
            {
                Console.Error.WriteLine($"[V8] InvokeCallbackWith id={id} arg={arg.GetType().Name}");
                _engine.CallFunction("__g_invoke", id, arg);
                Console.Error.WriteLine($"[V8] CallFunction OK");
            }
            else
            {
                _engine.CallFunction("__g_invoke", id);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[V8] CallFunction FAIL: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
