using System;
using System.Collections.Generic;
using SkiaSharp;

namespace UpBrowser.Rendering;

public class RenderingSettingsPage
{
    private readonly RenderingSettings _settings;
    private bool _visible;
    private float _scrollOffset;
    private float _panelWidth = 320;
    private float _contentHeight;

    private readonly SKTypeface _typeface;
    private readonly float _dpiScale;

    private int _hoveredItem = -1;
    private bool _draggingSlider;
    private int _draggingSliderIndex = -1;
    private bool _rebuilding;

    private struct SettingItem
    {
        public string Label;
        public string Value;
        public string Category;
        public Action? OnClick;
        public Func<bool>? IsOn;
        public int Index;
        public bool IsSlider;
        public float SliderValue;
        public float SliderMin;
        public float SliderMax;
        public Action<float>? OnSliderChange;
        public string[]? Options;
        public int SelectedOption;
        public Action<int>? OnOptionChange;
    }

    private List<SettingItem> _items = new();
    private int _categoryCount;

    public bool Visible => _visible;
    public float PanelWidth => _panelWidth;

    public event Action? OnChanged;

    public RenderingSettingsPage(RenderingSettings settings, float dpiScale)
    {
        _settings = settings;
        _dpiScale = dpiScale;
        _typeface = FontHelper.GetChineseTypeface() ?? SKTypeface.Default;
        _settings.OnChanged += RebuildItems;
        RebuildItems();
    }

    public void Toggle()
    {
        _visible = !_visible;
        _scrollOffset = 0;
    }

    public void Show() => _visible = true;
    public void Hide() => _visible = false;

    private void RebuildItems()
    {
        if (_rebuilding) return;
        _rebuilding = true;

        _items.Clear();
        _categoryCount = 0;
        int idx = 0;

        AddCategory("性能预设", ref idx);
        AddOption("预设方案", RenderingSettings.PresetName(_settings.Preset), () =>
        {
            CyclePreset();
        }, ref idx);

        AddCategory("渲染引擎", ref idx);
        AddToggle("GPU 加速", _settings.GpuAcceleration, () => _settings.ToggleGpu(), ref idx);
        AddToggle("垂直同步", _settings.VSync, () => _settings.ToggleVsync(), ref idx);
        AddToggle("脏区域渲染", _settings.DirtyRegions, () => _settings.ToggleDirtyRegions(), ref idx);
        AddToggle("画面缓存", _settings.PictureCaching, () => _settings.TogglePictureCaching(), ref idx);

        AddCategory("画质", ref idx);
        AddOptions("抗锯齿", new[] { "无", "普通", "高", "次像素" },
            (int)_settings.AntiAliasing, (i) => _settings.AntiAliasing = (AntiAliasMode)i, ref idx);
        AddSlider("渲染缩放", _settings.ResolutionScale, 0.25f, 3.0f, (v) => _settings.ResolutionScale = v, ref idx);

        AddCategory("帧率", ref idx);
        AddOptions("目标帧率", new[] { "30", "60", "120", "不限" },
            _settings.TargetFps switch { 30 => 0, 60 => 1, 120 => 2, _ => 3 },
            (i) => _settings.TargetFps = i switch { 0 => 30, 1 => 60, 2 => 120, _ => 0 }, ref idx);

        AddCategory("显示", ref idx);
        AddToggle("显示 FPS", _settings.ShowFps, () => _settings.ToggleFps(), ref idx);
        AddToggle("平滑滚动", _settings.SmoothScrolling, () => _settings.ToggleSmoothScrolling(), ref idx);

        _rebuilding = false;
    }

    private void CyclePreset()
    {
        var values = (PerformancePreset[])Enum.GetValues(typeof(PerformancePreset));
        int current = Array.IndexOf(values, _settings.Preset);
        current = (current + 1) % values.Length;
        _settings.Preset = values[current];
    }

    private void AddCategory(string name, ref int idx)
    {
        _items.Add(new SettingItem { Label = name, Category = name, Index = idx++ });
        _categoryCount++;
    }

    private Func<bool> MakeGetter(string label)
    {
        if (label.Contains("GPU")) return () => _settings.GpuAcceleration;
        if (label.Contains("垂直同步")) return () => _settings.VSync;
        if (label.Contains("脏区域")) return () => _settings.DirtyRegions;
        if (label.Contains("画面缓存")) return () => _settings.PictureCaching;
        if (label.Contains("平滑")) return () => _settings.SmoothScrolling;
        if (label.Contains("FPS")) return () => _settings.ShowFps;
        return () => false;
    }

    private void AddToggle(string label, bool isOn, Action onClick, ref int idx)
    {
        var getter = MakeGetter(label);
        _items.Add(new SettingItem
        {
            Label = label,
            Value = isOn ? "ON" : "OFF",
            Category = "",
            OnClick = onClick,
            IsOn = getter,
            Index = idx++
        });
    }

    private void AddOption(string label, string value, Action onClick, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = value,
            Category = "",
            OnClick = onClick,
            Index = idx++
        });
    }

    private void AddOptions(string label, string[] options, int selected, Action<int> onChange, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = options[selected],
            Category = "",
            OnClick = () =>
            {
                int next = (selected + 1) % options.Length;
                onChange(next);
            },
            Options = options,
            SelectedOption = selected,
            OnOptionChange = onChange,
            Index = idx++
        });
    }

    private void AddSlider(string label, float value, float min, float max, Action<float> onChange, ref int idx)
    {
        _items.Add(new SettingItem
        {
            Label = label,
            Value = $"{value:F2}x",
            Category = "",
            IsSlider = true,
            SliderValue = (value - min) / (max - min),
            SliderMin = min,
            SliderMax = max,
            OnSliderChange = onChange,
            Index = idx++
        });
    }

    public void Render(SKCanvas canvas, float windowWidth, float windowHeight, float contentOffset)
    {
        if (!_visible) return;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;
        float panelBottom = windowHeight;

        canvas.Save();

        using var bg = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 240),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, bg);

        using var border = new SKPaint
        {
            Color = new SKColor(200, 200, 200, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        canvas.DrawRoundRect(panelLeft, panelTop, _panelWidth, panelBottom - panelTop, 8, 8, border);

        float headerHeight = 40;
        using var headerBg = new SKPaint
        {
            Color = new SKColor(26, 115, 232),
            Style = SKPaintStyle.Fill
        };
        using var headerPath = new SKPath();
        headerPath.AddRoundRect(new SKRect(panelLeft, panelTop, panelLeft + _panelWidth, panelTop + headerHeight), 8, 8);
        canvas.DrawPath(headerPath, headerBg);
        canvas.DrawRect(panelLeft, panelTop + 4, _panelWidth, headerHeight - 4, headerBg);

        using var headerFont = new SKFont(_typeface, 15);
        using var headerPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("渲染设置", panelLeft + 16, panelTop + 26, SKTextAlign.Left, headerFont, headerPaint);

        float xPos = panelLeft + 12;
        float yPos = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 32;
        float labelWidth = _panelWidth - 60;

        canvas.Save();
        var clipRect = new SKRect(panelLeft, panelTop + headerHeight, panelLeft + _panelWidth, panelBottom);
        canvas.ClipRect(clipRect);

        using var labelFont = new SKFont(_typeface, 12);
        using var valueFont = new SKFont(_typeface, 12);
        using var valuePaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        using var labelPaint = new SKPaint { Color = new SKColor(60, 64, 67), IsAntialias = true };
        using var catPaint = new SKPaint { Color = new SKColor(26, 115, 232), IsAntialias = true };
        using var catFont = new SKFont(_typeface, 11);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            float itemY = yPos;

            if (itemY + itemHeight < panelTop + headerHeight)
            {
                yPos += itemHeight;
                continue;
            }
            if (itemY > panelBottom) break;

            if (!string.IsNullOrEmpty(item.Category))
            {
                canvas.DrawText(item.Label, xPos + 4, itemY + 22, SKTextAlign.Left, catFont, catPaint);
                yPos += itemHeight + 4;
                continue;
            }

            bool isHovered = i == _hoveredItem;
            bool isCategory = !string.IsNullOrEmpty(item.Category);

            if (!isCategory && isHovered)
            {
                using var hoverBg = new SKPaint
                {
                    Color = new SKColor(232, 240, 254),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRoundRect(xPos, itemY, _panelWidth - 24, itemHeight, 4, 4, hoverBg);
            }

            if (item.IsSlider)
            {
                canvas.DrawText(item.Label, xPos + 8, itemY + 21, SKTextAlign.Left, labelFont, labelPaint);

                float sliderLeft = xPos + 8;
                float sliderWidth = _panelWidth - 64;
                float sliderY = itemY + itemHeight / 2;
                float sliderTrackH = 4;

                using var trackPaint = new SKPaint
                {
                    Color = new SKColor(218, 220, 224),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRoundRect(sliderLeft, sliderY - sliderTrackH / 2, sliderWidth, sliderTrackH, 2, 2, trackPaint);

                float fillWidth = item.SliderValue * sliderWidth;
                using var fillPaint = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRoundRect(sliderLeft, sliderY - sliderTrackH / 2, fillWidth, sliderTrackH, 2, 2, fillPaint);

                float thumbX = sliderLeft + fillWidth;
                float thumbR = 6;
                using var thumbPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(thumbX, sliderY, thumbR, thumbPaint);
                using var thumbBorder = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2,
                    IsAntialias = true
                };
                canvas.DrawCircle(thumbX, sliderY, thumbR, thumbBorder);

                string val = $"{_settings.ResolutionScale:F1}x";
                float vw = valueFont.MeasureText(val);
                canvas.DrawText(val, xPos + _panelWidth - 24 - vw - 8, itemY + 21, SKTextAlign.Left, valueFont, valuePaint);
            }
            else if (item.Options != null)
            {
                canvas.DrawText(item.Label, xPos + 8, itemY + 21, SKTextAlign.Left, labelFont, labelPaint);

                string val = item.Value;
                float vw = valueFont.MeasureText(val);
                canvas.DrawText(val, xPos + _panelWidth - 24 - vw - 8, itemY + 21, SKTextAlign.Left, valueFont, valuePaint);

                float arrowX = xPos + _panelWidth - 24;
                using var arrowPaint = new SKPaint
                {
                    Color = new SKColor(26, 115, 232),
                    IsAntialias = true
                };
                canvas.DrawText("›", arrowX - 4, itemY + 21, SKTextAlign.Left, valueFont, arrowPaint);
            }
            else
            {
                canvas.DrawText(item.Label, xPos + 8, itemY + 21, SKTextAlign.Left, labelFont, labelPaint);

                if (item.IsOn != null)
                {
                    DrawToggle(canvas, xPos + _panelWidth - 52, itemY + 6, 36, 20, item.IsOn(), isHovered);
                }
                else
                {
                    string val = item.Value;
                    float vw = valueFont.MeasureText(val);
                    canvas.DrawText(val, xPos + _panelWidth - 24 - vw, itemY + 21, SKTextAlign.Left, valueFont, valuePaint);
                    canvas.DrawText("›", xPos + _panelWidth - 20, itemY + 21, SKTextAlign.Left, valueFont, valuePaint);
                }
            }

            yPos += itemHeight;
        }

        _contentHeight = yPos - (panelTop + headerHeight + 10 - _scrollOffset);

        canvas.Restore();
        canvas.Restore();
    }



    private void DrawToggle(SKCanvas canvas, float x, float y, float w, float h, bool isOn, bool hovered)
    {
        float radius = h / 2;

        using var bg = new SKPaint
        {
            Color = isOn ? new SKColor(26, 115, 232) : new SKColor(189, 193, 198),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(x, y, w, h, radius, radius, bg);

        float thumbX = isOn ? x + w - h : x;
        using var thumb = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(thumbX + radius, y + radius, radius - 2, thumb);

        if (hovered)
        {
            using var glow = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 60),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawCircle(thumbX + radius, y + radius, radius, glow);
        }
    }

    public bool HandleClick(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop) return false;

        float headerHeight = 40;
        float itemY = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 32;
        float xPos = panelLeft + 12;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (!string.IsNullOrEmpty(item.Category))
            {
                itemY += itemHeight + 4;
                continue;
            }

            float y0 = itemY;
            float y1 = itemY + itemHeight;

            if (y >= y0 && y <= y1)
            {
                if (item.IsOn != null)
                {
                    item.OnClick?.Invoke();
                    return true;
                }
                if (item.IsSlider)
                {
                    _draggingSlider = true;
                    _draggingSliderIndex = i;
                    UpdateSliderValue(i, x, xPos, panelLeft);
                    return true;
                }
                if (item.OnClick != null)
                {
                    item.OnClick();
                    OnChanged?.Invoke();
                    return true;
                }
            }

            itemY += itemHeight;
        }

        return true;
    }

    public bool HandleMouseMove(float x, float y, float windowWidth, float contentOffset)
    {
        if (!_visible) return false;

        float panelLeft = windowWidth - _panelWidth;
        float panelTop = contentOffset;

        if (_draggingSlider && _draggingSliderIndex >= 0)
        {
            UpdateSliderValue(_draggingSliderIndex, x, panelLeft + 12, panelLeft);
            return true;
        }

        if (x < panelLeft || x > panelLeft + _panelWidth || y < panelTop)
        {
            _hoveredItem = -1;
            return false;
        }

        float headerHeight = 40;
        float itemY = panelTop + headerHeight + 10 - _scrollOffset;
        float itemHeight = 32;

        int hovered = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (!string.IsNullOrEmpty(_items[i].Category))
            {
                itemY += itemHeight + 4;
                continue;
            }

            if (y >= itemY && y <= itemY + itemHeight)
            {
                hovered = i;
                break;
            }
            itemY += itemHeight;
        }

        _hoveredItem = hovered;
        return true;
    }

    public void HandleMouseUp()
    {
        if (_draggingSlider && _pendingSliderValue >= 0 && _draggingSliderIndex >= 0)
        {
            var item = _items[_draggingSliderIndex];
            item.OnSliderChange?.Invoke(_pendingSliderValue);
            _pendingSliderValue = -1f;
        }
        _draggingSlider = false;
        _draggingSliderIndex = -1;
    }

    public bool HandleWheel(float delta, float windowHeight, float contentOffset)
    {
        if (!_visible) return false;

        _scrollOffset = Math.Clamp(_scrollOffset - delta * 0.5f, 0, Math.Max(0, _contentHeight - (windowHeight - contentOffset - 40) + 40));
        return true;
    }

    private float _pendingSliderValue = -1f;

    private void UpdateSliderValue(int index, float mouseX, float xPos, float panelLeft)
    {
        var item = _items[index];
        float sliderLeft = xPos + 8;
        float sliderWidth = _panelWidth - 64;

        float t = (mouseX - sliderLeft) / sliderWidth;
        t = Math.Clamp(t, 0, 1);

        float val = item.SliderMin + t * (item.SliderMax - item.SliderMin);
        val = MathF.Round(val / 0.25f) * 0.25f;

        // Store pending value, don't trigger heavy operations yet
        _pendingSliderValue = val;

        // Update local display value only
        item.SliderValue = (val - item.SliderMin) / (item.SliderMax - item.SliderMin);
        _items[index] = item;
    }

    public int GetScrollOffset() => (int)_scrollOffset;
}
