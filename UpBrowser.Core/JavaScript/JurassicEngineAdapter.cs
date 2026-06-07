using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jurassic;

namespace UpBrowser.Core.JavaScript;

public class JurassicEngineAdapter : EngineAdapterBase
{
    public override JsEngineType EngineType => JsEngineType.Jurassic;
    public override bool SupportsHostObjects => true;
    public override bool SupportsES6Proxy => false;

    public JurassicEngineAdapter() : base(JsEngineConfig.CreateEngine(JsEngineType.Jurassic))
    {
    }

    public JurassicEngineAdapter(IJsEngine engine) : base(engine)
    {
    }
}