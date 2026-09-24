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
            bool repeating = input.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase);

            float angle = 180f;
            var parts = SplitGradientParts(inner);

            if (parts.Count > 0 && TryParseAngle(parts[0], out angle))
                parts.RemoveAt(0);

            var stops = ParseColorStops(parts);
            if (stops.Count == 0) return null;

            var (startPoint, endPoint) = CalculateLinearPoints(angle, rect);

            if (repeating)
            {
                // A repeating gradient tiles the span defined by its explicit
                // lengths: the gradient line runs from the first to the last
                // explicit px stop and repeats.
                float spanPx = stops.Where(s => s.Px >= 0).DefaultIfEmpty(new ColorStop { Px = 0 }).Max(s => s.Px);
                if (spanPx <= 0) return null;
                var dir = new SKPoint(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                float full = MathF.Max(1f, MathF.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
                var rcolors = stops.Select(s => s.Color).ToArray();
                var rpos = stops.Select(s => s.Px >= 0 ? s.Px / spanPx : (s.Position >= 0 ? s.Position : 1f)).ToArray();
                var shader = SKShader.CreateLinearGradient(
                    startPoint, endPoint, rcolors, rpos, SKShaderTileMode.Repeat);
                // Scale the unit gradient line down to the repeating span.
                var m = SKMatrix.CreateScale(spanPx / full, spanPx / full, startPoint.X, startPoint.Y);
                return shader.WithLocalMatrix(m);
            }

            var colors = stops.Select(s => s.Color).ToArray();
            float lineLen = MathF.Max(1f, Dist(startPoint, endPoint));
            var positions = stops.Select(s => s.Px >= 0 ? Math.Clamp(s.Px / lineLen, 0f, 1f) : s.Position).ToArray();

            return SKShader.CreateLinearGradient(
                new SKPoint(startPoint.X, startPoint.Y),
                new SKPoint(endPoint.X, endPoint.Y),
                colors, positions, SKShaderTileMode.Clamp);
        }
        catch { return null; }
    }

    private static float Dist(SKPoint a, SKPoint b) =>
        MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

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
            float px = -1;
            // A stop may carry two position tokens ("#000 0 10px" — a range
            // hint); the first one is the stop's own position.
            var posTokens = posPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (posTokens.Length > 0)
            {
                var tok = posTokens[0];
                if (tok.EndsWith("%"))
                {
                    if (float.TryParse(tok[..^1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pct))
                        position = pct / 100f;
                }
                else if (tok.EndsWith("px"))
                {
                    if (float.TryParse(tok[..^2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var pxv))
                        px = pxv;
                }
                else if (tok == "0")
                    px = 0;
            }

            stops.Add(new ColorStop { Color = color.Value, Position = position, Px = px });
        }

        if (stops.Count > 0)
        {
            bool Positioned(ColorStop s) => s.Position >= 0 || s.Px >= 0;

            // First: set first stop to 0% and last stop to 100% if unspecified
            if (!Positioned(stops[0])) stops[0] = new ColorStop { Color = stops[0].Color, Position = 0f, Px = -1 };
            if (!Positioned(stops[^1])) stops[^1] = new ColorStop { Color = stops[^1].Color, Position = 1f, Px = -1 };

            // Distribute remaining unpositioned stops evenly between known positions
            for (int i = 0; i < stops.Count; i++)
            {
                if (Positioned(stops[i])) continue;

                int start = i - 1;
                // find the next assigned position
                int end = stops.FindIndex(i + 1, s => Positioned(s) && (s.Position >= 0 || s.Px >= 0));
                if (end < 0) end = stops.Count - 1;
                if (start < 0) start = 0;

                float startPos = EffectiveFraction(stops[start], 1f);
                float endPos = EffectiveFraction(stops[end], 1f);
                int count = Math.Max(1, end - start);
                float step = (endPos - startPos) / count;
                for (int j = start + 1; j < end; j++)
                {
                    stops[j] = new ColorStop { Color = stops[j].Color, Position = startPos + step * (j - start), Px = -1 };
                }
                i = end; // skip ahead
            }
        }

        return stops;
    }

    // A stop's position as a 0..1 fraction; px values need the gradient length
    // and are resolved by the caller, so fall back to the fraction when known.
    private static float EffectiveFraction(ColorStop s, float fallback) =>
        s.Position >= 0 ? s.Position : fallback;

    private static int FindColorStopSplit(string s)
    {
        // The color comes first; the position (possibly several tokens) after
        // it. Find the first space outside parentheses — functional colors like
        // rgb(0, 0, 0) keep their inner spaces.
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (depth == 0 && s[i] == ' ')
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
        /// <summary>Explicit pixel position (first position token), or -1.</summary>
        public float Px;
    }
}
