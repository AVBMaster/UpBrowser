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
            "Courier New", "DejaVu Sans Mono", "Liberation Mono");

        _chineseTypeface = FindFont(families, "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei",
            "Source Han Sans SC", "Noto Sans SC", "PingFang SC", "WenQuanYi Micro Hei", "Droid Sans Fallback");

        _defaultTypeface = FindFont(families, "Segoe UI", "Arial", "Tahoma", "Verdana",
            "Liberation Sans", "DejaVu Sans");

        _emojiTypeface = FindFont(families, "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji",
            "Twitter Color Emoji", "EmojiOne Color", "Noto Emoji", "Segoe UI Symbol");
    }

    public static SKPaint CreateMonoPaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            Typeface = _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default,
        };
    }

    public static SKPaint CreatePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            Typeface = _defaultTypeface ?? SKTypeface.Default,
        };
    }

    public static SKPaint CreateChinesePaint(float textSize = 12)
    {
        Initialize();
        return new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            Typeface = _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default,
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
        return new SKPaint
        {
            TextSize = textSize,
            IsAntialias = true,
            Typeface = _emojiTypeface ?? _defaultTypeface ?? SKTypeface.Default,
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
