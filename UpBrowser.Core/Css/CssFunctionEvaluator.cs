using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

public static class CssFunctionEvaluator
{
    private static readonly Regex CalcFuncRegex = new(@"calc\s*\(", RegexOptions.IgnoreCase);
    private static readonly Regex VarRegex = new(@"var\s*\(--([^,)]+)(?:,\s*([^)]+))?\)", RegexOptions.IgnoreCase);
    private static readonly Regex AttrRegex = new(@"attr\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex FitContentRegex = new(@"fit-content\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CounterRegex = new(@"counter\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CountersRegex = new(@"counters\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex LinearGradRegex = new(@"linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RadialGradRegex = new(@"radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex ConicGradRegex = new(@"conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingLinearGradRegex = new(@"repeating-linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingRadialGradRegex = new(@"repeating-radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingConicGradRegex = new(@"repeating-conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"^[+-]?\d+(\.\d+)?");
    private static readonly Regex UnitRegex = new(@"^[a-z%]+", RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    public static void SetCustomProperty(string name, string value)
    {
        _customProperties[name] = value;
    }

    public static string? GetCustomProperty(string name)
    {
        return _customProperties.GetValueOrDefault(name);
    }

    public static void ClearCustomProperties()
    {
        _customProperties.Clear();
    }

    public static string Evaluate(string value, Element? context = null, float parentFontSize = 16, float rootFontSize = 16, float viewportWidth = 0, float viewportHeight = 0, int maxRecursion = 10)
    {
        if (string.IsNullOrEmpty(value) || maxRecursion <= 0) return value;

        var result = value;

        result = EvaluateVar(result, context, maxRecursion);
        result = EvaluateAttr(result, context);
        result = EvaluateCalcAndMath(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateFitContent(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateCounter(result, context);
        result = EvaluateCounters(result, context);

        return result;
    }

    private static string EvaluateVar(string value, Element? context, int maxRecursion = 10)
    {
        return VarRegex.Replace(value, match =>
        {
            var name = match.Groups[1].Value.Trim();
            var fallback = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

            var resolved = ResolveVar(name, context);

            if (resolved == null)
                resolved = !string.IsNullOrEmpty(fallback) ? fallback : "";

            if (resolved != null && (VarRegex.IsMatch(resolved) || CalcFuncRegex.IsMatch(resolved)))
                resolved = Evaluate(resolved, context, 16, 16, 1920, 1080, maxRecursion - 1);

            return resolved ?? "";
        });
    }

    private static string? ResolveVar(string name, Element? context)
    {
        if (context != null)
        {
            var inlineVal = context.GetAttribute("style");
            if (inlineVal != null)
            {
                var props = inlineVal.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var prop in props)
                {
                    var colon = prop.IndexOf(':');
                    if (colon > 0)
                    {
                        var pname = prop[..colon].Trim();
                        var pval = prop[(colon + 1)..].Trim();
                        if (pname.Equals("--" + name, StringComparison.OrdinalIgnoreCase))
                            return pval;
                    }
                }
            }
        }

        var registered = GetCustomProperty(name);
        if (registered != null) return registered;

        if (context?.ComputedStyle != null)
        {
            var cp = context.ComputedStyle.GetCustomProperty(name);
            if (cp != null) return cp;
        }

        return null;
    }

    private static string EvaluateAttr(string value, Element? context)
    {
        return AttrRegex.Replace(value, match =>
        {
            var attrName = match.Groups[1].Value.Trim().Trim('"', '\'');
            if (context == null) return "";
            return context.GetAttribute(attrName) ?? "";
        });
    }

    private static string EvaluateCalcAndMath(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        int maxIter = 20;
        while (maxIter-- > 0)
        {
            var match = FindOuterMathFunction(value);
            if (match == null) break;

            var (funcName, innerExpr, _, _) = match.Value;
            float result;
            try
            {
                result = EvalMathExpression(innerExpr, funcName, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            }
            catch
            {
                result = 0;
            }
            value = value[..match.Value.StartIndex] + $"{result:F1}px" + value[(match.Value.StartIndex + match.Value.Length)..];
        }
        return value;
    }

    private static (string funcName, string innerExpr, int StartIndex, int Length)? FindOuterMathFunction(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            string? func = null;
            if (i + 4 < value.Length && value[i..(i + 5)].Equals("calc(", StringComparison.OrdinalIgnoreCase))
                func = "calc";
            else if (i + 3 < value.Length && value[i..(i + 4)].Equals("min(", StringComparison.OrdinalIgnoreCase))
                func = "min";
            else if (i + 3 < value.Length && value[i..(i + 4)].Equals("max(", StringComparison.OrdinalIgnoreCase))
                func = "max";
            else if (i + 5 < value.Length && value[i..(i + 6)].Equals("clamp(", StringComparison.OrdinalIgnoreCase))
                func = "clamp";

            if (func != null)
            {
                int parenStart = i + func.Length;
                int depth = 1;
                int j = parenStart;
                while (j < value.Length && depth > 0)
                {
                    if (value[j] == '(') depth++;
                    else if (value[j] == ')') depth--;
                    if (depth > 0) j++;
                }
                if (depth == 0)
                {
                    var inner = value[(parenStart + 1)..j];
                    return (func, inner, i, j - i + 1);
                }
                i = j + 1;
            }
            else
            {
                i++;
            }
        }
        return null;
    }

    private static float EvalMathExpression(string expr, string funcName, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        expr = expr.Trim();

        if (funcName == "min")
        {
            var parts = SplitArgs(expr);
            if (parts.Count == 0) return 0;
            float minVal = float.MaxValue;
            foreach (var part in parts)
            {
                float val = EvalArithmetic(part.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                if (val < minVal) minVal = val;
            }
            return minVal;
        }

        if (funcName == "max")
        {
            var parts = SplitArgs(expr);
            if (parts.Count == 0) return 0;
            float maxVal = float.MinValue;
            foreach (var part in parts)
            {
                float val = EvalArithmetic(part.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                if (val > maxVal) maxVal = val;
            }
            return maxVal;
        }

        if (funcName == "clamp")
        {
            var parts = SplitArgs(expr);
            if (parts.Count < 3) return 0;
            float min = EvalArithmetic(parts[0].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float mid = EvalArithmetic(parts[1].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            float max = EvalArithmetic(parts[2].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            return Math.Clamp(mid, min, max);
        }

        return EvalArithmetic(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
    }

    private static List<string> SplitArgs(string args)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                result.Add(args[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < args.Length)
            result.Add(args[start..].Trim());
        return result;
    }

    private static float EvalArithmetic(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        expr = expr.Trim();

        // Resolve any nested function calls first
        expr = ResolveNestedFunctions(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);

        // Tokenize into numbers/units and operators
        var tokens = TokenizeWithPrecedence(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        if (tokens.Count == 0) return 0;

        // First pass: evaluate * and /
        var pass1 = new List<float>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is OpToken op && (op.Value == '*' || op.Value == '/'))
            {
                float left = pass1[^1];
                pass1.RemoveAt(pass1.Count - 1);
                i++;
                float right = ((NumToken)tokens[i]).Value;
                float result = op.Value == '*' ? left * right : (right != 0 ? left / right : 0);
                pass1.Add(result);
            }
            else if (tokens[i] is NumToken n)
            {
                pass1.Add(n.Value);
            }
        }

        // Second pass: evaluate + and -
        float result2 = pass1[0];
        int opIdx = 1;
        foreach (var token in tokens)
        {
            if (token is OpToken op)
            {
                if (opIdx < pass1.Count)
                {
                    if (op.Value == '+') result2 += pass1[opIdx];
                    else if (op.Value == '-') result2 -= pass1[opIdx];
                    opIdx++;
                }
            }
        }

        return result2;
    }

    private static string ResolveNestedFunctions(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        int maxIter = 10;
        while (maxIter-- > 0)
        {
            var match = FindOuterMathFunction(expr);
            if (match == null) break;
            var (funcName, innerExpr, start, len) = match.Value;
            float val = EvalMathExpression(innerExpr, funcName, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            expr = expr[..start] + $"{val:F1}px" + expr[(start + len)..];
        }
        return expr;
    }

    private abstract class Token { }
    private class NumToken : Token { public float Value; }
    private class OpToken : Token { public char Value; }

    private static List<Token> TokenizeWithPrecedence(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i]))
            {
                i++;
                continue;
            }

            if (expr[i] == '+' || expr[i] == '-' || expr[i] == '*' || expr[i] == '/')
            {
                tokens.Add(new OpToken { Value = expr[i] });
                i++;
                continue;
            }

            if (char.IsDigit(expr[i]) || expr[i] == '.')
            {
                var numMatch = NumberRegex.Match(expr[i..]);
                if (numMatch.Success)
                {
                    float num = float.Parse(numMatch.Value);
                    int consumed = numMatch.Length;
                    i += consumed;

                    var unitMatch = UnitRegex.Match(i < expr.Length ? expr[i..] : "");
                    if (unitMatch.Success)
                    {
                        string unit = unitMatch.Value.ToLowerInvariant();
                        i += unitMatch.Length;
                        num = ConvertUnit(num, unit, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                    }

                    tokens.Add(new NumToken { Value = num });
                    continue;
                }
            }

            if (expr[i] == '-' && i + 1 < expr.Length && (char.IsDigit(expr[i + 1]) || expr[i + 1] == '.'))
            {
                int start = i;
                i++;
                var numMatch = NumberRegex.Match(expr[i..]);
                if (numMatch.Success)
                {
                    float num = -float.Parse(numMatch.Value);
                    i += numMatch.Length;

                    var unitMatch = UnitRegex.Match(i < expr.Length ? expr[i..] : "");
                    if (unitMatch.Success)
                    {
                        string unit = unitMatch.Value.ToLowerInvariant();
                        i += unitMatch.Length;
                        num = ConvertUnit(num, unit, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                    }

                    tokens.Add(new NumToken { Value = num });
                    continue;
                }
            }

            i++;
        }

        return tokens;
    }

    private static float ConvertUnit(float value, string unit, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return unit switch
        {
            "px" => value,
            "em" => value * parentFontSize,
            "rem" => value * rootFontSize,
            "%" => value / 100f * parentFontSize,
            "vw" => value * viewportWidth / 100f,
            "vh" => value * viewportHeight / 100f,
            "vmin" => value * Math.Min(viewportWidth, viewportHeight) / 100f,
            "vmax" => value * Math.Max(viewportWidth, viewportHeight) / 100f,
            "pt" => value * 1.33333f,
            "pc" => value * 16f,
            "in" => value * 96f,
            "cm" => value * 37.7953f,
            "mm" => value * 3.77953f,
            "ex" => value * parentFontSize * 0.5f,
            "ch" => value * parentFontSize * 0.5f,
            _ => value
        };
    }

    private static string EvaluateFitContent(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return FitContentRegex.Replace(value, match =>
        {
            var arg = match.Groups[1].Value.Trim();
            var evaluated = Evaluate(arg, null, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            var px = ResolveToPixels(evaluated, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            return $"{px:F1}px";
        });
    }

    private static string EvaluateCounter(string value, Element? context)
    {
        return CounterRegex.Replace(value, match => "0");
    }

    private static string EvaluateCounters(string value, Element? context)
    {
        return CountersRegex.Replace(value, match => "");
    }

    private static float ResolveToPixels(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        value = value.Trim();

        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * parentFontSize;
        if (value.EndsWith("rem") && float.TryParse(value[..^2], out var rem)) return rem * rootFontSize;
        if (value.EndsWith("%") && float.TryParse(value[..^1], out var pct)) return pct / 100f * parentFontSize;
        if (value.EndsWith("vw") && float.TryParse(value[..^2], out var vw)) return vw * viewportWidth / 100f;
        if (value.EndsWith("vh") && float.TryParse(value[..^2], out var vh)) return vh * viewportHeight / 100f;

        if (float.TryParse(value, out var num)) return num;
        return 0;
    }

    public static string? ParseGradient(string value)
    {
        if (LinearGradRegex.IsMatch(value)) return value;
        if (RadialGradRegex.IsMatch(value)) return value;
        if (ConicGradRegex.IsMatch(value)) return value;
        if (RepeatingLinearGradRegex.IsMatch(value)) return value;
        if (RepeatingRadialGradRegex.IsMatch(value)) return value;
        if (RepeatingConicGradRegex.IsMatch(value)) return value;
        return null;
    }

    public static bool IsGradient(string value) => ParseGradient(value) != null;

    public static string? ParseTransform(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "none") return null;
        return value;
    }

    public static bool IsCalc(string value) => CalcFuncRegex.IsMatch(value);
    public static bool IsVar(string value) => VarRegex.IsMatch(value);
    public static bool IsMinMax(string value) => FindOuterMathFunction(value) != null;
    public static bool IsClamp(string value) => FindOuterMathFunction(value)?.funcName == "clamp";
    public static bool IsFitContent(string value) => FitContentRegex.IsMatch(value);
}
