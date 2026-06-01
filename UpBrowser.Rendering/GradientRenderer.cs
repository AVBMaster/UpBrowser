using SkiaSharp;
using UpBrowser.Core.Css;

namespace UpBrowser.Rendering;

public static class GradientRenderer
{
    public static SKShader? CreateGradient(string gradientString, SKRect rect)
    {
        if (string.IsNullOrEmpty(gradientString)) return null;

        if (gradientString.Contains("linear-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateLinearGradient(gradientString, rect);
        if (gradientString.Contains("radial-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateRadialGradient(gradientString, rect);
        if (gradientString.Contains("conic-gradient", StringComparison.OrdinalIgnoreCase))
            return CreateConicGradient(gradientString, rect);

        return null;
    }

    private static SKShader? CreateLinearGradient(string input, SKRect rect)
    {
        try
        {
            var inner = ExtractGradientContent(input, "linear-gradient");
            if (inner == null) return null;

            float angle = 180f;
            var parts = SplitGradientParts(inner);

            if (parts.Count > 0 && TryParseAngle(parts[0], out angle))
                parts.RemoveAt(0);

            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            var (startPoint, endPoint) = CalculateLinearPoints(angle, rect);

            var colors = stops.Select(s => s.Color).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();

            return SKShader.CreateLinearGradient(
                new SKPoint(startPoint.X, startPoint.Y),
                new SKPoint(endPoint.X, endPoint.Y),
                colors, positions, SKShaderTileMode.Clamp);
        }
        catch { return null; }
    }

    private static SKShader? CreateRadialGradient(string input, SKRect rect)
    {
        try
        {
            var inner = ExtractGradientContent(input, "radial-gradient");
            if (inner == null) return null;

            var parts = SplitGradientParts(inner);
            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            float cx = rect.MidX, cy = rect.MidY;
            float radius = Math.Max(rect.Width, rect.Height) / 2f;

            var colors = stops.Select(s => s.Color).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();

            return SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), radius,
                colors, positions, SKShaderTileMode.Clamp);
        }
        catch { return null; }
    }

    private static SKShader? CreateConicGradient(string input, SKRect rect)
    {
        try
        {
            var inner = ExtractGradientContent(input, "conic-gradient");
            if (inner == null) return null;

            var parts = SplitGradientParts(inner);
            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            var colors = stops.Select(s => s.Color).ToArray();
            var positions = stops.Select(s => s.Position).ToArray();

            return SKShader.CreateSweepGradient(
                new SKPoint(rect.MidX, rect.MidY),
                colors, positions);
        }
        catch { return null; }
    }

    private static string? ExtractGradientContent(string input, string gradientType)
    {
        int start = input.IndexOf(gradientType, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start = input.IndexOf('(', start);
        if (start < 0) return null;
        int end = FindMatchingParen(input, start);
        if (end < 0) return null;
        return input[(start + 1)..end];
    }

    private static int FindMatchingParen(string s, int openIndex)
    {
        int depth = 1;
        for (int i = openIndex + 1; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static List<string> SplitGradientParts(string inner)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '(') depth++;
            else if (inner[i] == ')') depth--;
            else if (inner[i] == ',' && depth == 0)
            {
                parts.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < inner.Length)
            parts.Add(inner[start..].Trim());
        return parts;
    }

    private static bool TryParseAngle(string s, out float angle)
    {
        s = s.Trim();
        if (s.EndsWith("deg"))
        {
            if (float.TryParse(s[..^3], out angle)) return true;
        }
        if (s.EndsWith("rad"))
        {
            if (float.TryParse(s[..^3], out var rad)) { angle = rad * 180f / MathF.PI; return true; }
        }
        if (float.TryParse(s, out angle)) return true;
        angle = 0;
        return false;
    }

    private static (SKPoint start, SKPoint end) CalculateLinearPoints(float angle, SKRect rect)
    {
        float rad = (angle - 90) * MathF.PI / 180f;
        float cx = rect.MidX, cy = rect.MidY;
        float halfW = rect.Width / 2f, halfH = rect.Height / 2f;
        float length = MathF.Sqrt(halfW * halfW + halfH * halfH);

        float dx = MathF.Cos(rad) * length;
        float dy = MathF.Sin(rad) * length;

        return (new SKPoint(cx - dx, cy - dy), new SKPoint(cx + dx, cy + dy));
    }

    private static List<ColorStop> ParseColorStops(List<string> parts)
    {
        var stops = new List<ColorStop>();
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            var spaceIdx = FindColorStopSplit(p);
            string colorPart = spaceIdx > 0 ? p[..spaceIdx].Trim() : p;
            string posPart = spaceIdx > 0 ? p[spaceIdx..].Trim() : "";

            var color = ParseColor(colorPart);
            if (!color.HasValue) continue;

            float position = -1;
            if (posPart.EndsWith("%"))
            {
                if (float.TryParse(posPart[..^1], out var pct))
                    position = pct / 100f;
            }

            stops.Add(new ColorStop { Color = color.Value, Position = position });
        }

        if (stops.Count > 0)
        {
            float lastPos = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i].Position < 0)
                {
                    int nextAssigned = stops.FindIndex(i + 1, s => s.Position >= 0);
                    if (nextAssigned < 0) nextAssigned = stops.Count - 1;
                    float range = (nextAssigned == stops.Count - 1 ? 1f : stops[nextAssigned].Position) - lastPos;
                    float step = range / (nextAssigned - i + 1);
                    for (int j = i; j <= nextAssigned && j < stops.Count; j++)
                    {
                        var stop = stops[j];
                        stop.Position = lastPos + step * (j - i + 1);
                        stops[j] = stop;
                    }
                }
                lastPos = stops[i].Position;
            }
            if (stops.Count > 0 && stops[^1].Position < 0)
            {
                var last = stops[^1];
                last.Position = 1f;
                stops[^1] = last;
            }
        }

        return stops;
    }

    private static int FindColorStopSplit(string s)
    {
        int depth = 0;
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == ')') depth++;
            else if (s[i] == '(') depth--;
            else if (depth == 0 && s[i] == ' ' && i > 0 && char.IsDigit(s[i - 1]))
                return i;
        }
        return -1;
    }

    private static SKColor? ParseColor(string s)
    {
        s = s.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        if (s.StartsWith('#'))
        {
            if (SKColor.TryParse(s, out var c)) return c;
        }

        var namedColor = ColorParser.Parse(s);
        if (namedColor.Alpha != 0 || s == "transparent")
            return namedColor;

        if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parts = s.Replace("rgb", "").Replace("a", "").Replace("(", "").Replace(")", "").Split(',');
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

    private struct ColorStop
    {
        public SKColor Color;
        public float Position;
    }
}
