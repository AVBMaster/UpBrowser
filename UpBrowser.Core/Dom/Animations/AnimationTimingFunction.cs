namespace UpBrowser.Core.Dom.Animations;

/// <summary>
/// CSS easing functions: linear, cubic-bezier, steps, and step functions.
/// Mirrors Blink's TimingFunction and CSSEasing.
/// </summary>
public abstract class AnimationTimingFunction
{
    public static AnimationTimingFunction Parse(string easing)
    {
        easing = easing.Trim().ToLowerInvariant();
        if (easing == "linear") return new LinearTimingFunction();
        if (easing == "ease") return new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        if (easing == "ease-in") return new CubicBezierTimingFunction(0.42, 0, 1.0, 1.0);
        if (easing == "ease-out") return new CubicBezierTimingFunction(0, 0, 0.58, 1.0);
        if (easing == "ease-in-out") return new CubicBezierTimingFunction(0.42, 0, 0.58, 1.0);
        if (easing.StartsWith("cubic-bezier("))
        {
            var inner = easing[13..^1];
            var parts = inner.Split(',');
            if (parts.Length == 4 && parts.All(p => double.TryParse(p.Trim(), out _)))
            {
                return new CubicBezierTimingFunction(
                    double.Parse(parts[0]), double.Parse(parts[1]),
                    double.Parse(parts[2]), double.Parse(parts[3]));
            }
        }
        if (easing.StartsWith("steps("))
        {
            var inner = easing[6..^1];
            var parts = inner.Split(',');
            if (int.TryParse(parts[0].Trim(), out int count) && count > 0)
            {
                string dir = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "end";
                return new StepsTimingFunction(count, dir == "start");
            }
        }
        if (easing == "step-start") return new StepsTimingFunction(1, true);
        if (easing == "step-end") return new StepsTimingFunction(1, false);
        return new LinearTimingFunction();
    }

    public abstract double Apply(double progress);

    public static readonly AnimationTimingFunction Linear = new LinearTimingFunction();
}

public class LinearTimingFunction : AnimationTimingFunction
{
    public override double Apply(double progress) => progress;
}

public class CubicBezierTimingFunction : AnimationTimingFunction
{
    public double P1x { get; }
    public double P1y { get; }
    public double P2x { get; }
    public double P2y { get; }

    private static readonly double BezierEpsilon = 1e-7;
    private static readonly int BezierMaxIterations = 10;

    public CubicBezierTimingFunction(double p1x, double p1y, double p2x, double p2y)
    {
        P1x = p1x; P1y = p1y; P2x = p2x; P2y = p2y;
    }

    public override double Apply(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return SampleCurveY(SolveCurveX(t));
    }

    private double SampleCurveX(double t) =>
        ((1 - t) * 3 * (1 - t) * t * P1x) + (3 * (1 - t) * t * t * P2x) + (t * t * t);

    private double SampleCurveY(double t) =>
        ((1 - t) * 3 * (1 - t) * t * P1y) + (3 * (1 - t) * t * t * P2y) + (t * t * t);

    private double SampleCurveDerivativeX(double t) =>
        (3 * (1 - t) * (1 - t) * P1x) + (6 * (1 - t) * t * (P2x - P1x)) + (3 * t * t * (1 - P2x));

    private double SolveCurveX(double x)
    {
        double t0 = x;
        double t1;
        for (int i = 0; i < BezierMaxIterations; i++)
        {
            double x2 = SampleCurveX(t0) - x;
            if (Math.Abs(x2) < BezierEpsilon) return t0;
            double d2 = SampleCurveDerivativeX(t0);
            if (Math.Abs(d2) < BezierEpsilon) break;
            t0 -= x2 / d2;
        }
        t1 = 0; double t2 = 1;
        t0 = x;
        if (t0 < t1) return t1;
        if (t0 > t2) return t2;
        while (t1 < t2)
        {
            double x2 = SampleCurveX(t0);
            if (Math.Abs(x2 - x) < BezierEpsilon) return t0;
            if (x > x2) t1 = t0;
            else t2 = t0;
            t0 = (t2 - t1) / 2 + t1;
        }
        return t0;
    }
}

public class StepsTimingFunction : AnimationTimingFunction
{
    public int Count { get; }
    public bool JumpAtStart { get; }

    public StepsTimingFunction(int count, bool jumpAtStart)
    {
        Count = Math.Max(1, count);
        JumpAtStart = jumpAtStart;
    }

    public override double Apply(double progress)
    {
        if (progress <= 0) return JumpAtStart ? 1.0 / Count : 0;
        if (progress >= 1) return 1;
        int step = (int)(progress * Count);
        if (JumpAtStart) step++;
        return Math.Min(1.0, step / (double)Count);
    }
}