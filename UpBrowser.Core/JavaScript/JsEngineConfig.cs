using System.Reflection;
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
    private static bool _v8Available;

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

    public static bool IsV8Available => _v8Available;

    public static JsEngineType EffectiveEngineType
    {
        get
        {
            if (_defaultEngineType == JsEngineType.V8 && !_v8Available)
                return JsEngineType.Jint;
            return _defaultEngineType;
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var switcher = JsEngineSwitcher.Current;
        switcher.EngineFactories.AddJint();
        switcher.EngineFactories.AddJurassic();

        TryRegisterV8(switcher);

        if (!IsEngineAvailable(_defaultEngineType))
        {
            var fallback = JsEngineType.Jint;
            Console.WriteLine($"[JS] Engine '{_defaultEngineType}' not available, falling back to '{fallback}'");
            _defaultEngineType = fallback;
        }

        switcher.DefaultEngineName = GetEngineName(_defaultEngineType);
    }

    public static IJsEngine CreateEngine()
    {
        if (!_initialized) Initialize();
        return JsEngineSwitcher.Current.CreateDefaultEngine();
    }

    public static IJsEngine CreateEngine(JsEngineType type)
    {
        if (!_initialized) Initialize();

        var name = GetEngineName(type);
        var factory = JsEngineSwitcher.Current.EngineFactories
            .FirstOrDefault(f => f.EngineName == name);

        if (factory == null)
        {
            Console.WriteLine($"[JS] Engine factory '{name}' not found, using default");
            return JsEngineSwitcher.Current.CreateDefaultEngine();
        }

        return factory.CreateEngine();
    }

    public static bool IsEngineAvailable(JsEngineType type)
    {
        var name = GetEngineName(type);
        return JsEngineSwitcher.Current.EngineFactories
            .Any(f => f.EngineName == name);
    }

    private static void TryRegisterV8(IJsEngineSwitcher switcher)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Use reflection to call AddV8 since the V8 package is conditionally included
                var v8Assembly = Assembly.Load("JavaScriptEngineSwitcher.V8");
                if (v8Assembly != null)
                {
                    var extType = v8Assembly.GetType(
                        "JavaScriptEngineSwitcher.V8.JsEngineFactoryCollectionExtensions");
                    if (extType != null)
                    {
                        // Get the EngineFactories type from the switcher's assembly
                        var factoryType = switcher.EngineFactories.GetType();
                        var addV8 = extType.GetMethod("AddV8", new[] { factoryType });
                        if (addV8 != null)
                        {
                            addV8.Invoke(null, new object[] { switcher.EngineFactories });
                            _v8Available = true;
                            Console.WriteLine("[JS] V8 engine registered");
                            return;
                        }
                    }
                }
                Console.WriteLine("[JS] V8 assembly not loaded (Windows but package may not be restored)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS] Cannot register V8: {ex.Message}");
        }
        _v8Available = false;
    }

    private static string GetEngineName(JsEngineType type) => type switch
    {
        JsEngineType.V8 => "V8JsEngine",
        JsEngineType.Jint => JintJsEngine.EngineName,
        JsEngineType.Jurassic => JurassicJsEngine.EngineName,
        _ => JintJsEngine.EngineName
    };
}
