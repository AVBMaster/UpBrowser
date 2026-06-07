using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;

namespace UpBrowser.Core.JavaScript;

public class JintEngineAdapter : EngineAdapterBase
{
    public override JsEngineType EngineType => JsEngineType.Jint;
    public override bool SupportsHostObjects => true;
    public override bool SupportsES6Proxy => true;

    public JintEngineAdapter() : base(JsEngineConfig.CreateEngine(JsEngineType.Jint))
    {
    }

    public JintEngineAdapter(IJsEngine engine) : base(engine)
    {
    }
}