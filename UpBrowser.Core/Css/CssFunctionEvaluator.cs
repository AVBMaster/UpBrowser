using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

public static class CssFunctionEvaluator
{
    private static readonly Regex CalcRegex = new(@"calc\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex VarRegex = new(@"var\s*\(--([^,)]+)(?:,\s*([^)]+))?\)", RegexOptions.IgnoreCase);
    private static readonly Regex AttrRegex = new(@"attr\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex MinMaxRegex = new(@"(min|max)\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex ClampRegex = new(@"clamp\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex FitContentRegex = new(@"fit-content\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CounterRegex = new(@"counter\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex CountersRegex = new(@"counters\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex LinearGradRegex = new(@"linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RadialGradRegex = new(@"radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex ConicGradRegex = new(@"conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingLinearGradRegex = new(@"repeating-linear-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingRadialGradRegex = new(@"repeating-radial-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);
    private static readonly Regex RepeatingConicGradRegex = new(@"repeating-conic-gradient\s*\((.+)\)", RegexOptions.IgnoreCase);

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

    public static string Evaluate(string value, Element? context = null, float parentFontSize = 16, float rootFontSize = 16, float viewportWidth = 0, float viewportHeight = 0)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var result = value;

        result = EvaluateVar(result, context);
        result = EvaluateAttr(result, context);
        result = EvaluateCalc(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateMinMax(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateClamp(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateFitContent(result, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
        result = EvaluateCounter(result, context);
        result = EvaluateCounters(result, context);

        return result;
    }

    private static string EvaluateVar(string value, Element? context)
    {
        return VarRegex.Replace(value, match =>
        {
            var name = match.Groups[1].Value.Trim();
            var fallback = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

            // Check element inline style first (style attribute)
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

            // Check registered custom properties
            var registered = GetCustomProperty(name);
            if (registered != null) return registered;

            // Check computed custom properties on element
            if (context?.ComputedStyle != null)
            {
                var cp = context.ComputedStyle.GetCustomProperty(name);
                if (cp != null) return cp;
            }

            return !string.IsNullOrEmpty(fallback) ? fallback : "";
        });
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

    private static string EvaluateCalc(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return CalcRegex.Replace(value, match =>
        {
            var expr = match.Groups[1].Value.Trim();
            try
            {
                var result = EvalArithmetic(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                return $"{result:F1}px";
            }
            catch
            {
                return "0px";
            }
        });
    }

    private static string EvaluateMinMax(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return MinMaxRegex.Replace(value, match =>
        {
            var func = match.Groups[1].Value.ToLowerInvariant();
            var args = match.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return "0px";

            float[] vals = args.Select(a =>
            {
                var trimmed = a.Trim();
                var evaluated = EvaluateCalc(trimmed, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                evaluated = EvaluateMinMax(evaluated, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
                return ResolveToPixels(evaluated, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            }).ToArray();

            float result = func == "max" ? vals.Max() : vals.Min();
            return $"{result:F1}px";
        });
    }

    private static string EvaluateClamp(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return ClampRegex.Replace(value, match =>
        {
            var args = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (args.Length < 3) return "0px";

            var min = ResolveToPixels(EvaluateCalc(args[0].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            var mid = ResolveToPixels(EvaluateCalc(args[1].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            var max = ResolveToPixels(EvaluateCalc(args[2].Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight), parentFontSize, rootFontSize, viewportWidth, viewportHeight);

            var result = Math.Clamp(mid, min, max);
            return $"{result:F1}px";
        });
    }

    private static string EvaluateFitContent(string value, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        return FitContentRegex.Replace(value, match =>
        {
            var arg = match.Groups[1].Value.Trim();
            var px = ResolveToPixels(EvaluateCalc(arg, parentFontSize, rootFontSize, viewportWidth, viewportHeight), parentFontSize, rootFontSize, viewportWidth, viewportHeight);
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

    private static float EvalArithmetic(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        expr = expr.Trim();

        // Handle parentheses first
        while (expr.Contains('('))
        {
            var parenMatch = Regex.Match(expr, @"\(([^()]+)\)");
            if (!parenMatch.Success) break;
            var subResult = EvalArithmetic(parenMatch.Groups[1].Value, parentFontSize, rootFontSize, viewportWidth, viewportHeight);
            expr = expr[..parenMatch.Index] + subResult + expr[(parenMatch.Index + parenMatch.Length)..];
        }

        // Tokenize: split by + and - (but not inside numbers)
        var tokens = Tokenize(expr, parentFontSize, rootFontSize, viewportWidth, viewportHeight);

        if (tokens.Count == 0) return 0;

        float result = tokens[0];
        for (int i = 1; i < tokens.Count; i += 2)
        {
            var op = tokens[i];
            var val = tokens[i + 1];
            if (op == '+') result += val;
            else if (op == '-') result -= val;
            else if (op == '*') result *= val;
            else if (op == '/') result = val != 0 ? result / val : 0;
        }

        return result;
    }

    private static List<float> Tokenize(string expr, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        var result = new List<float>();
        var current = "";
        char? lastOp = null;

        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '+' || c == '-' || c == '*' || c == '/')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    if (lastOp.HasValue)
                        result.Add(lastOp.Value);
                    result.Add(ResolveTerm(current.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight));
                    lastOp = c;
                    current = "";
                }
                else if (c == '-')
                {
                    current = "-";
                }
            }
            else
            {
                current += c;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            if (lastOp.HasValue)
                result.Add(lastOp.Value);
            result.Add(ResolveTerm(current.Trim(), parentFontSize, rootFontSize, viewportWidth, viewportHeight));
        }

        return result;
    }

    private static float ResolveTerm(string term, float parentFontSize, float rootFontSize, float viewportWidth, float viewportHeight)
    {
        // Try to parse as a length with unit
        if (term.EndsWith("px") && float.TryParse(term[..^2], out var px)) return px;
        if (term.EndsWith("em") && float.TryParse(term[..^2], out var em)) return em * parentFontSize;
        if (term.EndsWith("rem") && float.TryParse(term[..^2], out var rem)) return rem * rootFontSize;
        if (term.EndsWith("%") && float.TryParse(term[..^1], out var pct)) return pct / 100f * parentFontSize;
        if (term.EndsWith("vw") && float.TryParse(term[..^2], out var vw)) return vw * viewportWidth / 100f;
        if (term.EndsWith("vh") && float.TryParse(term[..^2], out var vh)) return vh * viewportHeight / 100f;
        if (term.EndsWith("vmin") && float.TryParse(term[..^4], out var vmin)) return vmin * Math.Min(viewportWidth, viewportHeight) / 100f;
        if (term.EndsWith("vmax") && float.TryParse(term[..^4], out var vmax)) return vmax * Math.Max(viewportWidth, viewportHeight) / 100f;
        if (term.EndsWith("pt") && float.TryParse(term[..^2], out var pt)) return pt * 1.33333f;
        if (term.EndsWith("pc") && float.TryParse(term[..^2], out var pc)) return pc * 16f;
        if (term.EndsWith("in") && float.TryParse(term[..^2], out var inch)) return inch * 96f;
        if (term.EndsWith("cm") && float.TryParse(term[..^2], out var cm)) return cm * 37.7953f;
        if (term.EndsWith("mm") && float.TryParse(term[..^2], out var mm)) return mm * 3.77953f;
        if (term.EndsWith("ex") && float.TryParse(term[..^2], out var ex)) return ex * parentFontSize * 0.5f;
        if (term.EndsWith("ch") && float.TryParse(term[..^2], out var ch)) return ch * parentFontSize * 0.5f;

        // Try plain number
        if (float.TryParse(term, out var num)) return num;

        return 0;
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

    public static bool IsCalc(string value) => CalcRegex.IsMatch(value);
    public static bool IsVar(string value) => VarRegex.IsMatch(value);
    public static bool IsMinMax(string value) => MinMaxRegex.IsMatch(value);
    public static bool IsClamp(string value) => ClampRegex.IsMatch(value);
    public static bool IsFitContent(string value) => FitContentRegex.IsMatch(value);
}
