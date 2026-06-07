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
}