using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using JavaScriptEngineSwitcher.Jurassic;
using JavaScriptEngineSwitcher.V8;

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
        switcher.EngineFactories.AddV8();
        switcher.EngineFactories.AddJint();
        switcher.EngineFactories.AddJurassic();
        switcher.DefaultEngineName = GetEngineName(_defaultEngineType);
    }

    public static IJsEngine CreateEngine()
    {
        if (!_initialized) Initialize();
        return JsEngineSwitcher.Current.CreateDefaultEngine();
    }

    private static string GetEngineName(JsEngineType type) => type switch
    {
        JsEngineType.V8 => V8JsEngine.EngineName,
        JsEngineType.Jint => JintJsEngine.EngineName,
        JsEngineType.Jurassic => JurassicJsEngine.EngineName,
        _ => V8JsEngine.EngineName
    };
}
