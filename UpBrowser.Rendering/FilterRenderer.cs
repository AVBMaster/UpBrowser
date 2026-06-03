using SkiaSharp;

namespace UpBrowser.Rendering;

public static class FilterRenderer
{
    public static SKImageFilter? ParseAndChain(string? filterString)
    {
        if (string.IsNullOrWhiteSpace(filterString) || filterString == "none")
            return null;

        var filters = ParseFilters(filterString);
        if (filters.Count == 0) return null;

        SKImageFilter? result = null;
        foreach (var filter in filters)
        {
            var f = CreateFilter(filter);
            if (f != null)
            {
                result = result != null ? SKImageFilter.CreateCompose(result, f) : f;
            }
        }
        return result;
    }

    private static List<FilterEntry> ParseFilters(string input)
    {
        var filters = new List<FilterEntry>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '(') depth++;
            else if (input[i] == ')') depth--;
            else if (input[i] == ' ' && depth == 0)
            {
                var part = input[start..i].Trim();
                if (!string.IsNullOrEmpty(part))
                    filters.Add(ParseFilterEntry(part));
                start = i + 1;
            }
        }
        var last = input[start..].Trim();
        if (!string.IsNullOrEmpty(last))
            filters.Add(ParseFilterEntry(last));
        return filters;
    }

    private static FilterEntry ParseFilterEntry(string s)
    {
        var parenIdx = s.IndexOf('(');
        if (parenIdx > 0 && s.EndsWith(')'))
        {
            var name = s[..parenIdx].Trim().ToLowerInvariant();
            var argStr = s[(parenIdx + 1)..^1].Trim();
            var args = argStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim()).ToArray();
            return new FilterEntry { Name = name, Args = args };
        }
        return new FilterEntry { Name = s.Trim().ToLowerInvariant(), Args = Array.Empty<string>() };
    }

    private static SKImageFilter? CreateFilter(FilterEntry entry)
    {
        return entry.Name switch
        {
            "blur" => CreateBlur(entry.Args),
            "brightness" => CreateColorMatrix(entry.Args, BrightnessMatrix),
            "contrast" => CreateColorMatrix(entry.Args, ContrastMatrix),
            "saturate" => CreateColorMatrix(entry.Args, SaturateMatrix),
            "grayscale" => CreateGrayscale(entry.Args),
            "sepia" => CreateSepia(entry.Args),
            "invert" => CreateInvert(entry.Args),
            "hue-rotate" => CreateHueRotate(entry.Args),
            "opacity" => CreateOpacity(entry.Args),
            "drop-shadow" => CreateDropShadow(entry.Args),
            _ => null
        };
    }

    private static SKImageFilter? CreateBlur(string[] args)
    {
        if (args.Length < 1 || !float.TryParse(args[0].Replace("px", ""), out var radius))
            return null;
        radius = Math.Max(0, radius);
        return SKImageFilter.CreateBlur(radius, radius);
    }

    private static SKImageFilter? CreateColorMatrix(string[] args, Func<float, float[]> matrixFunc)
    {
        if (args.Length < 1) return null;
        var valStr = args[0].Trim();
        bool isPercent = valStr.EndsWith("%");
        valStr = valStr.Replace("%", "").Trim();
        if (!float.TryParse(valStr, out var amount)) return null;

        // CSS: brightness(2) = 200% brightness, brightness(50%) = 50% brightness
        // If no % sign, treat as multiplier (e.g., 2 → 200%)
        if (!isPercent)
            amount *= 100f;

        var matrix = matrixFunc(amount);
        var colorMatrix = SKColorFilter.CreateColorMatrix(matrix);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static float[] BrightnessMatrix(float amount)
    {
        amount /= 100f;
        return new float[]
        {
            amount, 0, 0, 0, 0,
            0, amount, 0, 0, 0,
            0, 0, amount, 0, 0,
            0, 0, 0, 1, 0
        };
    }

    private static float[] ContrastMatrix(float amount)
    {
        amount /= 100f;
        float t = (1f - amount) / 2f;
        return new float[]
        {
            amount, 0, 0, 0, t,
            0, amount, 0, 0, t,
            0, 0, amount, 0, t,
            0, 0, 0, 1, 0
        };
    }

    private static float[] SaturateMatrix(float amount)
    {
        amount /= 100f;
        float[] m = new float[20];
        m[0] = 0.2126f + 0.7874f * amount;
        m[1] = 0.7152f - 0.7152f * amount;
        m[2] = 0.0722f - 0.0722f * amount;
        m[5] = 0.2126f - 0.2126f * amount;
        m[6] = 0.7152f + 0.2848f * amount;
        m[7] = 0.0722f - 0.0722f * amount;
        m[10] = 0.2126f - 0.2126f * amount;
        m[11] = 0.7152f - 0.7152f * amount;
        m[12] = 0.0722f + 0.9278f * amount;
        m[15] = 1; m[16] = 1; m[17] = 1; m[18] = 1;
        return m;
    }

    private static SKImageFilter? CreateGrayscale(string[] args)
    {
        float amount = 100f;
        if (args.Length >= 1)
        {
            var valStr = args[0].Trim();
            bool isPercent = valStr.EndsWith("%");
            valStr = valStr.Replace("%", "").Trim();
            float.TryParse(valStr, out amount);
            if (!isPercent)
                amount *= 100f;
        }
        // grayscale(a%) = saturate(100% - a%)
        float satAmount = Math.Max(0, 100f - amount);
        return CreateColorMatrix(new[] { satAmount.ToString() }, SaturateMatrix);
    }

    private static SKImageFilter? CreateSepia(string[] args)
    {
        float[] m = new float[]
        {
            0.393f, 0.769f, 0.189f, 0, 0,
            0.349f, 0.686f, 0.168f, 0, 0,
            0.272f, 0.534f, 0.131f, 0, 0,
            0, 0, 0, 1, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateInvert(string[] args)
    {
        float[] m = new float[]
        {
            -1, 0, 0, 0, 1,
            0, -1, 0, 0, 1,
            0, 0, -1, 0, 1,
            0, 0, 0, 1, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateHueRotate(string[] args)
    {
        if (args.Length < 1) return null;
        var valStr = args[0].Replace("deg", "").Trim();
        if (!float.TryParse(valStr, out var degrees)) return null;

        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float[] m = new float[]
        {
            0.213f + cos * 0.787f - sin * 0.213f,
            0.715f - cos * 0.715f - sin * 0.715f,
            0.072f - cos * 0.072f + sin * 0.928f, 0, 0,
            0.213f - cos * 0.213f + sin * 0.143f,
            0.715f + cos * 0.285f + sin * 0.140f,
            0.072f - cos * 0.072f - sin * 0.283f, 0, 0,
            0.213f - cos * 0.213f - sin * 0.787f,
            0.715f - cos * 0.715f + sin * 0.715f,
            0.072f + cos * 0.928f + sin * 0.072f, 0, 0,
            0, 0, 0, 1, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateOpacity(string[] args)
    {
        if (args.Length < 1) return null;
        var valStr = args[0].Replace("%", "").Trim();
        if (!float.TryParse(valStr, out var amount)) return null;
        amount /= 100f;

        float[] m = new float[]
        {
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, amount, 0
        };
        var colorMatrix = SKColorFilter.CreateColorMatrix(m);
        return SKImageFilter.CreateColorFilter(colorMatrix);
    }

    private static SKImageFilter? CreateDropShadow(string[] args)
    {
        float offsetX = 0, offsetY = 0, blur = 0;
        SKColor color = SKColors.Black;

        if (args.Length >= 2)
        {
            float.TryParse(args[0].Replace("px", "").Trim(), out offsetX);
            float.TryParse(args[1].Replace("px", "").Trim(), out offsetY);
        }
        if (args.Length >= 3)
        {
            var blurStr = args[2].Replace("px", "").Trim();
            float.TryParse(blurStr, out blur);
        }
        if (args.Length >= 4)
        {
            color = ParseFilterColor(args[3]) ?? SKColors.Black;
        }

        return SKImageFilter.CreateDropShadow(offsetX, offsetY, blur, blur, color);
    }

    private static SKColor? ParseFilterColor(string s)
    {
        s = s.Trim();
        if (s.StartsWith("#") && SKColor.TryParse(s, out var c)) return c;
        if (s.StartsWith("rgba") || s.StartsWith("rgb"))
        {
            try
            {
                var inner = s[s.IndexOf('(')..].Trim('(', ')');
                var parts = inner.Split(',');
                if (parts.Length >= 3)
                {
                    byte r = byte.Parse(parts[0].Trim());
                    byte g = byte.Parse(parts[1].Trim());
                    byte b = byte.Parse(parts[2].Trim());
                    byte a = parts.Length > 3 ? (byte)(float.Parse(parts[3].Trim()) * 255) : (byte)255;
                    return new SKColor(r, g, b, a);
                }
            }
            catch { }
        }
        return null;
    }

    private class FilterEntry
    {
        public string Name { get; set; } = "";
        public string[] Args { get; set; } = Array.Empty<string>();
    }
}
