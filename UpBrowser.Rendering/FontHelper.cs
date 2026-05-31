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

        var families = SKFontManager.Default.FontFamilies.ToArray();

        _monoTypeface = FindFont(families, "Consolas", "Cascadia Code", "Cascadia Mono", "Source Code Pro",
            "Courier New", "DejaVu Sans Mono", "Liberation Mono", "Fira Code", "Monaco", "Menlo");

        _chineseTypeface = FindFont(families, "Noto Sans SC", "Source Han Sans SC", "PingFang SC",
            "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei",
            "WenQuanYi Micro Hei", "Droid Sans Fallback");

        _defaultTypeface = FindFont(families, "Liberation Sans", "DejaVu Sans", "Arial", "Helvetica",
            "Segoe UI", "Tahoma", "Verdana", "San Francisco", "Noto Sans");

        _emojiTypeface = FindFont(families, "Noto Color Emoji", "Apple Color Emoji", "Segoe UI Emoji",
            "Twitter Color Emoji", "EmojiOne Color", "Noto Emoji", "Segoe UI Symbol");
    }

    public static SKPaint CreateMonoPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint { IsAntialias = true };
    }

    public static SKFont CreateMonoFont(float textSize = 12)
    {
        Initialize();
        var typeface = _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        return new SKFont(typeface, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.Antialias,
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
            Edging = SKFontEdging.Antialias,
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
        return new SKFont(_defaultTypeface ?? SKTypeface.Default, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.Antialias
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
        return new SKFont(_chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.Antialias
        };
    }

    public static SKTypeface? GetMonoTypeface()
    {
        Initialize();
        return _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default;
    }

    public static SKTypeface? GetDefaultTypeface()
    {
        Initialize();
        return _defaultTypeface ?? SKTypeface.Default;
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
        return new SKFont(_emojiTypeface ?? _defaultTypeface ?? SKTypeface.Default, textSize)
        {
            Hinting = SKFontHinting.Normal,
            Edging = SKFontEdging.Antialias
        };
    }

    private static SKTypeface? FindFont(string[] families, params string[] candidates)
    {
        foreach (var name in candidates)
        {
            int idx = Array.IndexOf(families, name);
            if (idx >= 0)
            {
                try
                {
                    var typeface = SKFontManager.Default.GetFontStyles(idx).CreateTypeface(0);
                    if (typeface != null && typeface.FamilyName != null)
                        return typeface;
                }
                catch { }
            }
        }
        return null;
    }
}
