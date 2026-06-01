using System.Text.RegularExpressions;
using SkiaSharp;

namespace UpBrowser.Core.Css;

public static class TransformParser
{
    private static readonly Regex TransformRegex = new(
        @"(translateX|translateY|translate|translate3d|rotateX|rotateY|rotateZ|rotate|scaleX|scaleY|scale|scale3d|skewX|skewY|skew|matrix|matrix3d|perspective)\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase);

    public static List<TransformOperation> Parse(string? transformString)
    {
        var result = new List<TransformOperation>();
        if (string.IsNullOrWhiteSpace(transformString) || transformString == "none")
            return result;

        foreach (Match match in TransformRegex.Matches(transformString))
        {
            var op = new TransformOperation
            {
                Function = match.Groups[1].Value.ToLowerInvariant(),
                Args = match.Groups[2].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .ToArray()
            };
            result.Add(op);
        }
        return result;
    }

    public static SKMatrix ToMatrix(List<TransformOperation> operations, float originX, float originY)
    {
        var matrix = SKMatrix.Identity;

        foreach (var op in operations)
        {
            var m = OperationToMatrix(op);
            var toOrigin = SKMatrix.CreateTranslation(-originX, -originY);
            var fromOrigin = SKMatrix.CreateTranslation(originX, originY);
            matrix = SKMatrix.Concat(matrix, toOrigin);
            matrix = SKMatrix.Concat(matrix, m);
            matrix = SKMatrix.Concat(matrix, fromOrigin);
        }

        return matrix;
    }

    private static SKMatrix OperationToMatrix(TransformOperation op)
    {
        float[] args = op.Args.Select(ParseFloat).ToArray();

        return op.Function switch
        {
            "translate" or "translate3d" => SKMatrix.CreateTranslation(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
            "translatex" => SKMatrix.CreateTranslation(args.ElementAtOrDefault(0), 0),
            "translatey" => SKMatrix.CreateTranslation(0, args.ElementAtOrDefault(0)),
            "rotate" => SKMatrix.CreateRotationDegrees(args.ElementAtOrDefault(0)),
            "rotatex" => CreateRotateX(args.ElementAtOrDefault(0)),
            "rotatey" => CreateRotateY(args.ElementAtOrDefault(0)),
            "rotatez" => SKMatrix.CreateRotationDegrees(args.ElementAtOrDefault(0)),
            "scale" or "scale3d" => SKMatrix.CreateScale(args.Length > 0 ? args[0] : 1f, args.Length > 1 ? args[1] : 1f),
            "scalex" => SKMatrix.CreateScale(args.Length > 0 ? args[0] : 1f, 1),
            "scaley" => SKMatrix.CreateScale(1, args.Length > 0 ? args[0] : 1f),
            "skew" => CreateSkew(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
            "skewx" => CreateSkew(args.ElementAtOrDefault(0), 0),
            "skewy" => CreateSkew(0, args.ElementAtOrDefault(0)),
            "matrix" => CreateMatrix(args),
            _ => SKMatrix.Identity
        };
    }

    private static SKMatrix CreateRotateX(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        return SKMatrix.CreateScale(1, cos);
    }

    private static SKMatrix CreateRotateY(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        return SKMatrix.CreateScale(cos, 1);
    }

    private static SKMatrix CreateSkew(float x, float y)
    {
        float tanX = MathF.Tan(x * MathF.PI / 180f);
        float tanY = MathF.Tan(y * MathF.PI / 180f);
        return new SKMatrix
        {
            ScaleX = 1, SkewX = tanX, TransX = 0,
            SkewY = tanY, ScaleY = 1, TransY = 0,
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    private static SKMatrix CreateMatrix(float[] args)
    {
        if (args.Length < 6) return SKMatrix.Identity;
        return new SKMatrix
        {
            ScaleX = args[0], SkewX = args[2], TransX = args[4],
            SkewY = args[1], ScaleY = args[3], TransY = args[5],
            Persp0 = 0, Persp1 = 0, Persp2 = 1
        };
    }

    private static float ParseFloat(string s)
    {
        s = s.Trim();
        if (s.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^3], out var deg)) return deg;
        }
        if (s.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (float.TryParse(s[..^3], out var rad)) return rad * 180f / MathF.PI;
        }
        if (float.TryParse(s, out var val)) return val;
        return 0;
    }
}

public class TransformOperation
{
    public string Function { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
}
