using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpBrowser.Core.JavaScript;

/// <summary>
/// 管理 JS 引擎安装信息（从本地目录安装的引擎路径等元数据）。
/// 存储于 %APPDATA%\UpBrowser\engine_registry.json。
/// </summary>
public static class JsEngineInfoRegistry
{
    private static string RegistryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UpBrowser", "engine_registry.json");

    private static EngineRegistry? _data;

    public static EngineRegistry Data
    {
        get
        {
            if (_data != null) return _data;
            if (File.Exists(RegistryPath))
            {
                try
                {
                    var json = File.ReadAllText(RegistryPath);
                    _data = JsonSerializer.Deserialize<EngineRegistry>(json);
                }
                catch { _data = new EngineRegistry(); }
            }
            _data ??= new EngineRegistry();
            return _data;
        }
    }

    public static void Register(JsEngineType type, string assemblyPath, string sourceFolder)
    {
        var data = Data;
        data.Engines[type.ToString().ToLowerInvariant()] = new EngineInfo
        {
            AssemblyPath = assemblyPath,
            SourceFolder = sourceFolder,
            InstalledAt = DateTime.UtcNow.ToString("O")
        };
        Save();
    }

    public static EngineInfo? GetInfo(JsEngineType type)
    {
        var data = Data;
        var key = type.ToString().ToLowerInvariant();
        return data.Engines.TryGetValue(key, out var info) ? info : null;
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RegistryPath, json);
    }

    public class EngineRegistry
    {
        [JsonConstructor]
        public EngineRegistry() { Engines = new Dictionary<string, EngineInfo>(); }

        public Dictionary<string, EngineInfo> Engines { get; set; }
    }

    public class EngineInfo
    {
        [JsonPropertyName("assemblyPath")]
        public string AssemblyPath { get; set; } = "";

        [JsonPropertyName("sourceFolder")]
        public string SourceFolder { get; set; } = "";

        [JsonPropertyName("installedAt")]
        public string InstalledAt { get; set; } = "";
    }
}
