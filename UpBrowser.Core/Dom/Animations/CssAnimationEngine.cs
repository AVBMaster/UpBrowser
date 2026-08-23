using SkiaSharp;
using UpBrowser.Core.Css.Rules;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// CSS Animation and Transition engine, mirroring Blink's CSSAnimations.
/// Processes @keyframes rules and CSS animation/transition properties,
/// computes interpolated values, and applies them to element ComputedStyle.
/// </summary>
public class CssAnimationEngine
{
    private readonly Dictionary<Element, List<RunningAnimation>> _runningAnimations = new();
    private readonly Dictionary<Element, List<RunningTransition>> _runningTransitions = new();
    private double _currentTimeMs;

    public void SetCurrentTime(double timeMs) => _currentTimeMs = timeMs;

    /// <summary>Process animation and transition properties for all elements.</summary>
    public void ProcessAnimations(Element root, List<StyleRuleKeyframes> keyframeRules)
    {
        TraverseAndProcess(root, keyframeRules);
    }

    private void TraverseAndProcess(Element element, List<StyleRuleKeyframes> keyframeRules)
    {
        ProcessElementAnimations(element, keyframeRules);
        ProcessElementTransitions(element);

        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                TraverseAndProcess(childEl, keyframeRules);
        }
    }

    private void ProcessElementAnimations(Element element, List<StyleRuleKeyframes> keyframeRules)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        string animationName = style.AnimationName ?? "";
        string animationDuration = style.AnimationDuration ?? "0s";
        string animationTiming = style.AnimationTimingFunction ?? "ease";
        string animationDelay = style.AnimationDelay ?? "0s";
        string animationIteration = style.AnimationIterationCount ?? "1";
        string animationDirection = style.AnimationDirection ?? "normal";
        string animationFillMode = style.AnimationFillMode ?? "none";
        string animationPlayState = style.AnimationPlayState ?? "running";

        if (string.IsNullOrEmpty(animationName) || animationName == "none")
        {
            // Remove any running animations for this element
            if (_runningAnimations.Remove(element))
            {
                RestoreElementStyle(element);
            }
            return;
        }

        // Find matching @keyframes
        var keyframes = keyframeRules.Find(k => k.Name.Equals(animationName, StringComparison.OrdinalIgnoreCase));
        if (keyframes == null || keyframes.Keyframes.Count == 0)
        {
            if (_runningAnimations.Remove(element))
                RestoreElementStyle(element);
            return;
        }

        // Parse timing values
        double duration = ParseTime(animationDuration);
        double delay = ParseTime(animationDelay);
        double iterations = ParseIterations(animationIteration);
        bool isReverse = animationDirection.Equals("reverse", StringComparison.OrdinalIgnoreCase) ||
                         animationDirection.Equals("alternate", StringComparison.OrdinalIgnoreCase) ||
                         animationDirection.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase);
        bool isAlternate = animationDirection.Equals("alternate", StringComparison.OrdinalIgnoreCase) ||
                          animationDirection.Equals("alternate-reverse", StringComparison.OrdinalIgnoreCase);
        bool isPaused = animationPlayState.Equals("paused", StringComparison.OrdinalIgnoreCase);
        var timingFunc = AnimationTimingFunction.Parse(animationTiming);

        if (duration <= 0) return;

        // Calculate local time
        double localTime = _currentTimeMs - delay;
        if (localTime < 0) localTime = 0;

        if (!_runningAnimations.TryGetValue(element, out var runningAnimList))
        {
            runningAnimList = new List<RunningAnimation>();
            _runningAnimations[element] = runningAnimList;
        }

        // Create or update the running animation
        var existing = runningAnimList.Find(a => a.Name == animationName);
        if (existing == null)
        {
            existing = new RunningAnimation { Name = animationName, StartTime = _currentTimeMs };
            runningAnimList.Add(existing);
        }
        existing.TimingFunction = timingFunc;
        existing.Duration = duration;
        existing.Iterations = iterations;
        existing.Delay = delay;
        existing.IsAlternate = isAlternate;
        existing.IsReverse = isReverse;

        // Animation progress
        double totalDuration = duration * iterations;
        if (totalDuration <= 0) return;

        double elapsed = localTime;
        if (elapsed > totalDuration)
        {
            if (animationFillMode == "forwards" || animationFillMode == "both")
            {
                // Apply the final keyframe state
                ApplyKeyframeState(element, keyframes, timingFunc, 1.0, isReverse, isAlternate, iterations);
            }
            return;
        }

        // Calculate progress
        double iterationCount = elapsed / duration;
        double iterationProgress = iterationCount - Math.Floor(iterationCount);
        int currentIteration = (int)Math.Floor(iterationCount);

        // Apply direction
        bool reverseThisIteration = isReverse;
        if (isAlternate && currentIteration % 2 == 1)
            reverseThisIteration = !isReverse;

        double progress = reverseThisIteration ? 1.0 - iterationProgress : iterationProgress;
        progress = timingFunc.Apply(progress);

        // Apply the keyframe state
        ApplyKeyframeState(element, keyframes, timingFunc, progress, isReverse, isAlternate, iterations);
    }

    private void ProcessElementTransitions(Element element)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        string transitionProperty = style.TransitionProperty ?? "all";
        string transitionDuration = style.TransitionDuration ?? "0s";
        string transitionTiming = style.TransitionTimingFunction ?? "ease";
        string transitionDelay = style.TransitionDelay ?? "0s";

        if (transitionProperty == "none" || transitionDuration == "0s" || transitionDuration == "0ms")
            return;

        double duration = ParseTime(transitionDuration);
        double delay = ParseTime(transitionDelay);

        if (duration <= 0) return;

        if (!_runningTransitions.TryGetValue(element, out var transList))
        {
            transList = new List<RunningTransition>();
            _runningTransitions[element] = transList;
        }

        // Process each transition
        var properties = transitionProperty == "all" ? GetAllTransitionProperties() : transitionProperty.Split(',').Select(p => p.Trim());

        foreach (var prop in properties)
        {
            var existing = transList.Find(t => t.Property == prop);
            if (existing == null)
            {
                existing = new RunningTransition
                {
                    Property = prop,
                    FromValue = GetCurrentPropertyValue(element, prop),
                    StartTime = _currentTimeMs
                };
                transList.Add(existing);
            }

            double elapsed = _currentTimeMs - existing.StartTime - delay;
            if (elapsed <= 0) continue;

            double progress = Math.Min(1.0, elapsed / duration);
            var timingFunc = AnimationTimingFunction.Parse(transitionTiming);
            double easedProgress = timingFunc.Apply(progress);

            object? toValue = GetCurrentPropertyValue(element, prop);
            object? interpolated = Interpolation.Interpolate(prop, existing.FromValue, toValue, easedProgress);
            ApplyPropertyValue(element, prop, interpolated);

            if (progress >= 1.0)
            {
                // Transition complete
                existing.IsComplete = true;
            }
        }

        // Remove completed transitions
        transList.RemoveAll(t => t.IsComplete);
    }

    private void ApplyKeyframeState(Element element, StyleRuleKeyframes keyframes, AnimationTimingFunction timingFunc,
        double progress, bool isReverse, bool isAlternate, double iterations)
    {
        var style = element.ComputedStyle;
        if (style == null) return;

        // Find the two keyframes surrounding the current progress
        var sorted = keyframes.Keyframes
            .Select(k => new { Key = ParseKeyframeOffset(k.Key), k.Properties })
            .OrderBy(k => k.Key)
            .ToList();

        if (sorted.Count == 0) return;

        // Find surrounding keyframes
        int fromIdx = 0;
        int toIdx = sorted.Count - 1;
        double fromOffset = 0;
        double toOffset = 1;

        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Key <= progress)
            {
                fromIdx = i;
                fromOffset = sorted[i].Key;
            }
            if (sorted[i].Key >= progress && toIdx == sorted.Count - 1)
            {
                toIdx = i;
                toOffset = sorted[i].Key;
            }
        }

        var fromEntry = sorted[fromIdx];
        var toEntry = sorted[toIdx];

        double localProgress = toOffset - fromOffset > 0
            ? (progress - fromOffset) / (toOffset - fromOffset)
            : 0;

        // Interpolate between from and to keyframes
        foreach (var prop in fromEntry.Properties.Properties)
        {
            string propName = prop.Name.ToCssString();
            object? fromVal = prop.Value.CssText();
            object? toVal = null;

            if (toEntry.Properties.Properties.Any(p => p.Name.ToCssString() == propName))
            {
                var toProp = toEntry.Properties.Properties.First(p => p.Name.ToCssString() == propName);
                toVal = toProp.Value.CssText();
            }

            object? interpolated = Interpolation.Interpolate(propName, fromVal, toVal ?? fromVal, localProgress);
            ApplyPropertyValue(element, propName, interpolated);
        }
    }

    private static double ParseKeyframeOffset(string key)
    {
        key = key.Trim().ToLowerInvariant();
        if (key == "from") return 0;
        if (key == "to") return 1;
        if (key.EndsWith('%') && double.TryParse(key[..^1], out var pct))
            return pct / 100.0;
        return 0;
    }

    private static double ParseTime(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value.EndsWith("ms") && double.TryParse(value[..^2], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var ms))
            return ms;
        if (value.EndsWith("s") && double.TryParse(value[..^1], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var s))
            return s * 1000;
        return 0;
    }

    private static double ParseIterations(string value)
    {
        if (value.Equals("infinite", StringComparison.OrdinalIgnoreCase))
            return double.PositiveInfinity;
        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var count))
            return count;
        return 1;
    }

    private static object? GetCurrentPropertyValue(Element element, string property)
    {
        var style = element.ComputedStyle;
        if (style == null) return null;

        return property switch
        {
            "opacity" => (object)style.Opacity,
            "color" => style.Color,
            "background-color" => style.BackgroundColor ?? style.Color,
            "transform" => style.Transform ?? "none",
            "width" => style.Width?.ToString() ?? "auto",
            "height" => style.Height?.ToString() ?? "auto",
            "font-size" => (object)style.FontSize,
            _ => style.GetCustomProperty(property) ?? GetPropertyViaReflection(style, property)
        };
    }

    private static string? GetPropertyViaReflection(ComputedStyle style, string property)
    {
        var prop = property.Replace("-", "").Replace(" ", "");
        var field = typeof(ComputedStyle).GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(style)?.ToString();
    }

    private static void ApplyPropertyValue(Element element, string property, object? value)
    {
        if (value == null) return;
        var style = element.ComputedStyle;
        if (style == null) return;

        switch (property)
        {
            case "opacity":
                style.Opacity = value is float f ? f : 1;
                break;
            case "color":
                style.Color = value is SKColor c ? c : UpBrowser.Core.Css.ColorParser.Parse(value.ToString() ?? "black");
                break;
            case "background-color":
                style.BackgroundColor = value is SKColor bg ? bg : UpBrowser.Core.Css.ColorParser.Parse(value.ToString() ?? "transparent");
                break;
            default:
                // Store as custom property for now
                style.SetCustomProperty(property, value.ToString() ?? "");
                break;
        }
    }

    public void RestoreElementStyle(Element element)
    {
        // Restore the original computed style from the cascade
        // The cascade will re-resolve on the next style pass
    }

    private static IEnumerable<string> GetAllTransitionProperties() => new[]
    {
        "opacity", "color", "background-color", "transform",
        "width", "height", "font-size", "left", "right", "top", "bottom",
        "margin", "padding", "border-width", "border-color",
        "filter", "backdrop-filter", "box-shadow", "text-shadow",
        "outline", "outline-width", "outline-color"
    };

    private class RunningAnimation
    {
        public string Name { get; set; } = "";
        public double StartTime { get; set; }
        public double Duration { get; set; }
        public double Delay { get; set; }
        public double Iterations { get; set; } = 1;
        public bool IsReverse { get; set; }
        public bool IsAlternate { get; set; }
        public AnimationTimingFunction TimingFunction { get; set; } = AnimationTimingFunction.Linear;
    }

    private class RunningTransition
    {
        public string Property { get; set; } = "";
        public object? FromValue { get; set; }
        public double StartTime { get; set; }
        public bool IsComplete { get; set; }
    }
}