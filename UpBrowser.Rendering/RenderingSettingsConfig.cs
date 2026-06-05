using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpBrowser.Rendering;

public static class RenderingSettingsConfig
{
    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "UpBrowser");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private class ConfigData
    {
        public PerformancePreset Preset { get; set; } = PerformancePreset.Balanced;
        public bool GpuAcceleration { get; set; } = true;
        public bool VSync { get; set; }
        public int TargetFps { get; set; } = 60;
        public AntiAliasMode AntiAliasing { get; set; } = AntiAliasMode.Normal;
        public bool DirtyRegions { get; set; } = true;
        public bool PictureCaching { get; set; } = true;
        public bool SmoothScrolling { get; set; }
        public float ResolutionScale { get; set; } = 1.0f;
        public bool ShowFps { get; set; }
    }

    public static void Save(RenderingSettings settings)
    {
        var data = new ConfigData
        {
            Preset = settings.Preset,
            GpuAcceleration = settings.GpuAcceleration,
            VSync = settings.VSync,
            TargetFps = settings.TargetFps,
            AntiAliasing = settings.AntiAliasing,
            DirtyRegions = settings.DirtyRegions,
            PictureCaching = settings.PictureCaching,
            SmoothScrolling = settings.SmoothScrolling,
            ResolutionScale = settings.ResolutionScale,
            ShowFps = settings.ShowFps
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(GetConfigPath(), json);
    }

    public static void Load(RenderingSettings settings)
    {
        var path = GetConfigPath();
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<ConfigData>(json);
            if (data == null) return;

            settings.Preset = data.Preset;
            settings.GpuAcceleration = data.GpuAcceleration;
            settings.VSync = data.VSync;
            settings.TargetFps = data.TargetFps;
            settings.AntiAliasing = data.AntiAliasing;
            settings.DirtyRegions = data.DirtyRegions;
            settings.PictureCaching = data.PictureCaching;
            settings.SmoothScrolling = data.SmoothScrolling;
            settings.ResolutionScale = data.ResolutionScale;
            settings.ShowFps = data.ShowFps;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to load settings: {ex.Message}");
        }
    }
}
