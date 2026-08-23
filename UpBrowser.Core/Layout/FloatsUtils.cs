using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;
using System.Linq;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Float positioning utilities. Mirrors floats_utils.cc.
/// </summary>
public static class FloatsUtils
{
    public static float ComputeMarginBoxInlineSizeForUnpositionedFloat(Element node, ConstraintSpace space)
    {
        var style = node.ComputedStyle;
        if (style == null) return 0;
        return style.MarginLeft.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight)
            + style.MarginRight.ToPixels(style.FontSize, space.RootFontSize, space.ViewportWidth, space.ViewportHeight);
    }

    public static PositionedFloat PositionFloat(float origin_inline_offset, UnpositionedFloat unpositioned_float)
    {
        return new PositionedFloat
        {
            BfcOffset = new BfcOffset(origin_inline_offset, unpositioned_float.BfcBlockOffset),
            LayoutResult = unpositioned_float.LayoutResult,
        };
    }
}

public struct UnpositionedFloat
{
    public float BfcBlockOffset;
    public float BfcLineOffset;
    public PhysicalSize Size;
    public LayoutResult? LayoutResult;
    public bool IsLeft;
}

public struct PositionedFloat
{
    public BfcOffset BfcOffset;
    public LayoutResult? LayoutResult;
}

/// <summary>
/// Manages the exclusion space (floats + initial letter boxes) for a block
/// formatting context. Mirrors ExclusionSpace / ExclusionSpaceInternal in
/// exclusion_space.cc (simplified, no segment tree).
/// </summary>
public class ExclusionSpace
{
    private readonly List<ExclusionArea> _exclusions = new();
    private float _leftClearOffset;
    private float _rightClearOffset;
    private float _initialLetterLeftClearOffset;
    private float _initialLetterRightClearOffset;

    public IReadOnlyList<ExclusionArea> AllExclusions => _exclusions;

    public void Add(ExclusionArea exclusion)
    {
        _exclusions.Add(exclusion);
        if (exclusion.ExclusionKind == ExclusionArea.Kind.Float)
        {
            if (exclusion.Type == FloatType.Left)
                _leftClearOffset = Math.Max(_leftClearOffset, exclusion.Rect.BlockEndOffset);
            else
                _rightClearOffset = Math.Max(_rightClearOffset, exclusion.Rect.BlockEndOffset);
        }
        else
        {
            if (exclusion.Type == FloatType.Left)
                _initialLetterLeftClearOffset = Math.Max(_initialLetterLeftClearOffset, exclusion.Rect.BlockEndOffset);
            else
                _initialLetterRightClearOffset = Math.Max(_initialLetterRightClearOffset, exclusion.Rect.BlockEndOffset);
        }
    }

    public float ClearanceOffset(ClearType clearType)
    {
        return clearType switch
        {
            ClearType.None => float.MinValue,
            ClearType.Left => _leftClearOffset,
            ClearType.Right => _rightClearOffset,
            ClearType.Both => Math.Max(_leftClearOffset, _rightClearOffset),
            _ => float.MinValue,
        };
    }

    public float ClearanceOffsetIncludingInitialLetter(ClearType clearType)
    {
        float baseOffset = ClearanceOffset(clearType);
        float letterOffset = clearType switch
        {
            ClearType.Left => _initialLetterLeftClearOffset,
            ClearType.Right => _initialLetterRightClearOffset,
            ClearType.Both => Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset),
            _ => 0,
        };
        return Math.Max(baseOffset, letterOffset);
    }

    /// <summary>
    /// The clearance offset to use when sizing an element that establishes a new
    /// formatting context, ignoring floats hidden for paint.
    /// Mirrors ExclusionSpace::NonHiddenClearanceOffsetIncludingInitialLetter().
    /// </summary>
    public float NonHiddenClearanceOffsetIncludingInitialLetter()
    {
        float maxClear = 0;
        foreach (var e in _exclusions)
        {
            if (e.IsHiddenForPaint) continue;
            maxClear = Math.Max(maxClear, e.IsForInitialLetterBox
                ? ClearanceOffsetIncludingInitialLetter(e.Type == FloatType.Left ? ClearType.Left : ClearType.Right)
                : ClearanceOffset(e.Type == FloatType.Left ? ClearType.Left : ClearType.Right));
        }
        maxClear = Math.Max(maxClear, ClearanceOffset(ClearType.Both));
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));
        return maxClear;
    }

    /// <summary>Create an independent copy of this exclusion space.</summary>
    public ExclusionSpace Copy()
    {
        var copy = new ExclusionSpace();
        foreach (var exclusion in _exclusions)
            copy.Add(exclusion);
        return copy;
    }

    public LayoutOpportunity FindLayoutOpportunity(BfcOffset offset, float availableInlineSize, float minimumInlineSize = 0)
    {
        float maxClear = Math.Max(_leftClearOffset, _rightClearOffset);
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));

        if (offset.BlockOffset >= maxClear)
        {
            var end = new BfcOffset(offset.LineOffset + Math.Max(0, availableInlineSize), float.MaxValue);
            return new LayoutOpportunity(new BfcRect(offset, end));
        }

        float lineStart = offset.LineOffset;
        foreach (var e in _exclusions)
        {
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= offset.BlockOffset || eStart >= offset.BlockOffset + availableInlineSize)
                continue;
            if (e.Type == FloatType.Left)
                lineStart = Math.Max(lineStart, e.Rect.LineEndOffset);
        }

        float lineEnd = offset.LineOffset + availableInlineSize;
        for (int i = _exclusions.Count - 1; i >= 0; i--)
        {
            var e = _exclusions[i];
            float eStart = e.Rect.BlockStartOffset;
            float eEnd = e.Rect.BlockEndOffset;
            if (eEnd <= offset.BlockOffset || eStart >= offset.BlockOffset + availableInlineSize)
                continue;
            if (e.Type == FloatType.Right)
                lineEnd = Math.Min(lineEnd, e.Rect.LineStartOffset);
        }

        if (lineStart >= lineEnd)
            lineStart = lineEnd;

        var startOff = new BfcOffset(lineStart, offset.BlockOffset);
        var endOff = new BfcOffset(lineEnd, float.MaxValue);
        return new LayoutOpportunity(new BfcRect(startOff, endOff));
    }

    public List<LayoutOpportunity> AllLayoutOpportunities(BfcOffset offset, float availableInlineSize)
    {
        var opportunities = new List<LayoutOpportunity>();
        var main = FindLayoutOpportunity(offset, availableInlineSize);
        opportunities.Add(main);
        float maxClear = Math.Max(_leftClearOffset, _rightClearOffset);
        maxClear = Math.Max(maxClear, Math.Max(_initialLetterLeftClearOffset, _initialLetterRightClearOffset));

        if (offset.BlockOffset < maxClear)
        {
            // If there are right floats, there may be a second opportunity on the left.
            bool hasRightFloat = _exclusions.Any(e => e.Type == FloatType.Right);
            if (hasRightFloat)
            {
                var rightOff = new BfcOffset(offset.LineOffset, offset.BlockOffset);
                var rightEnd = new BfcOffset(offset.LineOffset + availableInlineSize, float.MaxValue);
                opportunities.Add(new LayoutOpportunity(new BfcRect(rightOff, rightEnd)));
            }
        }
        return opportunities;
    }
}