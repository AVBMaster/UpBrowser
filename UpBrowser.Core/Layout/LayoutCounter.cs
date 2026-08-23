using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Represents the text of a CSS counter. Mirrors layout_counter.cc.
/// Counters are always generated content ("content: counter(a)") and anonymous.
/// </summary>
public class LayoutCounter
{
    public string Identifier { get; }
    public string ListStyle { get; }
    public string Separator { get; }
    public List<int> Values { get; private set; } = new();

    public LayoutCounter(string identifier, string listStyle, string separator = "")
    {
        Identifier = identifier;
        ListStyle = listStyle;
        Separator = separator;
    }

    public void UpdateValues(List<int> values)
    {
        Values = values;
    }

    public string RenderText()
    {
        if (Values.Count == 0) return "0";
        return string.Join(Separator, Values.Select(v => FormatCounterValue(v, ListStyle)));
    }

    public bool IsDirectionalSymbolMarker =>
        ListStyle == "disclosure-open" || ListStyle == "disclosure-closed";

    public static string FormatCounterValue(int value, string listStyle)
    {
        return listStyle switch
        {
            "decimal" => value.ToString(),
            "decimal-leading-zero" => value.ToString().PadLeft(2, '0'),
            "lower-roman" => ToRoman(value).ToLower(),
            "upper-roman" => ToRoman(value),
            "lower-alpha" or "lower-latin" => ((char)('a' + ((value - 1) % 26))).ToString(),
            "upper-alpha" or "upper-latin" => ((char)('A' + ((value - 1) % 26))).ToString(),
            "lower-greek" => ((char)(0x03
            + ((value - 1) % 24))).ToString(),
            "disc" => "\u2022",
            "circle" => "\u25CB",
            "square" => "\u25A0",
            "disclosure-open" => "\u25BC",
            "disclosure-closed" => "\u25B6",
            _ => value.ToString()
        };
    }

    private static string ToRoman(int number)
    {
        if (number <= 0) return "";
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var result = "";
        for (int i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                result += symbols[i];
                number -= values[i];
            }
        }
        return result;
    }

    public static string ListStyleName(LayoutCounter? counter, ComputedStyle style)
    {
        if (counter != null)
            return counter.ListStyle;
        var listStyle = style.ListStyleType;
        return listStyle switch
        {
            ListStyleType.Decimal => "decimal",
            ListStyleType.DecimalLeadingZero => "decimal-leading-zero",
            ListStyleType.LowerRoman => "lower-roman",
            ListStyleType.UpperRoman => "upper-roman",
            ListStyleType.LowerAlpha => "lower-alpha",
            ListStyleType.UpperAlpha => "upper-alpha",
            ListStyleType.Disc => "disc",
            ListStyleType.Circle => "circle",
            ListStyleType.Square => "square",
            _ => "decimal"
        };
    }
}

/// <summary>
/// CSS counter management. Mirrors the counter system in layout_counter.cc.
/// </summary>
public class CounterManager
{
    private readonly Dictionary<string, int> _counters = new();

    public void Reset(string name, int value = 0)
    {
        _counters[name] = value;
    }

    public void Increment(string name, int delta = 1)
    {
        if (!_counters.ContainsKey(name))
            _counters[name] = 0;
        _counters[name] += delta;
    }

    public int GetValue(string name)
    {
        return _counters.GetValueOrDefault(name, 0);
    }

    public void SetValue(string name, int value)
    {
        _counters[name] = value;
    }

    public void PushScope()
    {
        // Each scope resets counters to previous values
    }

    public void PopScope()
    {
        // Restore counter values to previous scope
    }

    public void Clear()
    {
        _counters.Clear();
    }
}