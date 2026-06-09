namespace UpBrowser.Core.Dom;

public class ProgressEvent : Event
{
    public bool LengthComputable { get; }
    public ulong Loaded { get; }
    public ulong Total { get; }

    public ProgressEvent(string type, ProgressEventInit? init = null) : base(type, init)
    {
        LengthComputable = init?.LengthComputable ?? false;
        Loaded = init?.Loaded ?? 0;
        Total = init?.Total ?? 0;
    }
}

public class ProgressEventInit : EventInit
{
    public bool LengthComputable { get; set; }
    public ulong Loaded { get; set; }
    public ulong Total { get; set; }
}

public class AnimationEvent : Event
{
    public string AnimationName { get; }
    public float ElapsedTime { get; }
    public string PseudoElement { get; }

    public AnimationEvent(string type, AnimationEventInit? init = null) : base(type, init)
    {
        AnimationName = init?.AnimationName ?? "";
        ElapsedTime = init?.ElapsedTime ?? 0;
        PseudoElement = init?.PseudoElement ?? "";
    }
}

public class AnimationEventInit : EventInit
{
    public string AnimationName { get; set; } = "";
    public float ElapsedTime { get; set; }
    public string PseudoElement { get; set; } = "";
}

public class TransitionEvent : Event
{
    public string PropertyName { get; }
    public float ElapsedTime { get; }
    public string PseudoElement { get; }

    public TransitionEvent(string type, TransitionEventInit? init = null) : base(type, init)
    {
        PropertyName = init?.PropertyName ?? "";
        ElapsedTime = init?.ElapsedTime ?? 0;
        PseudoElement = init?.PseudoElement ?? "";
    }
}

public class TransitionEventInit : EventInit
{
    public string PropertyName { get; set; } = "";
    public float ElapsedTime { get; set; }
    public string PseudoElement { get; set; } = "";
}
