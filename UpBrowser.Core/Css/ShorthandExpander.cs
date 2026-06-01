using System;
using System.Collections.Generic;

namespace UpBrowser.Core.Css;

public static class ShorthandExpander
{
    public static Dictionary<string, string> Expand(Dictionary<string, string> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            var expanded = ExpandProperty(prop.Key, prop.Value);
            foreach (var kv in expanded)
                result[kv.Key] = kv.Value;
        }

        return result;
    }

    public static Dictionary<string, string> ExpandProperty(string name, string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (name.ToLowerInvariant())
        {
            case "margin": ExpandFourSides(result, "margin", value); break;
            case "padding": ExpandFourSides(result, "padding", value); break;
            case "border": ExpandBorder(result, value); break;
            case "border-width": ExpandFourSides(result, "border", value, "width"); break;
            case "border-color": ExpandFourSides(result, "border", value, "color"); break;
            case "border-style": ExpandFourSides(result, "border", value, "style"); break;
            case "border-radius": ExpandFourSides(result, "border", value, "radius"); break;
            case "border-top": ExpandBorderSide(result, "border-top", value); break;
            case "border-right": ExpandBorderSide(result, "border-right", value); break;
            case "border-bottom": ExpandBorderSide(result, "border-bottom", value); break;
            case "border-left": ExpandBorderSide(result, "border-left", value); break;
            case "background": ExpandBackground(result, value); break;
            case "font": ExpandFont(result, value); break;
            case "flex": ExpandFlex(result, value); break;
            case "flex-flow": ExpandFlexFlow(result, value); break;
            case "grid-area": ExpandGridArea(result, value); break;
            case "grid-column": ExpandGridLine(result, "grid-column", value); break;
            case "grid-row": ExpandGridLine(result, "grid-row", value); break;
            case "animation": ExpandAnimation(result, value); break;
            case "transition": ExpandTransition(result, value); break;
            case "outline": ExpandOutline(result, value); break;
            case "text-decoration": ExpandTextDecoration(result, value); break;
            case "text-emphasis": ExpandTextEmphasis(result, value); break;
            case "gap": ExpandGap(result, value); break;
            case "inset": ExpandFourSides(result, "", value); break;
            case "overflow": ExpandOverflow(result, value); break;
            case "mask": result["mask-image"] = value; break;
            default: result[name] = value; break;
        }

        return result;
    }

    private static void ExpandFourSides(Dictionary<string, string> result, string prefix, string value, string? suffix = null)
    {
        var parts = SplitShorthand(value);
        string top, right, bottom, left;

        switch (parts.Count)
        {
            case 1: top = right = bottom = left = parts[0]; break;
            case 2: top = bottom = parts[0]; right = left = parts[1]; break;
            case 3: top = parts[0]; right = left = parts[1]; bottom = parts[2]; break;
            default: top = parts[0]; right = parts[1]; bottom = parts[2]; left = parts[3]; break;
        }

        string suffixStr = string.IsNullOrEmpty(suffix) ? "" : $"-{suffix}";
        if (!string.IsNullOrEmpty(prefix))
        {
            result[$"{prefix}-top{suffixStr}"] = top;
            result[$"{prefix}-right{suffixStr}"] = right;
            result[$"{prefix}-bottom{suffixStr}"] = bottom;
            result[$"{prefix}-left{suffixStr}"] = left;
        }
        else
        {
            result["top"] = top;
            result["right"] = right;
            result["bottom"] = bottom;
            result["left"] = left;
        }
    }

    private static void ExpandBorder(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (IsBorderStyle(p))
            {
                result["border-top-style"] = p;
                result["border-right-style"] = p;
                result["border-bottom-style"] = p;
                result["border-left-style"] = p;
            }
            else if (IsBorderWidth(p))
            {
                result["border-top-width"] = p;
                result["border-right-width"] = p;
                result["border-bottom-width"] = p;
                result["border-left-width"] = p;
            }
            else if (p == "none" || p == "hidden")
            {
                result["border-top-style"] = p;
                result["border-right-style"] = p;
                result["border-bottom-style"] = p;
                result["border-left-style"] = p;
            }
            else if (IsColor(p))
            {
                result["border-top-color"] = p;
                result["border-right-color"] = p;
                result["border-bottom-color"] = p;
                result["border-left-color"] = p;
            }
        }

        result.TryAdd("border-top-style", "none");
        result.TryAdd("border-right-style", "none");
        result.TryAdd("border-bottom-style", "none");
        result.TryAdd("border-left-style", "none");
    }

    private static void ExpandBorderSide(Dictionary<string, string> result, string side, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (IsBorderStyle(p))
                result[$"{side}-style"] = p;
            else if (IsBorderWidth(p))
                result[$"{side}-width"] = p;
            else if (IsColor(p))
                result[$"{side}-color"] = p;
        }
    }

    private static void ExpandBackground(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (string.IsNullOrEmpty(p)) continue;

            if (p.StartsWith("url(") || p.StartsWith("linear-gradient") || p.StartsWith("radial-gradient"))
                result["background-image"] = p;
            else if (p.StartsWith("#") || p.StartsWith("rgb") || IsNamedColor(p))
                result["background-color"] = p;
            else if (p == "repeat" || p == "no-repeat" || p == "repeat-x" || p == "repeat-y")
                result["background-repeat"] = p;
            else if (p == "scroll" || p == "fixed" || p == "local")
                result["background-attachment"] = p;
            else if (p == "cover" || p == "contain")
                result["background-size"] = p;
            else if (p.Contains('/'))
            {
                var posSize = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (posSize.Length >= 1) result["background-position"] = posSize[0].Trim();
                if (posSize.Length >= 2) result["background-size"] = posSize[1].Trim();
            }
            else if (p.Contains('%') || p == "center" || p == "left" || p == "right" || p == "top" || p == "bottom")
                result["background-position"] = p;
        }

        result.TryAdd("background-repeat", "repeat");
        result.TryAdd("background-attachment", "scroll");
        result.TryAdd("background-position", "0% 0%");
        result.TryAdd("background-size", "auto");
    }

    private static void ExpandFont(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        string fontSize = "16px", lineHeight = "normal", fontFamily = "serif";
        bool foundSize = false;

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "normal" || p == "italic" || p == "oblique")
                result["font-style"] = p;
            else if (p == "bold" || p == "bolder" || p == "lighter" || int.TryParse(p, out _))
                result["font-weight"] = p;
            else if (p == "small-caps")
                result["font-variant"] = p;
            else if (p.Contains('/') || p.Contains(' '))
            {
                var sizeLine = p.Split('/');
                fontSize = sizeLine[0].Trim();
                if (sizeLine.Length > 1) lineHeight = sizeLine[1].Trim();
                foundSize = true;
            }
            else if (IsFontSize(p) || (!foundSize && (p.EndsWith("px") || p.EndsWith("em") || p.EndsWith("rem") || p.EndsWith("%"))))
            {
                fontSize = p;
                foundSize = true;
            }
            else if (p.StartsWith('"') || p.StartsWith('\'') || p.Contains(' '))
                fontFamily = part.Trim();
            else if (!foundSize)
                fontFamily = part.Trim();
        }

        result["font-size"] = fontSize;
        result["line-height"] = lineHeight;
        result["font-family"] = fontFamily;
        result.TryAdd("font-style", "normal");
        result.TryAdd("font-weight", "normal");
        result.TryAdd("font-variant", "normal");
    }

    private static void ExpandFlex(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        string grow = "0", shrink = "1", basis = "auto";

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "none") { grow = "0"; shrink = "0"; basis = "auto"; }
            else if (p == "auto") { basis = "auto"; }
            else if (p == "initial") { grow = "0"; shrink = "1"; basis = "auto"; }
            else if (p.Contains("px") || p.Contains("em") || p.Contains("%") || p == "0")
                basis = p;
            else if (float.TryParse(p, out var num))
            {
                if (grow == "0" && shrink == "1") grow = p;
                else shrink = p;
            }
        }

        result["flex-grow"] = grow;
        result["flex-shrink"] = shrink;
        result["flex-basis"] = basis;
    }

    private static void ExpandFlexFlow(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "row" or "row-reverse" or "column" or "column-reverse")
                result["flex-direction"] = p;
            else if (p is "nowrap" or "wrap" or "wrap-reverse")
                result["flex-wrap"] = p;
        }
    }

    private static void ExpandGridArea(Dictionary<string, string> result, string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) result["grid-row"] = parts[0].Trim();
        if (parts.Length >= 2) result["grid-column"] = parts[1].Trim();
    }

    private static void ExpandGridLine(Dictionary<string, string> result, string prop, string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1) result[$"{prop}-start"] = parts[0].Trim();
        if (parts.Length >= 2) result[$"{prop}-end"] = parts[1].Trim();
    }

    private static void ExpandAnimation(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        result["animation-name"] = "none";
        result["animation-duration"] = "0s";
        result["animation-timing-function"] = "ease";
        result["animation-delay"] = "0s";
        result["animation-iteration-count"] = "1";
        result["animation-direction"] = "normal";
        result["animation-fill-mode"] = "none";
        result["animation-play-state"] = "running";

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p.EndsWith("s") || p.EndsWith("ms"))
            {
                if (result["animation-duration"] == "0s" && result["animation-delay"] == "0s")
                    result["animation-duration"] = p;
                else
                    result["animation-delay"] = p;
            }
            else if (p is "ease" or "linear" or "ease-in" or "ease-out" or "ease-in-out" or "step-start" or "step-end" || p.StartsWith("cubic-bezier") || p.StartsWith("steps"))
                result["animation-timing-function"] = p;
            else if (p is "infinite" || float.TryParse(p, out _))
                result["animation-iteration-count"] = p;
            else if (p is "normal" or "reverse" or "alternate" or "alternate-reverse")
                result["animation-direction"] = p;
            else if (p is "none" or "forwards" or "backwards" or "both")
                result["animation-fill-mode"] = p;
            else if (p is "running" or "paused")
                result["animation-play-state"] = p;
            else
                result["animation-name"] = p;
        }
    }

    private static void ExpandTransition(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        result["transition-property"] = "all";
        result["transition-duration"] = "0s";
        result["transition-timing-function"] = "ease";
        result["transition-delay"] = "0s";

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p.EndsWith("s") || p.EndsWith("ms"))
            {
                if (result["transition-duration"] == "0s" && result["transition-delay"] == "0s")
                    result["transition-duration"] = p;
                else
                    result["transition-delay"] = p;
            }
            else if (p is "ease" or "linear" or "ease-in" or "ease-out" or "ease-in-out" || p.StartsWith("cubic-bezier") || p.StartsWith("steps"))
                result["transition-timing-function"] = p;
            else
                result["transition-property"] = p;
        }
    }

    private static void ExpandOutline(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "none" || p == "hidden")
                result["outline-style"] = p;
            else if (IsBorderStyle(p))
                result["outline-style"] = p;
            else if (p.EndsWith("px") || p.EndsWith("em"))
                result["outline-width"] = p;
            else if (IsColor(p))
                result["outline-color"] = p;
        }
        result.TryAdd("outline-style", "none");
    }

    private static void ExpandTextDecoration(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        result["text-decoration-line"] = "none";
        result["text-decoration-style"] = "solid";
        result["text-decoration-color"] = "currentcolor";

        foreach (var part in parts)
        {
            var p = part.Trim().ToLowerInvariant();
            if (p is "underline" or "overline" or "line-through")
                result["text-decoration-line"] = p;
            else if (p is "solid" or "double" or "dotted" or "dashed" or "wavy")
                result["text-decoration-style"] = p;
            else if (IsColor(p))
                result["text-decoration-color"] = p;
        }
    }

    private static void ExpandTextEmphasis(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        result["text-emphasis-style"] = "none";
        result["text-emphasis-color"] = "currentcolor";

        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p == "none" || p == "filled" || p == "open" || p == "dot" || p == "circle")
                result["text-emphasis-style"] = p;
            else if (IsColor(p))
                result["text-emphasis-color"] = p;
        }
    }

    private static void ExpandGap(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count >= 2)
        {
            result["row-gap"] = parts[0].Trim();
            result["column-gap"] = parts[1].Trim();
        }
        else if (parts.Count == 1)
        {
            result["row-gap"] = parts[0].Trim();
            result["column-gap"] = parts[0].Trim();
        }
    }

    private static void ExpandOverflow(Dictionary<string, string> result, string value)
    {
        var parts = SplitShorthand(value);
        if (parts.Count >= 2)
        {
            result["overflow-x"] = parts[0].Trim();
            result["overflow-y"] = parts[1].Trim();
        }
        else
        {
            result["overflow-x"] = parts[0].Trim();
            result["overflow-y"] = parts[0].Trim();
        }
    }

    private static List<string> SplitShorthand(string value)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ' ' && depth == 0)
            {
                if (i > start) parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        if (start < value.Length) parts.Add(value[start..]);
        return parts;
    }

    private static bool IsBorderStyle(string p) => p is "none" or "hidden" or "dotted" or "dashed" or "solid" or "double" or "groove" or "ridge" or "inset" or "outset";
    private static bool IsBorderWidth(string p) => p == "thin" || p == "medium" || p == "thick" || p.EndsWith("px") || p.EndsWith("em");
    private static bool IsColor(string p) => p.StartsWith("#") || p.StartsWith("rgb") || p == "transparent" || p == "currentcolor" || IsNamedColor(p);
    private static bool IsFontSize(string p) => p is "xx-small" or "x-small" or "small" or "medium" or "large" or "x-large" or "xx-large" or "larger" or "smaller";
    private static bool IsNamedColor(string p) => KnownColors.Get(p).HasValue;
}
