using System.Diagnostics.CodeAnalysis;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;

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

    public static JsEngineType EffectiveEngineType
    {
        get
        {
            if (_defaultEngineType != JsEngineType.Jint && !JsEngineDownloader.IsEngineDownloaded(_defaultEngineType))
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

        TryRegisterDownloadedEngine(switcher, JsEngineType.V8);
        TryRegisterDownloadedEngine(switcher, JsEngineType.Jurassic);

        if (!IsEngineAvailable(_defaultEngineType))
        {
            var fallback = JsEngineType.Jint;
            Console.WriteLine($"[JS] Engine '{_defaultEngineType}' not available, falling back to '{fallback}'");
            _defaultEngineType = fallback;
        }

        switcher.DefaultEngineName = GetEngineName(_defaultEngineType);
        Console.WriteLine($"[JS] Using {_defaultEngineType} engine ({JsEngineDownloader.GetEngineStatus(_defaultEngineType)})");
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

    public static JsEngineType? GetEngineTypeByName(string? name)
    {
        return name?.ToLowerInvariant() switch
        {
            "v8" => JsEngineType.V8,
            "jint" => JsEngineType.Jint,
            "jurassic" => JsEngineType.Jurassic,
            _ => null
        };
    }

    public static bool IsEngineAvailable(JsEngineType type)
    {
        var name = GetEngineName(type);
        return JsEngineSwitcher.Current.EngineFactories
            .Any(f => f.EngineName == name);
    }

    [RequiresUnreferencedCode("Assembly.LoadFrom requires dynamic assembly loading")]
    [RequiresDynamicCode("Assembly.LoadFrom requires dynamic assembly loading")]
    public static bool TryRegisterDownloadedEngine(IJsEngineSwitcher switcher, JsEngineType type)
    {
        if (!JsEngineDownloader.IsEngineDownloaded(type))
            return false;

        try
        {
            return JsEngineDownloader.TryLoadEngine(type, switcher);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[JS] Failed to register {type}: {ex.Message}");
            return false;
        }
    }

    public static string GetEngineName(JsEngineType type) => type switch
    {
        JsEngineType.V8 => "V8JsEngine",
        JsEngineType.Jint => JintJsEngine.EngineName,
        JsEngineType.Jurassic => "JurassicJsEngine",
        _ => JintJsEngine.EngineName
    };
}
