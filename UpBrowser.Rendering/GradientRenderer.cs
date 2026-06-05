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

            var center = new SKPoint(rect.MidX, rect.MidY);
            var sweep = SKShader.CreateSweepGradient(center, colors, positions);

            // CSS conic-gradient starts at top (12 o'clock), Skia sweep starts at right (3 o'clock).
            // Rotate by -90 degrees (270 degrees clockwise) to align.
            var rotation = SKMatrix.CreateRotationDegrees(-90, center.X, center.Y);
            return sweep.WithLocalMatrix(rotation);
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
        s = s.Trim().ToLowerInvariant();
        // CSS direction keywords
        if (s == "to top") { angle = 0; return true; }
        if (s == "to right") { angle = 90; return true; }
        if (s == "to bottom") { angle = 180; return true; }
        if (s == "to left") { angle = 270; return true; }
        if (s == "to top right" || s == "to right top") { angle = 45; return true; }
        if (s == "to top left" || s == "to left top") { angle = 315; return true; }
        if (s == "to bottom right" || s == "to right bottom") { angle = 135; return true; }
        if (s == "to bottom left" || s == "to left bottom") { angle = 225; return true; }

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
            // First: set first stop to 0% and last stop to 100% if unspecified
            if (stops[0].Position < 0) stops[0] = new ColorStop { Color = stops[0].Color, Position = 0f };
            if (stops[^1].Position < 0) stops[^1] = new ColorStop { Color = stops[^1].Color, Position = 1f };

            // Distribute remaining unpositioned stops evenly between known positions
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i].Position >= 0) continue;

                int start = i - 1;
                // find the next assigned position
                int end = stops.FindIndex(i + 1, s => s.Position >= 0);
                if (end < 0) end = stops.Count - 1;

                float startPos = stops[start].Position;
                float endPos = stops[end].Position;
                int count = end - start;
                float step = (endPos - startPos) / count;
                for (int j = start + 1; j < end; j++)
                {
                    stops[j] = new ColorStop { Color = stops[j].Color, Position = startPos + step * (j - start) };
                }
                i = end; // skip ahead
            }
        }

        return stops;
    }

    private static int FindColorStopSplit(string s)
    {
        int depth = 0;
        int lastSpace = -1;
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == ')') depth++;
            else if (s[i] == '(') depth--;
            else if (depth == 0 && s[i] == ' ')
            {
                lastSpace = i;
                // Check if the part after the space looks like a position (starts with digit, ., +, -, or ends with %)
                string after = s[(i + 1)..].TrimStart();
                if (after.Length > 0 && (char.IsDigit(after[0]) || after[0] == '.' || after[0] == '+' || after[0] == '-' || after[^1] == '%'))
                    return i;
            }
        }
        return lastSpace;
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
