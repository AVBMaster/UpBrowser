using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using JavaScriptEngineSwitcher.Jurassic;

namespace UpBrowser.Core.JavaScript;

public enum JsEngineType
{
    V8,
    Jint,
    Jurassic
}

public static class JsEngineConfig
{
    private static bool _initialized;
    private static JsEngineType _defaultEngineType = JsEngineType.Jint;

    public static JsEngineType DefaultEngineType
    {
        get => _defaultEngineType;
        set
        {
            _defaultEngineType = value;
            if (_initialized)
            {
                var switcher = JsEngineSwitcher.Current;
                switcher.DefaultEngineName = GetEngineName(value);
            }
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var switcher = JsEngineSwitcher.Current;
        switcher.EngineFactories.AddJint();
        switcher.EngineFactories.AddJurassic();

        // V8 is only available on Windows (requires ClearScript native binaries)
        // Other platforms use Jint (pure .NET) as fallback
        if (_defaultEngineType == JsEngineType.V8)
            _defaultEngineType = JsEngineType.Jint;

        switcher.DefaultEngineName = GetEngineName(_defaultEngineType);
    }

    public static IJsEngine CreateEngine()
    {
        if (!_initialized) Initialize();
        return JsEngineSwitcher.Current.CreateDefaultEngine();
    }

    private static string GetEngineName(JsEngineType type) => type switch
    {
        JsEngineType.V8 => "JintJsEngine",
        JsEngineType.Jint => JintJsEngine.EngineName,
        JsEngineType.Jurassic => JurassicJsEngine.EngineName,
        _ => JintJsEngine.EngineName
    };
}
