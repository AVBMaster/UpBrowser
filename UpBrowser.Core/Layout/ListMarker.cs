using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Generates and lays out CSS list markers (bullets / numbers).
/// Mirrors the core of list_marker.cc.
/// </summary>
public static class ListMarker
{
    /// <summary>Generate the marker text for a list item at the given index (1-based).</summary>
    public static string MarkerText(ListStyleType type, int itemIndex)
    {
        switch (type)
        {
            case ListStyleType.None:
                return "";
            case ListStyleType.Decimal:
                return itemIndex.ToString() + ".";
            case ListStyleType.DecimalLeadingZero:
                return itemIndex.ToString("D2") + ".";
            case ListStyleType.LowerRoman:
                return ToRoman(itemIndex) + ".";
            case ListStyleType.UpperRoman:
                return ToRoman(itemIndex).ToUpperInvariant() + ".";
            case ListStyleType.LowerAlpha:
                return ToAlpha(itemIndex) + ".";
            case ListStyleType.UpperAlpha:
                return ToAlpha(itemIndex).ToUpperInvariant() + ".";
            case ListStyleType.Disc:
            case ListStyleType.Circle:
            case ListStyleType.Square:
                return "•";
            default:
                return "•";
        }
    }

    /// <summary>Estimate the marker width in pixels based on list-style-type and font size.</summary>
    public static float MarkerWidth(ListStyleType type, ListStylePosition position, float fontSize)
    {
        if (position == ListStylePosition.Inside)
            return fontSize * 1.2f;
        switch (type)
        {
            case ListStyleType.None:
                return 0;
            case ListStyleType.Disc:
            case ListStyleType.Circle:
            case ListStyleType.Square:
                return fontSize * 0.8f;
            default:
                return fontSize * 1.5f; // numbered markers
        }
    }

    private static string ToAlpha(int value)
    {
        if (value <= 0) return "";
        var sb = new System.Text.StringBuilder();
        while (value > 0)
        {
            value--;
            sb.Insert(0, (char)('a' + (value % 26)));
            value /= 26;
        }
        return sb.ToString();
    }

    private static string ToRoman(int value)
    {
        if (value <= 0 || value >= 4000) return value.ToString();
        var sb = new System.Text.StringBuilder();
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var numerals = new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        for (int i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                sb.Append(numerals[i]);
                value -= values[i];
            }
        }
        return sb.ToString();
    }
}