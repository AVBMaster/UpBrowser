using System.Text.RegularExpressions;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css;

public class CssParser
{
    private static readonly Regex CommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex PropertyRegex = new(@"^([\-a-z]+)\s*:\s*(.+)$", RegexOptions.IgnoreCase);

    public Stylesheet Parse(string cssText)
    {
        var stylesheet = new Stylesheet();
        if (string.IsNullOrEmpty(cssText)) return stylesheet;

        cssText = CommentRegex.Replace(cssText, "");

        var bracePairs = new List<(int start, int end)>();
        int braceDepth = 0;
        int lastOpen = -1;

        for (int i = 0; i < cssText.Length; i++)
        {
            if (cssText[i] == '{') { braceDepth++; if (braceDepth == 1) lastOpen = i; }
            else if (cssText[i] == '}') { braceDepth--; if (braceDepth == 0) bracePairs.Add((lastOpen, i)); }
        }

        int lastEnd = -1;
        foreach (var (start, end) in bracePairs)
        {
            var selectorPart = cssText[(lastEnd + 1)..start].Trim();
            var bodyPart = cssText[(start + 1)..end].Trim();
            lastEnd = end;

            if (string.IsNullOrEmpty(selectorPart)) continue;

            var selectors = ParseSelectors(selectorPart);
            var properties = ParseProperties(bodyPart);

            foreach (var selector in selectors)
            {
                var rule = new CssRule
                {
                    Selector = selector,
                    Specificity = CalculateSpecificity(selector),
                    Properties = new Dictionary<string, string>(properties)
                };
                stylesheet.Rules.Add(rule);
            }
        }

        stylesheet.Rules.Sort((a, b) =>
        {
            int cmp = a.Specificity.a.CompareTo(b.Specificity.a);
            if (cmp != 0) return cmp;
            cmp = a.Specificity.b.CompareTo(b.Specificity.b);
            if (cmp != 0) return cmp;
            cmp = a.Specificity.c.CompareTo(b.Specificity.c);
            if (cmp != 0) return cmp;
            return a.Specificity.d.CompareTo(b.Specificity.d);
        });
        return stylesheet;
    }

    private List<string> ParseSelectors(string selectorText)
    {
        var selectors = new List<string>();
        var current = "";
        int parenDepth = 0;

        for (int i = 0; i < selectorText.Length; i++)
        {
            char c = selectorText[i];
            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == ',' && parenDepth == 0)
            {
                if (!string.IsNullOrWhiteSpace(current))
                    selectors.Add(current.Trim());
                current = "";
            }
            else current += c;
        }

        if (!string.IsNullOrWhiteSpace(current))
            selectors.Add(current.Trim());

        return selectors;
    }

    private Dictionary<string, string> ParseProperties(string body)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declarations = body.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var decl in declarations)
        {
            var match = PropertyRegex.Match(decl.Trim());
            if (match.Success)
            {
                var name = match.Groups[1].Value.ToLowerInvariant();
                var value = match.Groups[2].Value.Trim();
                properties[name] = value;
            }
        }

        return properties;
    }

    private (int a, int b, int c, int d) CalculateSpecificity(string selector)
    {
        int a = 0, b = 0, c = 0, d = 0;
        int i = 0;

        while (i < selector.Length)
        {
            char ch = selector[i];

            if (ch == '#')
            {
                a++;
                i++;
                while (i < selector.Length && IsIdentChar(selector[i])) i++;
            }
            else if (ch == '.')
            {
                b++;
                i++;
                while (i < selector.Length && IsIdentChar(selector[i])) i++;
            }
            else if (ch == '[')
            {
                b++;
                int depth = 1;
                i++;
                while (i < selector.Length && depth > 0)
                {
                    if (selector[i] == '[') depth++;
                    else if (selector[i] == ']') depth--;
                    i++;
                }
            }
            else if (ch == ':')
            {
                if (i + 1 < selector.Length && selector[i + 1] == ':')
                { d++; i += 2; }
                else { c++; i++; }
                while (i < selector.Length && selector[i] != '(' && selector[i] != ' ') i++;
                if (i < selector.Length && selector[i] == '(')
                {
                    int depth = 1;
                    i++;
                    while (i < selector.Length && depth > 0)
                    {
                        if (selector[i] == '(') depth++;
                        else if (selector[i] == ')') depth--;
                        i++;
                    }
                }
            }
            else if (ch == '*') { d++; i++; }
            else if (IsIdentStart(ch))
            {
                while (i < selector.Length && IsIdentChar(selector[i])) i++;
                if (i < selector.Length && selector[i] == '(')
                {
                    int depth = 1;
                    i++;
                    while (i < selector.Length && depth > 0)
                    {
                        if (selector[i] == '(') depth++;
                        else if (selector[i] == ')') depth--;
                        i++;
                    }
                }
                else d++;
            }
            else if (ch == ' ' || ch == '>' || ch == '+' || ch == '~')
            { i++; }
            else i++;
        }

        return (a, b, c, d);
    }

    private bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
    private bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-';

    public Dictionary<string, string> ParseInlineStyle(string styleText)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(styleText)) return properties;

        var declarations = styleText.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var decl in declarations)
        {
            var colonIndex = decl.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = decl[..colonIndex].Trim().ToLowerInvariant();
                var value = decl[(colonIndex + 1)..].Trim();
                properties[name] = value;
            }
        }

        return properties;
    }
}

public class Stylesheet
{
    public List<CssRule> Rules { get; } = new();

    public void AddRule(CssRule rule) => Rules.Add(rule);
}

public class CssRule
{
    public string Selector { get; set; } = string.Empty;
    public (int a, int b, int c, int d) Specificity { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public bool IsImportant { get; set; }
}

public class CssSelector
{
    public SelectorType Type { get; set; }
    public string? TagName { get; set; }
    public string? Id { get; set; }
    public List<string> Classes { get; } = new();
    public string? AttributeName { get; set; }
    public string? AttributeValue { get; set; }
    public AttributeMatchType AttributeMatch { get; set; }
    public PseudoClassType? PseudoClass { get; set; }
    public PseudoElementType? PseudoElement { get; set; }

    public CssSelector? Parent { get; set; }
    public CombinatorType Combinator { get; set; }

    public (int a, int b, int c, int d) Specificity { get; set; }

    public static CssSelector Parse(string selector)
    {
        selector = selector.Trim();
        return ParseSelectorChain(selector);
    }

    private static CssSelector ParseSelectorChain(string selector)
    {
        var parts = new List<(CombinatorType, string)>();
        var current = "";
        CombinatorType currentComb = CombinatorType.Descendant;

        for (int i = 0; i < selector.Length; i++)
        {
            char c = selector[i];
            if (c == ' ' || c == '\t' || c == '\n')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    parts.Add((currentComb, current.Trim()));
                    currentComb = CombinatorType.Descendant;
                }
            }
            else if (c == '>') { parts.Add((currentComb, current.Trim())); currentComb = CombinatorType.Child; current = ""; }
            else if (c == '+') { parts.Add((currentComb, current.Trim())); currentComb = CombinatorType.AdjacentSibling; current = ""; }
            else if (c == '~') { parts.Add((currentComb, current.Trim())); currentComb = CombinatorType.GeneralSibling; current = ""; }
            else current += c;
        }
        if (!string.IsNullOrEmpty(current)) parts.Add((currentComb, current.Trim()));

        if (parts.Count == 0) return new CssSelector { Type = SelectorType.Universal };

        CssSelector? root = null;
        CssSelector? prev = null;

        foreach (var (comb, part) in parts)
        {
            var sel = ParseSimpleSelector(part);
            sel.Combinator = comb;

            if (root == null) root = sel;
            else prev!.Parent = sel;
            prev = sel;
        }

        // Calculate specificity for the entire selector chain
        if (root != null)
        {
            CalculateChainSpecificity(root);
        }

        return root ?? new CssSelector { Type = SelectorType.Universal };
    }

    private static void CalculateChainSpecificity(CssSelector selector)
    {
        var (a, b, c, d) = CalculateSimpleSpecificity(selector);
        selector.Specificity = (a, b, c, d);

        if (selector.Parent != null)
        {
            CalculateChainSpecificity(selector.Parent);
            var parentSpec = selector.Parent.Specificity;
            selector.Specificity = (a + parentSpec.a, b + parentSpec.b, c + parentSpec.c, d + parentSpec.d);
        }
    }

    private static (int a, int b, int c, int d) CalculateSimpleSpecificity(CssSelector selector)
    {
        int a = 0, b = 0, c = 0, d = 0;

        if (!string.IsNullOrEmpty(selector.Id))
            a++;

        b += selector.Classes.Count;

        if (!string.IsNullOrEmpty(selector.AttributeName))
            b++;

        if (selector.PseudoClass.HasValue)
            c++;

        if (selector.PseudoElement.HasValue)
            d++;

        if (selector.Type == SelectorType.Tag && !string.IsNullOrEmpty(selector.TagName))
            d++;
        else if (selector.Type == SelectorType.Universal)
            d++;

        return (a, b, c, d);
    }

    private static CssSelector ParseSimpleSelector(string selector)
    {
        var s = new CssSelector();

        if (selector == "*" || string.IsNullOrEmpty(selector))
        {
            s.Type = SelectorType.Universal;
            return s;
        }

        int i = 0;
        char first = selector[0];

        if (first == '#')
        {
            s.Type = SelectorType.Id;
            s.Id = selector[1..];
        }
        else if (first == '.')
        {
            s.Type = SelectorType.Class;
            s.Classes.Add(selector[1..]);
            i = 1;
        }
        else if (first == ':')
        {
            s.Type = SelectorType.PseudoClass;
            var pseudo = selector[1..].ToLowerInvariant();
            if (pseudo.StartsWith("before")) { s.PseudoElement = PseudoElementType.Before; s.PseudoClass = PseudoClassType.Before; }
            else if (pseudo.StartsWith("after")) { s.PseudoElement = PseudoElementType.After; s.PseudoClass = PseudoClassType.After; }
            else if (pseudo == "hover") s.PseudoClass = PseudoClassType.Hover;
            else if (pseudo == "active") s.PseudoClass = PseudoClassType.Active;
            else if (pseudo == "focus") s.PseudoClass = PseudoClassType.Focus;
            else if (pseudo == "first-child") s.PseudoClass = PseudoClassType.FirstChild;
            else if (pseudo == "last-child") s.PseudoClass = PseudoClassType.LastChild;
        }
        else if (first == '[')
        {
            s.Type = SelectorType.Attribute;
            var inner = selector[1..^1];
            var eqIdx = inner.IndexOf('=');
            if (eqIdx > 0)
            {
                s.AttributeName = inner[..eqIdx];
                s.AttributeValue = inner[(eqIdx + 1)..].Trim('"', '\'');
                int opIdx = eqIdx - 1;
                if (opIdx >= 0)
                {
                    char op = inner[opIdx];
                    s.AttributeMatch = op switch
                    {
                        '~' => AttributeMatchType.WhitespaceSeparated,
                        '^' => AttributeMatchType.StartsWith,
                        '$' => AttributeMatchType.EndsWith,
                        '*' => AttributeMatchType.Contains,
                        '|' => AttributeMatchType.DashSeparator,
                        _ => AttributeMatchType.Exact
                    };
                    if (op == '~' || op == '^' || op == '$' || op == '*' || op == '|')
                        s.AttributeName = inner[..opIdx];
                }
                else
                {
                    s.AttributeMatch = AttributeMatchType.Exact;
                }
            }
            else
            {
                s.AttributeName = inner;
                s.AttributeMatch = AttributeMatchType.Exact;
            }
        }
        else
        {
            s.Type = SelectorType.Tag;
            var bracketIdx = selector.IndexOf('[');
            if (bracketIdx > 0)
            {
                s.TagName = selector[..bracketIdx].ToLowerInvariant();
                var inner = selector[(bracketIdx + 1)..^1];
                var eqIdx = inner.IndexOf('=');
                if (eqIdx > 0)
                {
                    s.AttributeName = inner[..eqIdx];
                    s.AttributeValue = inner[(eqIdx + 1)..].Trim('"', '\'');
                    s.AttributeMatch = AttributeMatchType.Exact;
                }
                else
                {
                    s.AttributeName = inner;
                }
            }
            else
            {
                s.TagName = selector.ToLowerInvariant();
            }
        }

        // Parse remaining parts (classes, ids, pseudo)
        while (i < selector.Length)
        {
            char c = selector[i];
            if (c == '.')
            {
                int end = i + 1;
                while (end < selector.Length && (char.IsLetterOrDigit(selector[end]) || selector[end] == '-')) end++;
                s.Classes.Add(selector[(i + 1)..end]);
                i = end;
            }
            else if (c == '#')
            {
                int end = i + 1;
                while (end < selector.Length && (char.IsLetterOrDigit(selector[end]) || selector[end] == '-')) end++;
                s.Id = selector[(i + 1)..end];
                i = end;
            }
            else if (c == ':')
            {
                int end = i + 1;
                while (end < selector.Length && (char.IsLetterOrDigit(selector[end]) || selector[end] == '-')) end++;
                var pseudo = selector[(i + 1)..end].ToLowerInvariant();
                if (pseudo == "before") s.PseudoElement = PseudoElementType.Before;
                else if (pseudo == "after") s.PseudoElement = PseudoElementType.After;
                else if (Enum.TryParse<PseudoClassType>(pseudo, true, out var pc)) s.PseudoClass = pc;
                i = end;
            }
            else i++;
        }

        return s;
    }

    public bool Matches(Element element, Element? parent)
    {
        if (!MatchesSimple(this, element)) return false;

        if (Parent == null) return true;

        // For descendant combinator (space), check any ancestor
        var ancestor = parent;
        while (ancestor != null)
        {
            if (MatchesSimple(Parent, ancestor)) return true;
            if (Combinator == CombinatorType.Child) break;
            ancestor = ancestor.ParentElement;
        }

        // Check other combinators
        return CheckOtherCombinators(element);
    }

    private bool CheckOtherCombinators(Element element)
    {
        switch (Combinator)
        {
            case CombinatorType.Child:
                return Parent != null && element.ParentElement != null && MatchesSimple(Parent, element.ParentElement);
            case CombinatorType.AdjacentSibling:
                var prev = element.PreviousSibling;
                return prev is Element prevEl && Parent != null && MatchesSimple(Parent, prevEl);
            case CombinatorType.GeneralSibling:
                var siblings = element.Parent?.Children.OfType<Element>() ?? Enumerable.Empty<Element>();
                var before = siblings.TakeWhile(s => s != element);
                return before.Any(s => Parent != null && MatchesSimple(Parent, s));
            default:
                return true;
        }
    }

    private bool MatchesSimple(CssSelector selector, Element element)
    {
        return selector.Type switch
        {
            SelectorType.Universal => true,
            SelectorType.Tag => selector.TagName == null || element.TagName.Equals(selector.TagName, StringComparison.OrdinalIgnoreCase),
            SelectorType.Id => element.Id == selector.Id,
            SelectorType.Class => selector.Classes.All(c => element.HasClass(c)),
            SelectorType.Attribute => MatchAttribute(selector, element),
            SelectorType.PseudoClass => MatchesPseudoClass(selector, element),
            _ => true
        };
    }

    private bool MatchAttribute(CssSelector selector, Element element)
    {
        if (selector.AttributeName == null) return true;
        var attrValue = element.GetAttribute(selector.AttributeName);
        if (attrValue == null) return false;

        return selector.AttributeMatch switch
        {
            AttributeMatchType.Exact => attrValue == selector.AttributeValue,
            AttributeMatchType.WhitespaceSeparated => (selector.AttributeValue == null) || attrValue.Split(' ').Contains(selector.AttributeValue),
            AttributeMatchType.StartsWith => attrValue.StartsWith(selector.AttributeValue ?? ""),
            AttributeMatchType.EndsWith => attrValue.EndsWith(selector.AttributeValue ?? ""),
            AttributeMatchType.Contains => attrValue.Contains(selector.AttributeValue ?? ""),
            AttributeMatchType.DashSeparator => attrValue == selector.AttributeValue || attrValue.StartsWith((selector.AttributeValue ?? "") + "-"),
            _ => true
        };
    }

    private bool MatchesPseudoClass(CssSelector selector, Element element)
    {
        return selector.PseudoClass switch
        {
            PseudoClassType.FirstChild => element.ParentElement?.Children.OfType<Element>().FirstOrDefault() == element,
            PseudoClassType.LastChild => element.ParentElement?.Children.OfType<Element>().LastOrDefault() == element,
            _ => true
        };
    }
}

public enum SelectorType { Universal, Tag, Id, Class, Attribute, PseudoClass, PseudoElement }
public enum CombinatorType { Descendant, Child, AdjacentSibling, GeneralSibling }
public enum AttributeMatchType { Exact, WhitespaceSeparated, StartsWith, EndsWith, Contains, DashSeparator }
public enum PseudoClassType { Hover, Active, Focus, FirstChild, LastChild, FirstOfType, LastOfType, Before, After }
public enum PseudoElementType { Before, After, FirstLine, FirstLetter }