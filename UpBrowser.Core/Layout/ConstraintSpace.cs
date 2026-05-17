namespace UpBrowser.Core.Layout;

/// <summary>
/// ConstraintSpace - encodes the dimensional constraint environment for layout passes.
/// Inspired by Blink's LayoutNG ConstraintSpace architecture.
/// </summary>
public struct ConstraintSpace
{
    private readonly AvailableSizeType _availableInlineSize;
    private readonly AvailableSizeType _availableBlockSize;
    private readonly float _inlineSizeValue;
    private readonly float _blockSizeValue;
    private readonly PercentageStorage _percentageResolutionStorage;
    private readonly float _percentageResolutionInline;
    private readonly float _percentageResolutionBlock;
    private readonly bool _isFixedInlineSize;
    private readonly bool _isFixedBlockSize;
    private readonly AutoBehavior _inlineAutoBehavior;
    private readonly AutoBehavior _blockAutoBehavior;
    private readonly bool _isShrinkToFit;
    private readonly WritingMode _writingMode;

    public ConstraintSpace(
        float availableInlineSize = float.NaN,
        float availableBlockSize = float.NaN,
        bool isFixedInlineSize = false,
        bool isFixedBlockSize = false,
        AutoBehavior inlineAutoBehavior = AutoBehavior.StretchImplicit,
        AutoBehavior blockAutoBehavior = AutoBehavior.StretchImplicit,
        bool isShrinkToFit = false,
        WritingMode writingMode = WritingMode.HorizontalTb,
        float percentageResolutionInline = 0,
        float percentageResolutionBlock = 0)
    {
        _availableInlineSize = float.IsNaN(availableInlineSize) ? AvailableSizeType.Auto : AvailableSizeType.Definite;
        _availableBlockSize = float.IsNaN(availableBlockSize) ? AvailableSizeType.Auto : AvailableSizeType.Definite;
        _inlineSizeValue = availableInlineSize;
        _blockSizeValue = availableBlockSize;
        _isFixedInlineSize = isFixedInlineSize;
        _isFixedBlockSize = isFixedBlockSize;
        _inlineAutoBehavior = inlineAutoBehavior;
        _blockAutoBehavior = blockAutoBehavior;
        _isShrinkToFit = isShrinkToFit;
        _writingMode = writingMode;

        _percentageResolutionStorage = (percentageResolutionInline > 0 || percentageResolutionBlock > 0)
            ? PercentageStorage.Defined
            : PercentageStorage.SameAsAvailable;
        _percentageResolutionInline = percentageResolutionInline > 0 ? percentageResolutionInline : availableInlineSize;
        _percentageResolutionBlock = percentageResolutionBlock > 0 ? percentageResolutionBlock : availableBlockSize;
    }

    public float AvailableInlineSize => _availableInlineSize == AvailableSizeType.Definite ? _inlineSizeValue : float.NaN;
    public float AvailableBlockSize => _availableBlockSize == AvailableSizeType.Definite ? _blockSizeValue : float.NaN;
    public bool HasDefiniteInlineSize => _availableInlineSize == AvailableSizeType.Definite;
    public bool HasDefiniteBlockSize => _availableBlockSize == AvailableSizeType.Definite;
    public bool IsFixedInlineSize => _isFixedInlineSize;
    public bool IsFixedBlockSize => _isFixedBlockSize;
    public AutoBehavior InlineAutoBehavior => _inlineAutoBehavior;
    public AutoBehavior BlockAutoBehavior => _blockAutoBehavior;
    public bool IsShrinkToFit => _isShrinkToFit;
    public WritingMode WritingMode => _writingMode;

    public float PercentageResolutionInlineSize =>
        _percentageResolutionStorage == PercentageStorage.SameAsAvailable ? AvailableInlineSize : _percentageResolutionInline;

    public float PercentageResolutionBlockSize =>
        _percentageResolutionStorage == PercentageStorage.SameAsAvailable ? AvailableBlockSize : _percentageResolutionBlock;

    public ConstraintSpace WithInlineSize(float size) => new(
        size, _blockSizeValue, true, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit, _writingMode,
        _percentageResolutionInline, _percentageResolutionBlock);

    public ConstraintSpace WithBlockSize(float size) => new(
        _inlineSizeValue, size, _isFixedInlineSize, true, _inlineAutoBehavior, _blockAutoBehavior, _isShrinkToFit, _writingMode,
        _percentageResolutionInline, _percentageResolutionBlock);

    public ConstraintSpace WithShrinkToFit(bool value) => new(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, _inlineAutoBehavior, _blockAutoBehavior, value, _writingMode,
        _percentageResolutionInline, _percentageResolutionBlock);

    public ConstraintSpace WithAutoBehavior(AutoBehavior inline, AutoBehavior block) => new(
        _inlineSizeValue, _blockSizeValue, _isFixedInlineSize, _isFixedBlockSize, inline, block, _isShrinkToFit, _writingMode,
        _percentageResolutionInline, _percentageResolutionBlock);

    public static ConstraintSpace Infinite() => new();

    public static ConstraintSpace ForViewport(float width, float height) => new(width, height, false, false);

    public override string ToString() =>
        $"ConstraintSpace(inline:{(HasDefiniteInlineSize ? _inlineSizeValue.ToString("F1") : "auto")}, block:{(HasDefiniteBlockSize ? _blockSizeValue.ToString("F1") : "auto")})";
}

public enum AvailableSizeType { Auto, Definite, MinContent, MaxContent, FitContent }
public enum PercentageStorage { SameAsAvailable, Defined }
public enum AutoBehavior { StretchImplicit, FitContent }
public enum WritingMode { HorizontalTb, VerticalRl, VerticalLr }

/// <summary>
/// LayoutResult - the result of a layout pass within a constraint space.
/// </summary>
public struct LayoutResult
{
    public float InlineSize { get; set; }
    public float BlockSize { get; set; }
    public bool IsIntrinsic { get; set; }
    public bool NeedsLayout { get; set; }

    public static LayoutResult FromSize(float inline, float block) => new() { InlineSize = inline, BlockSize = block };
    public static LayoutResult MinContent(float inline, float block) => new() { InlineSize = inline, BlockSize = block, IsIntrinsic = true };
    public static LayoutResult MaxContent(float inline, float block) => new() { InlineSize = inline, BlockSize = block, IsIntrinsic = true };
}
