using System;

namespace UpBrowser.Rendering;

public enum AntiAliasMode
{
    None,
    Normal,
    High,
    Subpixel
}

public enum PerformancePreset
{
    Custom,
    LowPower,
    Balanced,
    HighQuality,
    Ultra
}

public class RenderingSettings
{
    public event Action? OnChanged;
    public event Action<bool>? OnGpuChanged;

    private PerformancePreset _preset = PerformancePreset.Balanced;
    private bool _gpuAcceleration = true;
    private bool _vSync;
    private int _targetFps = 60;
    private AntiAliasMode _antiAliasing = AntiAliasMode.Normal;
    private bool _dirtyRegions = true;
    private bool _pictureCaching = true;
    private bool _smoothScrolling;
    private float _resolutionScale = 1.0f;
    private bool _showFps;
    private bool _showSettingsButton = true;

    public PerformancePreset Preset
    {
        get => _preset;
        set
        {
            if (_preset != value)
            {
                _preset = value;
                ApplyPreset(value);
                NotifyChanged();
            }
        }
    }

    public bool GpuAcceleration
    {
        get => _gpuAcceleration;
        set
        {
            if (_gpuAcceleration != value)
            {
                _gpuAcceleration = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
                OnGpuChanged?.Invoke(value);
            }
        }
    }

    public bool VSync
    {
        get => _vSync;
        set
        {
            if (_vSync != value)
            {
                _vSync = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public int TargetFps
    {
        get => _targetFps;
        set
        {
            var clamped = Math.Clamp(value, 15, 360);
            if (_targetFps != clamped)
            {
                _targetFps = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public AntiAliasMode AntiAliasing
    {
        get => _antiAliasing;
        set
        {
            if (_antiAliasing != value)
            {
                _antiAliasing = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool DirtyRegions
    {
        get => _dirtyRegions;
        set
        {
            if (_dirtyRegions != value)
            {
                _dirtyRegions = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool PictureCaching
    {
        get => _pictureCaching;
        set
        {
            if (_pictureCaching != value)
            {
                _pictureCaching = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool SmoothScrolling
    {
        get => _smoothScrolling;
        set
        {
            if (_smoothScrolling != value)
            {
                _smoothScrolling = value;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public float ResolutionScale
    {
        get => _resolutionScale;
        set
        {
            var clamped = Math.Clamp(value, 0.25f, 3.0f);
            if (Math.Abs(_resolutionScale - clamped) > 0.01f)
            {
                _resolutionScale = clamped;
                _preset = PerformancePreset.Custom;
                NotifyChanged();
            }
        }
    }

    public bool ShowFps
    {
        get => _showFps;
        set
        {
            if (_showFps != value)
            {
                _showFps = value;
                NotifyChanged();
            }
        }
    }

    public bool ShowSettingsButton
    {
        get => _showSettingsButton;
        set
        {
            if (_showSettingsButton != value)
            {
                _showSettingsButton = value;
                NotifyChanged();
            }
        }
    }

    public double FrameTimeMs => _targetFps > 0 ? 1000.0 / _targetFps : 0;

    private void ApplyPreset(PerformancePreset preset)
    {
        switch (preset)
        {
            case PerformancePreset.LowPower:
                _gpuAcceleration = false;
                _vSync = false;
                _targetFps = 30;
                _antiAliasing = AntiAliasMode.None;
                _dirtyRegions = false;
                _pictureCaching = false;
                _resolutionScale = 0.75f;
                break;

            case PerformancePreset.Balanced:
                _gpuAcceleration = false;
                _vSync = false;
                _targetFps = 60;
                _antiAliasing = AntiAliasMode.Normal;
                _dirtyRegions = true;
                _pictureCaching = true;
                _resolutionScale = 1.0f;
                break;

            case PerformancePreset.HighQuality:
                _gpuAcceleration = true;
                _vSync = true;
                _targetFps = 120;
                _antiAliasing = AntiAliasMode.High;
                _dirtyRegions = true;
                _pictureCaching = true;
                _resolutionScale = 1.0f;
                break;

            case PerformancePreset.Ultra:
                _gpuAcceleration = true;
                _vSync = true;
                _targetFps = 0;
                _antiAliasing = AntiAliasMode.Subpixel;
                _dirtyRegions = true;
                _pictureCaching = true;
                _resolutionScale = 1.5f;
                break;
        }
    }

    private void NotifyChanged()
    {
        OnChanged?.Invoke();
    }

    public void ToggleGpu() => GpuAcceleration = !GpuAcceleration;
    public void ToggleVsync() => VSync = !VSync;
    public void ToggleDirtyRegions() => DirtyRegions = !DirtyRegions;
    public void TogglePictureCaching() => PictureCaching = !PictureCaching;
    public void ToggleSmoothScrolling() => SmoothScrolling = !SmoothScrolling;
    public void ToggleFps() => ShowFps = !ShowFps;

    public static string PresetName(PerformancePreset p) => p switch
    {
        PerformancePreset.Custom => "自定义",
        PerformancePreset.LowPower => "低功耗",
        PerformancePreset.Balanced => "均衡",
        PerformancePreset.HighQuality => "高质量",
        PerformancePreset.Ultra => "极致",
        _ => "未知"
    };

    public static string AaName(AntiAliasMode m) => m switch
    {
        AntiAliasMode.None => "无",
        AntiAliasMode.Normal => "普通",
        AntiAliasMode.High => "高",
        AntiAliasMode.Subpixel => "次像素",
        _ => "未知"
    };
}
