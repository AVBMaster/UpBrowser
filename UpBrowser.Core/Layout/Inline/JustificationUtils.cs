using UpBrowser.Core.Dom;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout.Inline;

/// <summary>
/// Text-align application for laid-out logical lines. Implements the alignment
/// half of the engine's line breaker / shape-result spacing split: the line breaker records
/// expansion opportunities (breakable space runs), and this pass distributes the
/// free inline space into those opportunities (justify) or offsets the whole line
/// (end/center) once the natural content width is known.
/// </summary>
public static class JustificationUtils
{
    /// <summary>
    /// Place <paramref name="items"/> inside a content box of
    /// <paramref name="contentBoxInlineSize"/> according to the line's text-align.
    /// Must run AFTER <see cref="LogicalLineBuilder.CreateLine"/> has produced final
    /// natural positions/sizes and BEFORE run X coordinates are materialized.
    /// </summary>
    public static void ApplyTextAlignment(LineInfo info, LogicalLineItems items, float contentBoxInlineSize)
    {
        if (items == null || items.Count == 0)
            return;

        var align = ResolveEffectiveAlign(info);

        // Natural content extent = right edge of the right-most item.
        float naturalExtent = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (!ParticipatesInAlignment(it))
                continue;
            naturalExtent = MathF.Max(naturalExtent, it.Rect.InlineStart + it.Rect.InlineSize);
        }

        float free = MathF.Max(0f, contentBoxInlineSize - naturalExtent);

        switch (align)
        {
            case TextAlignType.End:
            case TextAlignType.Right when IsLtr(info.BaseDirection()):
                ShiftAll(items, free);
                break;

            case TextAlignType.Center:
                ShiftAll(items, free / 2f);
                break;

            case TextAlignType.Justify when !info.IsLastLine():
                ApplyJustifyExpansion(info, items, contentBoxInlineSize, naturalExtent);
                break;
        }
    }

    private static TextAlignType ResolveEffectiveAlign(LineInfo info)
    {
        var align = info.TextAlign();
        var dir = info.BaseDirection();

        // Map physical to logical for directional values (LTR base assumed for
        // Start; RTL flips Left/Right like the engine's text-align-line logic).
        return align switch
        {
            TextAlignType.Left => IsLtr(dir) ? TextAlignType.Start : TextAlignType.End,
            TextAlignType.Right => IsRtl(dir) ? TextAlignType.Start : TextAlignType.End,
            _ => align,
        };
    }

    private static void ShiftAll(LogicalLineItems items, float delta)
    {
        if (delta <= 0)
            return;
        for (int i = 0; i < items.Count; i++)
            items[i].MoveInInlineDirection(delta);
    }

    /// <summary>
    /// Distribute the free space into word-spacing expansion opportunities.
    /// Every collapsible space run between words counts as ONE opportunity and
    /// absorbs an equal share; the run's own advance grows so following items
    /// shift cumulatively. Trailing (hanging) spaces never expand.
    /// </span>
    /// </summary>
    private static void ApplyJustifyExpansion(LineInfo info, LogicalLineItems items,
        float contentBoxInlineSize, float naturalExtent)
    {
        float free = contentBoxInlineSize - naturalExtent;
        if (free <= 0)
            return;

        var source = info.ItemsData()?.TextContent ?? string.Empty;

        // Collect expansion opportunities (index 鈫?share). A text item composed
        // entirely of breakable spaces is one opportunity regardless of how many
        // collapsed spaces it represents.
        List<int> opportunities = new();
        for (int i = 0; i < items.Count; i++)
        {
            if (IsExpansionOpportunity(items[i], source))
                opportunities.Add(i);
        }

        if (opportunities.Count == 0)
            return;

        float expansion = free / opportunities.Count;

        float accumulatedShift = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (accumulatedShift != 0)
                it.MoveInInlineDirection(accumulatedShift);

            if (opportunities.BinarySearch(i) >= 0)
            {
                // Widen the space run itself so paint/picking see the full advance.
                it.InlineSize += expansion;
                accumulatedShift += expansion;
            }
        }
    }

    private static bool IsExpansionOpportunity(LogicalLineItem item, string source)
    {
        if (item.InlineItem is not { Type: InlineItem.InlineItemType.Text })
            return false;

        int start = item.TextOffset.Start;
        int end = item.TextOffset.End;
        if (end <= start || end > source.Length)
            return false;

        for (int i = start; i < end; i++)
        {
            if (!Character.IsBreakableSpace(source[i]))
                return false;
        }
        return true;
    }

    private static bool ParticipatesInAlignment(LogicalLineItem item) =>
        item.HasInFlowFragment() && !item.IsHiddenForPaint;

    private static bool IsLtr(TextDirection d) => d != TextDirection.Rtl;
    private static bool IsRtl(TextDirection d) => d == TextDirection.Rtl;
}
