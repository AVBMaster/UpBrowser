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

        Console.WriteLine("[FontHelper.Initialize] Finding Chinese font...");
        _chineseTypeface = FindFont(
            "Noto Sans SC", "Source Han Sans SC", "PingFang SC",
            "Microsoft YaHei", "Microsoft YaHei UI", "Microsoft JhengHei",
            "SimSun", "SimHei",
            "WenQuanYi Micro Hei", "Droid Sans Fallback",
            "Noto Sans CJK SC", "Source Han Sans");
        Console.WriteLine($"[FontHelper.Initialize] Chinese: {_chineseTypeface?.FamilyName ?? "null"}");

        Console.WriteLine("[FontHelper.Initialize] Finding Mono font...");
        _monoTypeface = FindFont(
            "Consolas", "Cascadia Code", "Cascadia Mono", "Source Code Pro",
            "Courier New", "DejaVu Sans Mono", "Liberation Mono",
            "Fira Code", "Monaco", "Menlo");
        Console.WriteLine($"[FontHelper.Initialize] Mono: {_monoTypeface?.FamilyName ?? "null"}");

        Console.WriteLine("[FontHelper.Initialize] Finding Default font...");
        _defaultTypeface = FindFont(
            "Microsoft YaHei", "Microsoft YaHei UI",
            "PingFang SC", "Noto Sans SC", "Source Han Sans SC",
            "Segoe UI", "Arial", "Helvetica",
            "Liberation Sans", "DejaVu Sans", "Tahoma",
            "Verdana", "San Francisco", "Noto Sans");
        Console.WriteLine($"[FontHelper.Initialize] Default: {_defaultTypeface?.FamilyName ?? "null"}");

        Console.WriteLine("[FontHelper.Initialize] Finding Emoji font...");
        _emojiTypeface = FindFont(
            "Segoe UI Emoji", "Noto Color Emoji", "Apple Color Emoji",
            "Twitter Color Emoji", "EmojiOne Color", "Noto Emoji",
            "Segoe UI Symbol");
        Console.WriteLine($"[FontHelper.Initialize] Emoji: {_emojiTypeface?.FamilyName ?? "null"}");

        Console.WriteLine($"[FontHelper.Initialize] All done: CN={_chineseTypeface?.FamilyName ?? "null"} Def={_defaultTypeface?.FamilyName ?? "null"} Mono={_monoTypeface?.FamilyName ?? "null"}");
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
        var font = new SKFont(typeface, textSize);
        try {
            #if !SUPPORT_WINXP
            font.Hinting = SKFontHinting.Normal;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            font.LinearMetrics = true;
            font.Embolden = false;
            font.ForceAutoHinting = false;
            Console.WriteLine("[FontHelper.CreateMonoFont] Font options OK");
            #else
            Console.WriteLine("[FontHelper.CreateMonoFont] Font options skipped (XP)");
            #endif
        }
        catch (Exception ex) { Console.WriteLine($"[FontHelper.CreateMonoFont] Font options skipped: {ex.GetType().Name}: {ex.Message}"); }
        return font;
    }

    public static SKFont CreateDevToolsFont(float textSize = 12)
    {
        Console.WriteLine("[FontHelper.CreateDevToolsFont] Initialize");
        Initialize();
        Console.WriteLine("[FontHelper.CreateDevToolsFont] Initialize OK");

        Console.WriteLine("[FontHelper.CreateDevToolsFont] Getting typeface...");
        var typeface = _chineseTypeface ?? _monoTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        Console.WriteLine($"[FontHelper.CreateDevToolsFont] Typeface: {typeface?.FamilyName ?? "null"}");

        Console.WriteLine("[FontHelper.CreateDevToolsFont] Creating SKFont...");
        var font = new SKFont(typeface, textSize);
        Console.WriteLine("[FontHelper.CreateDevToolsFont] SKFont constructor OK");

        try {
            Console.WriteLine("[FontHelper.CreateDevToolsFont] Applying font options...");
            #if !SUPPORT_WINXP
            font.Hinting = SKFontHinting.Normal;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            font.LinearMetrics = true;
            #endif
            Console.WriteLine("[FontHelper.CreateDevToolsFont] SKFont options OK");
        }
        catch (Exception ex) {
            Console.WriteLine($"[FontHelper.CreateDevToolsFont] Font options skipped: {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine("[FontHelper.CreateDevToolsFont] SKFont done");

        return font;
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
        var font = new SKFont(typeface, textSize);
        try
        {
            #if !SUPPORT_WINXP
            font.Hinting = SKFontHinting.Normal;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            Console.WriteLine("[FontHelper.CreateFont] Font options OK");
            #else
            Console.WriteLine("[FontHelper.CreateFont] Font options skipped (XP)");
            #endif
        }
        catch (Exception ex) { Console.WriteLine($"[FontHelper.CreateFont] Font options skipped: {ex.GetType().Name}: {ex.Message}"); }
        return font;
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
        var font = new SKFont(typeface, textSize);
        try
        {
            #if !SUPPORT_WINXP
            font.Hinting = SKFontHinting.Normal;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            Console.WriteLine("[FontHelper.CreateChineseFont] Font options OK");
            #else
            Console.WriteLine("[FontHelper.CreateChineseFont] Font options skipped (XP)");
            #endif
        }
        catch (Exception ex) { Console.WriteLine($"[FontHelper.CreateChineseFont] Font options skipped: {ex.GetType().Name}: {ex.Message}"); }
        return font;
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
        var typeface = _emojiTypeface ?? _chineseTypeface ?? _defaultTypeface ?? SKTypeface.Default;
        var font = new SKFont(typeface, textSize);
        try
        {
            #if !SUPPORT_WINXP
            font.Hinting = SKFontHinting.Normal;
            font.Edging = SKFontEdging.SubpixelAntialias;
            font.Subpixel = true;
            Console.WriteLine("[FontHelper.CreateEmojiFont] Font options OK");
            #else
            Console.WriteLine("[FontHelper.CreateEmojiFont] Font options skipped (XP)");
            #endif
        }
        catch (Exception ex) { Console.WriteLine($"[FontHelper.CreateEmojiFont] Font options skipped: {ex.GetType().Name}: {ex.Message}"); }
        return font;
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
