using SkiaSharp;

namespace UpBrowser.Rendering;

public static class FontHelper
{
    private static SKTypeface? _chineseTypeface;
    private static SKTypeface? _monoTypeface;
    private static SKTypeface? _defaultTypeface;
    private static SKTypeface? _emojiTypeface;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _chineseTypeface = FindFont(
            "Noto Sans SC", "Source Han Sans SC", "PingFang SC",
            "Microsoft YaHei", "Microsoft YaHei UI", "Microsoft JhengHei",
            "SimSun", "SimHei",
            "WenQuanYi Micro Hei", "Droid Sans Fallback",
            "Noto Sans CJK SC", "Source Han Sans");

        _monoTypeface = FindFont(
            "Consolas", "Cascadia Code", "Cascadia Mono", "Source Code Pro",
            "Courier New", "DejaVu Sans Mono", "Liberation Mono",
            "Fira Code", "Monaco", "Menlo");

        _defaultTypeface = FindFont(
            "Microsoft YaHei", "Microsoft YaHei UI",
            "PingFang SC", "Noto Sans SC", "Source Han Sans SC",
            "Segoe UI", "Arial", "Helvetica",
            "Liberation Sans", "DejaVu Sans", "Tahoma",
            "Verdana", "San Francisco", "Noto Sans");

        _emojiTypeface = FindFont(
            "Segoe UI Emoji", "Noto Color Emoji", "Apple Color Emoji",
            "Twitter Color Emoji", "EmojiOne Color", "Noto Emoji",
            "Segoe UI Symbol");

        Console.WriteLine($"[FontHelper] Chinese={_chineseTypeface?.FamilyName ?? "null"} " +
            $"Default={_defaultTypeface?.FamilyName ?? "null"} " +
            $"Mono={_monoTypeface?.FamilyName ?? "null"}");
    }

    public static SKPaint CreateMonoPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateMonoFont(float textSize = 12)
    {
        Initialize();
        var typeface = _monoTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        return new SKFont(typeface, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
            LinearMetrics = true,
            Embolden = false,
            ForceAutoHinting = false
        };
    }

    public static SKFont CreateDevToolsFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        return new SKFont(typeface, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
            LinearMetrics = true
        };
    }

    public static SKPaint CreatePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        return new SKFont(typeface, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
        };
    }

    public static SKPaint CreateChinesePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateChineseFont(float textSize = 12)
    {
        Initialize();
        var typeface = _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        return new SKFont(typeface, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
        };
    }

    public static SKTypeface? GetMonoTypeface()
    {
        Initialize();
        return _monoTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetDefaultTypeface()
    {
        Initialize();
        return _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetChineseTypeface()
    {
        Initialize();
        return _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetEmojiTypeface()
    {
        Initialize();
        return _emojiTypeface;
    }

    public static SKPaint CreateEmojiPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateEmojiFont(float textSize = 12)
    {
        Initialize();
        return new SKFont(_emojiTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.SubpixelAntialias,
            Subpixel = true,
        };
    }

    private static SKTypeface? FindFont(params string[] candidates)
    {
        // Try SKTypeface.FromFamilyName first (supports system font lookup by any valid name)
        foreach (var name in candidates)
        {
            try
            {
                var tf = SKTypeface.FromFamilyName(name);
                if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                    return tf;
            }
            catch { }
        }
        // Fallback: exact match via font families array
        try
        {
            var families = SKFontManager.Default.FontFamilies.ToArray();
            foreach (var name in candidates)
            {
                int idx = Array.IndexOf(families, name);
                if (idx >= 0)
                {
                    var tf = SKFontManager.Default.GetFontStyles(idx).CreateTypeface(0);
                    if (tf != null && !string.IsNullOrEmpty(tf.FamilyName))
                        return tf;
                }
            }
        }
        catch { }
        return null;
    }
}
