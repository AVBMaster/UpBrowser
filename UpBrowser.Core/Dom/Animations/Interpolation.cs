using SkiaSharp;

namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// Interpolates CSS values between keyframes. Mirrors Blink's Interpolation approach.
/// Handles: color, opacity, length, transform, visibility, etc.
/// </summary>
public static class Interpolation
{
    public static object? Interpolate(string property, object? from, object? to, double progress)
    {
        if (from == null || to == null) return progress >= 0.5 ? to : from;

        return property switch
        {
            "opacity" => InterpolateOpacity(AsFloat(from), AsFloat(to), progress),
            "color" or "background-color" or "border-color" or "border-top-color" or
            "border-bottom-color" or "border-left-color" or "border-right-color" or
            "outline-color" or "text-decoration-color" => InterpolateColor(AsColor(from), AsColor(to), progress),
            "transform" => InterpolateTransform(from.ToString()!, to.ToString()!, progress),
            "visibility" => progress >= 0.5 ? to : from,
            "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height" or
            "margin" or "margin-left" or "margin-right" or "margin-top" or "margin-bottom" or
            "padding" or "padding-left" or "padding-right" or "padding-top" or "padding-bottom" or
            "left" or "right" or "top" or "bottom" or "font-size" or "line-height" or
            "letter-spacing" or "word-spacing" or "text-indent" or
            "border-width" or "border-top-width" or "border-right-width" or
            "border-bottom-width" or "border-left-width" or
            "border-radius" or "border-top-left-radius" or "border-top-right-radius" or
            "border-bottom-right-radius" or "border-bottom-left-radius" or
            "outline-offset" or "outline-width" or "column-gap" or "row-gap" or "gap" or
            "flex-basis" or "perspective" or "translate" or "rotate" or "scale" or "x" or "y" =>
                InterpolateLength(from.ToString()!, to.ToString()!, progress),
            "filter" or "backdrop-filter" => InterpolateFilter(from.ToString()!, to.ToString()!, progress),
            _ => progress >= 0.5 ? to : from
        };
    }

    private static float AsFloat(object? value) =>
        value is float f ? f : float.TryParse(value?.ToString(), out var r) ? r : 0;

    private static SKColor AsColor(object? value)
    {
        if (value is SKColor c) return c;
        return UpBrowser.Core.Css.ColorParser.Parse(value?.ToString() ?? "transparent");
    }

    private static object InterpolateOpacity(float from, float to, double progress) =>
        (float)(from + (to - from) * progress);

    private static object InterpolateColor(SKColor from, SKColor to, double progress)
    {
        float t = (float)progress;
        return new SKColor(
            (byte)(from.Red + (to.Red - from.Red) * t),
            (byte)(from.Green + (to.Green - from.Green) * t),
            (byte)(from.Blue + (to.Blue - from.Blue) * t),
            (byte)(from.Alpha + (to.Alpha - from.Alpha) * t));
    }

    private static object InterpolateLength(string from, string to, double progress)
    {
        float f = TryParseFloat(from);
        float t = TryParseFloat(to);
        float result = f + (t - f) * (float)progress;
        string unit = ExtractUnit(from) ?? ExtractUnit(to) ?? "px";
        return $"{result:F2}{unit}";
    }

    private static object InterpolateTransform(string from, string to, double progress)
    {
        if (from == "none" && to == "none") return "none";
        if (from == "none") from = "matrix(1,0,0,1,0,0)";
        if (to == "none") to = "matrix(1,0,0,1,0,0)";

        var fromValues = ParseTransformValues(from);
        var toValues = ParseTransformValues(to);

        int maxCount = Math.Max(fromValues.Count, toValues.Count);
        var result = new List<float>();
        for (int i = 0; i < maxCount; i++)
        {
            float f = i < fromValues.Count ? fromValues[i] : 0;
            float t = i < toValues.Count ? toValues[i] : 0;
            result.Add(f + (t - f) * (float)progress);
        }

        if (result.Count == 6)
            return $"matrix({result[0]:F4},{result[1]:F4},{result[2]:F4},{result[3]:F4},{result[4]:F4},{result[5]:F4})";
        return "none";
    }

    private static object InterpolateFilter(string from, string to, double progress)
    {
        return progress >= 0.5 ? to : from;
    }

    private static List<float> ParseTransformValues(string transform)
    {
        var values = new List<float>();
        if (transform.StartsWith("matrix("))
        {
            var inner = transform[7..^1];
            foreach (var p in inner.Split(','))
            {
                if (TryParseFloat(p.Trim(), out var v))
                    values.Add(v);
            }
        }
        return values;
    }

    private static float TryParseFloat(string s) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0;

    private static bool TryParseFloat(string s, out float result) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out result);

    private static string? ExtractUnit(string value)
    {
        value = value.Trim();
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (char.IsLetter(value[i]))
            {
                if (i > 0 && (char.IsDigit(value[i - 1]) || value[i - 1] == '.' || value[i - 1] == '-'))
                    return value[i..];
            }
        }
        return null;
    }
}