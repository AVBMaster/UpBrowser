using SkiaSharp;
using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

/// <summary>
/// Cross-platform color parser supporting all CSS color formats.
/// </summary>
public static class ColorParser
{
    private static readonly Regex RgbRegex = new(@"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RgbaRegex = new(@"rgba\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HslRegex = new(@"hsl\s*\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HslaRegex = new(@"hsla\s*\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
        var rgbaMatch = RgbaRegex.Match(value);
        if (rgbaMatch.Success)
        {
            return new SKColor(
                byte.Parse(rgbaMatch.Groups[1].Value),
                byte.Parse(rgbaMatch.Groups[2].Value),
                byte.Parse(rgbaMatch.Groups[3].Value),
                (byte)(float.Parse(rgbaMatch.Groups[4].Value) * 255));
        }

        var rgbMatch = RgbRegex.Match(value);
        if (rgbMatch.Success)
        {
            return new SKColor(
                byte.Parse(rgbMatch.Groups[1].Value),
                byte.Parse(rgbMatch.Groups[2].Value),
                byte.Parse(rgbMatch.Groups[3].Value));
        }

        return null;
    }

    private static SKColor? ParseHsl(string value)
    {
        var hslaMatch = HslaRegex.Match(value);
        if (hslaMatch.Success)
        {
            float h = float.Parse(hslaMatch.Groups[1].Value);
            float s = float.Parse(hslaMatch.Groups[2].Value) / 100f;
            float l = float.Parse(hslaMatch.Groups[3].Value) / 100f;
            float a = float.Parse(hslaMatch.Groups[4].Value);
            return HslToRgb(h, s, l, a);
        }

        var hslMatch = HslRegex.Match(value);
        if (hslMatch.Success)
        {
            float h = float.Parse(hslMatch.Groups[1].Value);
            float s = float.Parse(hslMatch.Groups[2].Value) / 100f;
            float l = float.Parse(hslMatch.Groups[3].Value) / 100f;
            return HslToRgb(h, s, l, 1.0f);
        }

        return null;
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
        { "rebeccapurple", SKColor.Parse("#663399") }
    };

    public static SKColor? Get(string name) => Colors.GetValueOrDefault(name);
}
