using SkiaSharp;
using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

/// <summary>
/// Cross-platform color parser supporting all CSS color formats.
/// </summary>
public static class ColorParser
{
    private static readonly Regex RgbFuncRegex = new(@"rgba?\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HslFuncRegex = new(@"hsla?\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HwbRegex = new(@"hwb\s*\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*(?:\s*/\s*([\d.]+%?))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LabRegex = new(@"lab\s*\(\s*([\d.]+)%?\s*([+-]\s*[\d.]+)\s*([+-]\s*[\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LchRegex = new(@"lch\s*\(\s*([\d.]+)%?\s*([\d.]+)\s*([\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OklabRegex = new(@"oklab\s*\(\s*([\d.]+)\s*([+-]\s*[\d.]+)\s*([+-]\s*[\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OklchRegex = new(@"oklch\s*\(\s*([\d.]+)\s*([\d.]+)\s*([\d.]+)\s*(?:\s*/\s*([\d.]+))?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex8Regex = new(@"^#([0-9a-f]{8})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex6Regex = new(@"^#([0-9a-f]{6})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex4Regex = new(@"^#([0-9a-f]{4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Hex3Regex = new(@"^#([0-9a-f]{3})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SKColor Parse(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "transparent" || value == "inherit")
            return SKColors.Transparent;

        value = value.Trim();

        var hex = ParseHex(value);
        if (hex.HasValue) return hex.Value;

        var rgb = ParseRgb(value);
        if (rgb.HasValue) return rgb.Value;

        var hsl = ParseHsl(value);
        if (hsl.HasValue) return hsl.Value;

        var hwb = ParseHwb(value);
        if (hwb.HasValue) return hwb.Value;

        var lab = ParseLab(value);
        if (lab.HasValue) return lab.Value;

        var lch = ParseLch(value);
        if (lch.HasValue) return lch.Value;

        var oklab = ParseOklab(value);
        if (oklab.HasValue) return oklab.Value;

        var oklch = ParseOklch(value);
        if (oklch.HasValue) return oklch.Value;

        var lightDark = ParseLightDark(value);
        if (lightDark.HasValue) return lightDark.Value;

        var colorMix = ParseColorMix(value);
        if (colorMix.HasValue) return colorMix.Value;

        var colorFn = ParseColorFunction(value);
        if (colorFn.HasValue) return colorFn.Value;

        var current = ParseCurrentColor(value);
        if (current.HasValue) return current.Value;

        return GetNamedColor(value);
    }

    private static SKColor? ParseHex(string value)
    {
        if (!value.StartsWith("#")) return null;

        var match8 = Hex8Regex.Match(value);
        if (match8.Success)
        {
            var hex = match8.Groups[1].Value;
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16));
        }

        var match6 = Hex6Regex.Match(value);
        if (match6.Success)
        {
            var hex = match6.Groups[1].Value;
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        var match4 = Hex4Regex.Match(value);
        if (match4.Success)
        {
            var hex = match4.Groups[1].Value;
            return new SKColor(
                Convert.ToByte($"{hex[0]}{hex[0]}", 16),
                Convert.ToByte($"{hex[1]}{hex[1]}", 16),
                Convert.ToByte($"{hex[2]}{hex[2]}", 16),
                Convert.ToByte($"{hex[3]}{hex[3]}", 16));
        }

        var match3 = Hex3Regex.Match(value);
        if (match3.Success)
        {
            var hex = match3.Groups[1].Value;
            return new SKColor(
                Convert.ToByte($"{hex[0]}{hex[0]}", 16),
                Convert.ToByte($"{hex[1]}{hex[1]}", 16),
                Convert.ToByte($"{hex[2]}{hex[2]}", 16));
        }

        return null;
    }

    private static SKColor? ParseRgb(string value)
    {
        var match = RgbFuncRegex.Match(value);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        var parts = ParseColorFunctionArgs(inner);
        if (parts.Count < 3) return null;

        byte r = ParseColorChannel(parts[0], 255);
        byte g = ParseColorChannel(parts[1], 255);
        byte b = ParseColorChannel(parts[2], 255);
        byte alpha = 255;

        if (parts.Count >= 4)
            alpha = (byte)Math.Clamp(MathF.Round(ParseAlpha(parts[3]) * 255), 0, 255);

        return new SKColor(r, g, b, alpha);
    }

    private static SKColor? ParseHsl(string value)
    {
        var match = HslFuncRegex.Match(value);
        if (!match.Success) return null;

        var inner = ExtractFunctionInner(value, match.Index);
        if (inner == null) return null;

        var parts = ParseColorFunctionArgs(inner);
        if (parts.Count < 3) return null;

        float h = float.Parse(parts[0]);
        float s = ParsePercent(parts[1]);
        float l = ParsePercent(parts[2]);
        float alpha = 1.0f;

        if (parts.Count >= 4)
            alpha = ParseAlpha(parts[3]);

        return HslToRgb(h, s, l, alpha);
    }

    private static string? ExtractFunctionInner(string value, int funcStart)
    {
        int parenStart = value.IndexOf('(', funcStart);
        if (parenStart < 0) return null;
        int depth = 1;
        int i = parenStart + 1;
        while (i < value.Length && depth > 0)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            if (depth > 0) i++;
        }
        return depth == 0 ? value[(parenStart + 1)..i] : null;
    }

    private static List<string> ParseColorFunctionArgs(string inner)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '(') depth++;
            else if (inner[i] == ')') depth--;
            else if (depth == 0 && inner[i] == ',')
            {
                result.Add(inner[start..i].Trim());
                start = i + 1;
            }
            else if (depth == 0 && inner[i] == '/')
            {
                result.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }

        var last = inner[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }

    private static byte ParseColorChannel(string value, byte max)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
        {
            float pct = float.Parse(value[..^1]);
            return (byte)Math.Clamp(MathF.Round(pct / 100f * max), 0, max);
        }
        return byte.Parse(value);
    }

    private static float ParseAlpha(string value)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
            return float.Parse(value[..^1]) / 100f;
        return float.Parse(value);
    }

    private static float ParsePercent(string value)
    {
        value = value.Trim();
        if (value.EndsWith("%"))
            return float.Parse(value[..^1]) / 100f;
        return float.Parse(value);
    }

    private static SKColor HslToRgb(float h, float s, float l, float a)
    {
        h = ((h % 360) + 360) % 360;
        float c = (1 - Math.Abs(2 * l - 1)) * s;
        float x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        float m = l - c / 2;

        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new SKColor(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255),
            (byte)(a * 255));
    }

    private static SKColor? ParseHwb(string value)
    {
        var match = HwbRegex.Match(value);
        if (!match.Success) return null;

        float h = float.Parse(match.Groups[1].Value);
        float w = float.Parse(match.Groups[2].Value) / 100f;
        float b = float.Parse(match.Groups[3].Value) / 100f;
        float alpha = 1f;
        if (match.Groups[4].Success)
        {
            var aVal = match.Groups[4].Value;
            alpha = aVal.EndsWith("%") ? float.Parse(aVal[..^1]) / 100f : float.Parse(aVal);
        }

        h = ((h % 360) + 360) % 360;
        w = MathF.Min(w, 1f);
        b = MathF.Min(b, 1f);
        float sum = w + b;
        if (sum > 1f) { w /= sum; b /= sum; }

        // HWB to RGB: compute hue with full saturation, then apply whiteness and blackness
        float hue = h;
        float c = 1f;
        float x = c * (1 - Math.Abs((hue / 60f) % 2 - 1));
        float r = 0, g = 0, bb = 0;
        if (hue < 60) { r = c; g = x; }
        else if (hue < 120) { r = x; g = c; }
        else if (hue < 180) { r = 0; g = c; bb = x; }
        else if (hue < 240) { r = 0; g = x; bb = c; }
        else if (hue < 300) { r = x; g = 0; bb = c; }
        else { r = c; g = 0; bb = x; }

        // Apply whiteness and blackness
        r = r * (1f - w - b) + w;
        g = g * (1f - w - b) + w;
        bb = bb * (1f - w - b) + w;

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(bb * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static SKColor? ParseLab(string value)
    {
        var match = LabRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float a = float.Parse(match.Groups[2].Value);
        float bb = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        // Approximate Lab -> sRGB via simple conversion
        l = Math.Clamp(l, 0, 100);
        float fy = (l + 16) / 116;
        float fx = a / 500 + fy;
        float fz = fy - bb / 200;

        float x = LabLinearComponent(fx) * 0.96422f;
        float y = LabLinearComponent(fy) * 1f;
        float z = LabLinearComponent(fz) * 0.82521f;

        return XyzToSrgb(x, y, z, alpha);
    }

    private static SKColor? ParseLch(string value)
    {
        var match = LchRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float c = float.Parse(match.Groups[2].Value);
        float h = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        float a = c * MathF.Cos(h * MathF.PI / 180f);
        float bb = c * MathF.Sin(h * MathF.PI / 180f);

        float fy = (l + 16) / 116;
        float fx = a / 500 + fy;
        float fz = fy - bb / 200;

        float x = LabLinearComponent(fx) * 0.96422f;
        float y = LabLinearComponent(fy) * 1f;
        float z = LabLinearComponent(fz) * 0.82521f;

        return XyzToSrgb(x, y, z, alpha);
    }

    private static SKColor? ParseOklab(string value)
    {
        var match = OklabRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float a = float.Parse(match.Groups[2].Value);
        float bb = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        // OKLab -> linear sRGB conversion
        l = Math.Clamp(l, 0, 1);
        float l_ = l + 0.3963377774f * a + 0.2158037573f * bb;
        float m_ = l - 0.1055613458f * a - 0.0638541728f * bb;
        float s_ = l - 0.0894841775f * a - 1.2914855480f * bb;

        float l3 = l_ * l_ * l_;
        float m3 = m_ * m_ * m_;
        float s3 = s_ * s_ * s_;

        float r = 4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3;
        float g = -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3;
        float b = -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static SKColor? ParseOklch(string value)
    {
        var match = OklchRegex.Match(value);
        if (!match.Success) return null;

        float l = float.Parse(match.Groups[1].Value);
        float c = float.Parse(match.Groups[2].Value);
        float h = float.Parse(match.Groups[3].Value);
        float alpha = 1f;
        if (match.Groups[4].Success)
            alpha = float.Parse(match.Groups[4].Value);

        float a = c * MathF.Cos(h * MathF.PI / 180f);
        float bb = c * MathF.Sin(h * MathF.PI / 180f);

        // OKLab -> linear sRGB (reuse oklab logic)
        l = Math.Clamp(l, 0, 1);
        float l_ = l + 0.3963377774f * a + 0.2158037573f * bb;
        float m_ = l - 0.1055613458f * a - 0.0638541728f * bb;
        float s_ = l - 0.0894841775f * a - 1.2914855480f * bb;

        float l3 = l_ * l_ * l_;
        float m3 = m_ * m_ * m_;
        float s3 = s_ * s_ * s_;

        float r = 4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3;
        float g = -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3;
        float b = -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static SKColor? ParseLightDark(string value)
    {
        var match = Regex.Match(value, @"light-dark\s*\(\s*([^,]+)\s*,\s*(.+)\s*\)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        // Return the light color value (default to light theme)
        return Parse(match.Groups[1].Value.Trim());
    }

    private static SKColor? ParseColorMix(string value)
    {
        var match = Regex.Match(value, @"color-mix\s*\((.*)\)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var inner = match.Groups[1].Value.Trim();

        string colorspace = "srgb";
        if (inner.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
        {
            int spaceEnd = inner.IndexOf(',');
            if (spaceEnd > 0)
            {
                colorspace = inner[3..spaceEnd].Trim();
                inner = inner[(spaceEnd + 1)..].Trim();
            }
        }

        var args = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 2) return null;

        var (color1, pct1) = ParseColorMixArg(args[0].Trim());
        var (color2, pct2) = ParseColorMixArg(args[1].Trim());

        float total = pct1 + pct2;
        if (total == 0) { pct1 = 0.5f; pct2 = 0.5f; }
        else { pct1 /= total; pct2 /= total; }

        byte r = (byte)Math.Clamp(MathF.Round(color1.Red * pct1 + color2.Red * pct2), 0, 255);
        byte g = (byte)Math.Clamp(MathF.Round(color1.Green * pct1 + color2.Green * pct2), 0, 255);
        byte b = (byte)Math.Clamp(MathF.Round(color1.Blue * pct1 + color2.Blue * pct2), 0, 255);
        byte a = (byte)Math.Clamp(MathF.Round(color1.Alpha * pct1 + color2.Alpha * pct2), 0, 255);
        return new SKColor(r, g, b, a);
    }

    private static (SKColor color, float pct) ParseColorMixArg(string arg)
    {
        var pctMatch = Regex.Match(arg, @"([\d.]+%)");
        if (pctMatch.Success)
        {
            float pct = float.Parse(pctMatch.Value[..^1]) / 100f;
            var colorStr = arg.Replace(pctMatch.Value, "").Trim();
            return (Parse(colorStr), pct);
        }
        return (Parse(arg), 1f);
    }

    private static SKColor? ParseColorFunction(string value)
    {
        var match = Regex.Match(value, @"color\s*\(\s*([\w-]+)\s+(.+?)\s*\)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        // Color function: color(colorspace c1 c2 c3 / a)
        // For now, parse as raw sRGB values if in srgb colorspace
        var space = match.Groups[1].Value.ToLowerInvariant();
        var channels = match.Groups[2].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var vals = channels[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (space == "srgb" && vals.Length >= 3)
        {
            float r = float.Parse(vals[0]);
            float g = float.Parse(vals[1]);
            float b = float.Parse(vals[2]);
            float alpha = 1f;
            if (channels.Length > 1 && float.TryParse(channels[1].Trim(), out var a))
                alpha = a;
            return new SKColor(
                (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
                (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
                (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
                (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
        }

        // srgb-linear, display-p3, a98-rgb, prophoto-rgb, rec2020, xyz, xyz-d50, xyz-d65
        // Fallback: treat as sRGB if possible from xyz or display-p3
        if (vals.Length >= 3)
        {
            float x = float.Parse(vals[0]);
            float y = float.Parse(vals[1]);
            float z = vals.Length > 2 ? float.Parse(vals[2]) : 0;
            float alpha = 1f;
            if (channels.Length > 1 && float.TryParse(channels[1].Trim(), out var a))
                alpha = a;

            if (space == "xyz" || space == "xyz-d65")
                return XyzToSrgb(x, y, z, alpha);
            if (space == "display-p3")
            {
                var rgb = DisplayP3ToSrgb(x, y, z);
                return new SKColor(rgb.r, rgb.g, rgb.b, (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
            }
        }

        return null;
    }

    private static float LabLinearComponent(float t)
    {
        const float delta = 6f / 29f;
        if (t > delta)
            return t * t * t;
        return 3 * delta * delta * (t - 4f / 29f);
    }

    private static SKColor XyzToSrgb(float x, float y, float z, float alpha)
    {
        float r = 3.2404542f * x - 1.5371385f * y - 0.4985314f * z;
        float g = -0.9692660f * x + 1.8760108f * y + 0.0415560f * z;
        float b = 0.0556434f * x - 0.2040259f * y + 1.0572252f * z;

        r = LinearToSrgbChannel(r);
        g = LinearToSrgbChannel(g);
        b = LinearToSrgbChannel(b);

        return new SKColor(
            (byte)Math.Clamp(MathF.Round(r * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(g * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(b * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
    }

    private static (byte r, byte g, byte b) DisplayP3ToSrgb(float r, float g, float b)
    {
        // Approximate Display P3 to sRGB
        float sr = 1.0f * r + 0.0f * g + 0.0f * b;
        float sg = 0.0f * r + 1.0f * g + 0.0f * b;
        float sb = 0.0f * r + 0.0f * g + 1.0f * b;
        return (
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sr) * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sg) * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(LinearToSrgbChannel(sb) * 255), 0, 255));
    }

    private static float LinearToSrgbChannel(float c)
    {
        if (c <= 0.0031308f)
            return 12.92f * c;
        return 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    private static SKColor? ParseCurrentColor(string value)
    {
        return value.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            ? SKColors.Black
            : null;
    }

    private static SKColor GetNamedColor(string name)
    {
        return KnownColors.Get(name) ?? SKColors.Black;
    }

    public static bool IsColorName(string name) => KnownColors.Get(name).HasValue;
}

public static class KnownColors
{
    private static readonly Dictionary<string, SKColor> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        { "black", SKColors.Black }, { "white", SKColors.White },
        { "red", SKColor.Parse("#FF0000") }, { "green", SKColor.Parse("#008000") },
        { "blue", SKColor.Parse("#0000FF") }, { "yellow", SKColors.Yellow },
        { "cyan", SKColors.Cyan }, { "magenta", SKColors.Magenta },
        { "gray", SKColors.Gray }, { "silver", SKColor.Parse("#C0C0C0") },
        { "maroon", SKColor.Parse("#800000") }, { "olive", SKColor.Parse("#808000") },
        { "lime", SKColor.Parse("#00FF00") }, { "aqua", SKColor.Parse("#00FFFF") },
        { "teal", SKColor.Parse("#008080") }, { "navy", SKColor.Parse("#000080") },
        { "fuchsia", SKColor.Parse("#FF00FF") }, { "purple", SKColors.Purple },
        { "orange", SKColor.Parse("#FFA500") }, { "pink", SKColor.Parse("#FFC0CB") },
        { "coral", SKColor.Parse("#FF7F50") }, { "salmon", SKColor.Parse("#FA8072") },
        { "gold", SKColor.Parse("#FFD700") }, { "khaki", SKColor.Parse("#F0E68C") },
        { "plum", SKColor.Parse("#DDA0DD") }, { "violet", SKColor.Parse("#EE82EE") },
        { "tan", SKColor.Parse("#D2B48C") }, { "chocolate", SKColor.Parse("#D2691E") },
        { "transparent", SKColors.Transparent },
        { "aliceblue", SKColor.Parse("#F0F8FF") }, { "antiquewhite", SKColor.Parse("#FAEBD7") },
        { "aquamarine", SKColor.Parse("#7FFFD4") }, { "azure", SKColor.Parse("#F0FFFF") },
        { "beige", SKColor.Parse("#F5F5DC") }, { "bisque", SKColor.Parse("#FFE4C4") },
        { "blanchedalmond", SKColor.Parse("#FFEBCD") }, { "blueviolet", SKColor.Parse("#8A2BE2") },
        { "brown", SKColor.Parse("#A52A2A") }, { "burlywood", SKColor.Parse("#DEB887") },
        { "cadetblue", SKColor.Parse("#5F9EA0") }, { "chartreuse", SKColor.Parse("#7FFF00") },
        { "cornflowerblue", SKColor.Parse("#6495ED") }, { "cornsilk", SKColor.Parse("#FFF8DC") },
        { "crimson", SKColor.Parse("#DC143C") }, { "darkblue", SKColor.Parse("#00008B") },
        { "darkcyan", SKColor.Parse("#008B8B") }, { "darkgoldenrod", SKColor.Parse("#B8860B") },
        { "darkgray", SKColor.Parse("#A9A9A9") }, { "darkgreen", SKColor.Parse("#006400") },
        { "darkkhaki", SKColor.Parse("#BDB76B") }, { "darkmagenta", SKColor.Parse("#8B008B") },
        { "darkolivegreen", SKColor.Parse("#556B2F") }, { "darkorange", SKColor.Parse("#FF8C00") },
        { "darkorchid", SKColor.Parse("#9932CC") }, { "darkred", SKColor.Parse("#8B0000") },
        { "darksalmon", SKColor.Parse("#E9967A") }, { "darkseagreen", SKColor.Parse("#8FBC8F") },
        { "darkslateblue", SKColor.Parse("#483D8B") }, { "darkslategray", SKColor.Parse("#2F4F4F") },
        { "darkturquoise", SKColor.Parse("#00CED1") }, { "darkviolet", SKColor.Parse("#9400D3") },
        { "deeppink", SKColor.Parse("#FF1493") }, { "deepskyblue", SKColor.Parse("#00BFFF") },
        { "dimgray", SKColor.Parse("#696969") }, { "dodgerblue", SKColor.Parse("#1E90FF") },
        { "firebrick", SKColor.Parse("#B22222") }, { "floralwhite", SKColor.Parse("#FFFAF0") },
        { "forestgreen", SKColor.Parse("#228B22") }, { "gainsboro", SKColor.Parse("#DCDCDC") },
        { "ghostwhite", SKColor.Parse("#F8F8FF") }, { "goldenrod", SKColor.Parse("#DAA520") },
        { "greenyellow", SKColor.Parse("#ADFF2F") }, { "honeydew", SKColor.Parse("#F0FFF0") },
        { "hotpink", SKColor.Parse("#FF69B4") }, { "indianred", SKColor.Parse("#CD5C5C") },
        { "indigo", SKColor.Parse("#4B0082") }, { "ivory", SKColor.Parse("#FFFFF0") },
        { "lavender", SKColor.Parse("#E6E6FA") }, { "lavenderblush", SKColor.Parse("#FFF0F5") },
        { "lawngreen", SKColor.Parse("#7CFC00") }, { "lemonchiffon", SKColor.Parse("#FFFACD") },
        { "lightblue", SKColor.Parse("#ADD8E6") }, { "lightcoral", SKColor.Parse("#F08080") },
        { "lightcyan", SKColor.Parse("#E0FFFF") }, { "lightgoldenrodyellow", SKColor.Parse("#FAFAD2") },
        { "lightgray", SKColor.Parse("#D3D3D3") }, { "lightgreen", SKColor.Parse("#90EE90") },
        { "lightpink", SKColor.Parse("#FFB6C1") }, { "lightsalmon", SKColor.Parse("#FFA07A") },
        { "lightseagreen", SKColor.Parse("#20B2AA") }, { "lightskyblue", SKColor.Parse("#87CEFA") },
        { "lightslategray", SKColor.Parse("#778899") }, { "lightsteelblue", SKColor.Parse("#B0C4DE") },
        { "lightyellow", SKColor.Parse("#FFFFE0") }, { "limegreen", SKColor.Parse("#32CD32") },
        { "linen", SKColor.Parse("#FAF0E6") }, { "mediumaquamarine", SKColor.Parse("#66CDAA") },
        { "mediumblue", SKColor.Parse("#0000CD") }, { "mediumorchid", SKColor.Parse("#BA55D3") },
        { "mediumpurple", SKColor.Parse("#9370DB") }, { "mediumseagreen", SKColor.Parse("#3CB371") },
        { "mediumslateblue", SKColor.Parse("#7B68EE") }, { "mediumspringgreen", SKColor.Parse("#00FA9A") },
        { "mediumturquoise", SKColor.Parse("#48D1CC") }, { "mediumvioletred", SKColor.Parse("#C71585") },
        { "midnightblue", SKColor.Parse("#191970") }, { "mintcream", SKColor.Parse("#F5FFFA") },
        { "mistyrose", SKColor.Parse("#FFE4E1") }, { "moccasin", SKColor.Parse("#FFE4B5") },
        { "navajowhite", SKColor.Parse("#FFDEAD") }, { "oldlace", SKColor.Parse("#FDF5E6") },
        { "olivedrab", SKColor.Parse("#6B8E23") }, { "orangered", SKColor.Parse("#FF4500") },
        { "orchid", SKColor.Parse("#DA70D6") }, { "palegoldenrod", SKColor.Parse("#EEE8AA") },
        { "palegreen", SKColor.Parse("#98FB98") }, { "paleturquoise", SKColor.Parse("#AFEEEE") },
        { "palevioletred", SKColor.Parse("#DB7093") }, { "papayawhip", SKColor.Parse("#FFEFD5") },
        { "peachpuff", SKColor.Parse("#FFDAB9") }, { "peru", SKColor.Parse("#CD853F") },
        { "powderblue", SKColor.Parse("#B0E0E6") }, { "rosybrown", SKColor.Parse("#BC8F8F") },
        { "royalblue", SKColor.Parse("#4169E1") }, { "saddlebrown", SKColor.Parse("#8B4513") },
        { "sandybrown", SKColor.Parse("#F4A460") }, { "seagreen", SKColor.Parse("#2E8B57") },
        { "seashell", SKColor.Parse("#FFF5EE") }, { "sienna", SKColor.Parse("#A0522D") },
        { "skyblue", SKColor.Parse("#87CEEB") }, { "slateblue", SKColor.Parse("#6A5ACD") },
        { "slategray", SKColor.Parse("#708090") }, { "snow", SKColor.Parse("#FFFAFA") },
        { "springgreen", SKColor.Parse("#00FF7F") }, { "steelblue", SKColor.Parse("#4682B4") },
        { "thistle", SKColor.Parse("#D8BFD8") }, { "tomato", SKColor.Parse("#FF6347") },
        { "turquoise", SKColor.Parse("#40E0D0") }, { "wheat", SKColor.Parse("#F5DEB3") },
        { "whitesmoke", SKColor.Parse("#F5F5F5") }, { "yellowgreen", SKColor.Parse("#9ACD32") },
        { "rebeccapurple", SKColor.Parse("#663399") },
        { "grey", SKColor.Parse("#808080") },
        { "darkgrey", SKColor.Parse("#A9A9A9") },
        { "darkslategrey", SKColor.Parse("#2F4F4F") },
        { "dimgrey", SKColor.Parse("#696969") },
        { "lightgrey", SKColor.Parse("#D3D3D3") },
        { "lightslategrey", SKColor.Parse("#778899") },
        { "slategrey", SKColor.Parse("#708090") }
    };

    public static SKColor? Get(string name) => Colors.GetValueOrDefault(name);
}
