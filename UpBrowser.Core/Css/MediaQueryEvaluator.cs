namespace UpBrowser.Core.Css;

public static class MediaQueryEvaluator
{
    public static bool Evaluate(string condition, float viewportWidth, float viewportHeight, string colorScheme = "light")
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        if (condition.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            return !Evaluate(condition[4..].Trim(), viewportWidth, viewportHeight, colorScheme);

        var parts = condition.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (!EvaluateSingle(part.Trim(), viewportWidth, viewportHeight, colorScheme))
                return false;
        }
        return true;
    }

    private static bool EvaluateSingle(string condition, float viewportWidth, float viewportHeight, string colorScheme)
    {
        condition = condition.Trim();
        if (condition.StartsWith("(") && condition.EndsWith(")"))
            condition = condition[1..^1];

        if (condition.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        if (condition.Equals("screen", StringComparison.OrdinalIgnoreCase)) return true;
        if (condition.Equals("print", StringComparison.OrdinalIgnoreCase)) return false;

        if (condition.StartsWith("width", StringComparison.OrdinalIgnoreCase) && !condition.StartsWith("max-width", StringComparison.OrdinalIgnoreCase) && !condition.StartsWith("min-width", StringComparison.OrdinalIgnoreCase))
        {
            var op = GetOperator(condition);
            var value = ParseValue(condition);
            if (value <= 0) return false;
            return CompareValue(viewportWidth, op, value);
        }

        if (condition.StartsWith("height", StringComparison.OrdinalIgnoreCase) && !condition.StartsWith("max-height", StringComparison.OrdinalIgnoreCase) && !condition.StartsWith("min-height", StringComparison.OrdinalIgnoreCase))
        {
            var op = GetOperator(condition);
            var value = ParseValue(condition);
            if (value <= 0) return false;
            return CompareValue(viewportHeight, op, value);
        }

        if (condition.StartsWith("min-width", StringComparison.OrdinalIgnoreCase))
        {
            var value = ParseValue(condition);
            return viewportWidth >= value;
        }

        if (condition.StartsWith("max-width", StringComparison.OrdinalIgnoreCase))
        {
            var value = ParseValue(condition);
            return viewportWidth <= value;
        }

        if (condition.StartsWith("min-height", StringComparison.OrdinalIgnoreCase))
        {
            var value = ParseValue(condition);
            return viewportHeight >= value;
        }

        if (condition.StartsWith("max-height", StringComparison.OrdinalIgnoreCase))
        {
            var value = ParseValue(condition);
            return viewportHeight <= value;
        }

        if (condition.StartsWith("prefers-color-scheme", StringComparison.OrdinalIgnoreCase))
        {
            var val = condition.Split(':').Last().Trim().ToLowerInvariant();
            return val == colorScheme;
        }

        if (condition.StartsWith("orientation", StringComparison.OrdinalIgnoreCase))
        {
            var val = condition.Split(':').Last().Trim().ToLowerInvariant();
            if (val == "landscape") return viewportWidth > viewportHeight;
            if (val == "portrait") return viewportHeight >= viewportWidth;
        }

        if (condition.StartsWith("aspect-ratio", StringComparison.OrdinalIgnoreCase))
        {
            var parts2 = condition.Split('/').Last().Trim().Split('/');
            if (parts2.Length == 2 && float.TryParse(parts2[0], out var w) && float.TryParse(parts2[1], out var h) && h > 0)
            {
                float ratio = viewportWidth / viewportHeight;
                float targetRatio = w / h;
                return Math.Abs(ratio - targetRatio) < 0.01f;
            }
        }

        return true;
    }

    private static string GetOperator(string condition)
    {
        if (condition.Contains(">=")) return ">=";
        if (condition.Contains("<=")) return "<=";
        if (condition.Contains(">")) return ">";
        if (condition.Contains("<")) return "<";
        return "=";
    }

    private static float ParseValue(string condition)
    {
        var colonIdx = condition.IndexOf(':');
        string valuePart = colonIdx >= 0 ? condition[(colonIdx + 1)..] : condition;

        valuePart = valuePart.Replace("(", "").Replace(")", "").Trim();
        valuePart = valuePart.Replace("px", "").Replace("rem", "").Replace("em", "").Replace("vw", "").Replace("vh", "").Trim();

        if (float.TryParse(valuePart, out var val))
            return val;
        return 0;
    }

    private static bool CompareValue(float actual, string op, float target)
    {
        return op switch
        {
            ">=" => actual >= target,
            "<=" => actual <= target,
            ">" => actual > target,
            "<" => actual < target,
            _ => Math.Abs(actual - target) < 0.01f
        };
    }
}
