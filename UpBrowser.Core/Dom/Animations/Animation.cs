namespace UpBrowser.Core.Dom.Animations;

public enum AnimationPlayState { Idle, Running, Paused, Finished }
public enum AnimationReplaceState { Active, Removed, Persisted }
public enum FillMode { None, Forwards, Backwards, Both, Auto }
public enum PlaybackDirection { Normal, Reverse, Alternate, AlternateReverse }
public enum CompositeOperation { Replace, Add, Accumulate }
public enum Phase { Before, Active, After, Idle }

public class AnimationTimeline
{
    public double? CurrentTime { get; protected set; }
}

public class DocumentTimeline : AnimationTimeline
{
    public double OriginTime { get; }

    public DocumentTimeline(double originTime = 0)
    {
        OriginTime = originTime;
    }
}

public class Animation : EventTarget
{
    public string Id { get; set; } = "";
    public AnimationEffect? Effect { get; set; }
    public AnimationTimeline? Timeline { get; set; }
    public double? StartTime { get; set; }
    public double? CurrentTime { get; set; }
    public double PlaybackRate { get; set; } = 1;
    public AnimationPlayState PlayState { get; private set; } = AnimationPlayState.Idle;
    public AnimationReplaceState ReplaceState { get; private set; } = AnimationReplaceState.Active;
    public bool Pending { get; private set; }
    public bool Finished { get; private set; }

    public event Action<Animation>? OnFinish;
    public event Action<Animation>? OnCancel;
    public event Action<Animation>? OnRemove;

    public Animation(AnimationEffect? effect = null, AnimationTimeline? timeline = null)
    {
        Effect = effect;
        Timeline = timeline;
    }

    public void Play()
    {
        PlayState = AnimationPlayState.Running;
        Pending = true;
    }

    public void Pause()
    {
        PlayState = AnimationPlayState.Paused;
    }

    public void Cancel()
    {
        PlayState = AnimationPlayState.Idle;
        OnCancel?.Invoke(this);
    }

    public void Finish()
    {
        PlayState = AnimationPlayState.Finished;
        Finished = true;
        OnFinish?.Invoke(this);
    }

    public void Reverse()
    {
        PlaybackRate = -PlaybackRate;
        Play();
    }

    public void CommitStyles()
    {
    }

    public void UpdatePlaybackRate(double rate)
    {
        PlaybackRate = rate;
    }

    public void Persist()
    {
        ReplaceState = AnimationReplaceState.Persisted;
    }

    public Animation Clone()
    {
        return new Animation(Effect, Timeline)
        {
            Id = Id,
            StartTime = StartTime,
            CurrentTime = CurrentTime,
            PlaybackRate = PlaybackRate,
            PlayState = PlayState,
        };
    }
}

public abstract class AnimationEffect
{
    public EffectTiming Timing { get; protected set; } = new();
    public ComputedEffectTiming ComputedTiming => GetComputedTiming();

    public void UpdateTiming(OptionalEffectTiming timing)
    {
        if (timing.Delay.HasValue) Timing.Delay = timing.Delay.Value;
        if (timing.EndDelay.HasValue) Timing.EndDelay = timing.EndDelay.Value;
        if (timing.Fill.HasValue) Timing.Fill = timing.Fill.Value;
        if (timing.IterationStart.HasValue) Timing.IterationStart = timing.IterationStart.Value;
        if (timing.Iterations.HasValue) Timing.Iterations = timing.Iterations.Value;
        if (timing.Duration.HasValue) Timing.Duration = timing.Duration.Value;
        if (timing.Direction.HasValue) Timing.Direction = timing.Direction.Value;
        if (timing.Easing != null) Timing.Easing = timing.Easing;
    }

    public ComputedEffectTiming GetComputedTiming()
    {
        return new ComputedEffectTiming
        {
            Delay = Timing.Delay,
            EndDelay = Timing.EndDelay,
            Fill = Timing.Fill,
            IterationStart = Timing.IterationStart,
            Iterations = Timing.Iterations,
            Duration = Timing.Duration,
            Direction = Timing.Direction,
            Easing = Timing.Easing,
            ActiveDuration = Timing.Duration * Timing.Iterations,
            EndTime = Timing.Delay + Timing.Duration * Timing.Iterations + Timing.EndDelay,
            Progress = 0,
            CurrentIteration = 0,
            Phase = Phase.Idle,
        };
    }
}

public class KeyframeEffect : AnimationEffect
{
    public Element? Target { get; set; }
    public string? PseudoElement { get; set; }
    public CompositeOperation Composite { get; set; } = CompositeOperation.Replace;
    public string? IterationComposite { get; set; }

    private List<Dictionary<string, object?>> _keyframes = new();

    public KeyframeEffect(Element? target = null, object? keyframes = null, KeyframeEffectOptions? options = null)
    {
        Target = target;
        if (options != null)
        {
            if (options.Delay.HasValue) Timing.Delay = options.Delay.Value;
            if (options.Duration.HasValue) Timing.Duration = options.Duration.Value;
            if (options.Iterations.HasValue) Timing.Iterations = options.Iterations.Value;
            if (options.Direction.HasValue) Timing.Direction = options.Direction.Value;
            if (options.Easing != null) Timing.Easing = options.Easing;
            if (options.Fill.HasValue) Timing.Fill = options.Fill.Value;
            Composite = options.Composite;
        }
        if (keyframes != null)
            SetKeyframes(keyframes);
    }

    public void SetKeyframes(object? keyframes)
    {
        _keyframes.Clear();
    }

    public object[] GetKeyframes() => _keyframes.ToArray();
}

public class EffectTiming
{
    public double Delay { get; set; }
    public double EndDelay { get; set; }
    public FillMode Fill { get; set; } = FillMode.Auto;
    public double IterationStart { get; set; }
    public double Iterations { get; set; } = 1;
    public double Duration { get; set; } = double.NaN;
    public PlaybackDirection Direction { get; set; } = PlaybackDirection.Normal;
    public string Easing { get; set; } = "linear";
    public CompositeOperation Composite { get; set; } = CompositeOperation.Replace;
    public string? IterationComposite { get; set; }
}

public class OptionalEffectTiming
{
    public double? Delay { get; set; }
    public double? EndDelay { get; set; }
    public FillMode? Fill { get; set; }
    public double? IterationStart { get; set; }
    public double? Iterations { get; set; }
    public double? Duration { get; set; }
    public PlaybackDirection? Direction { get; set; }
    public string? Easing { get; set; }
    public CompositeOperation? Composite { get; set; }
}

public class ComputedEffectTiming : EffectTiming
{
    public double ActiveDuration { get; set; }
    public double? EndTime { get; set; }
    public double? LocalTime { get; set; }
    public double? Progress { get; set; }
    public double? CurrentIteration { get; set; }
    public Phase Phase { get; set; }
}

public class KeyframeEffectOptions
{
    public double? Delay { get; set; }
    public double? Duration { get; set; }
    public double? Iterations { get; set; }
    public PlaybackDirection? Direction { get; set; }
    public string? Easing { get; set; }
    public FillMode? Fill { get; set; }
    public CompositeOperation Composite { get; set; } = CompositeOperation.Replace;
}
