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

        // Strip string literals before brace matching to avoid matching braces inside strings
        var cleaned = new System.Text.StringBuilder(cssText.Length);
        bool inString = false;
        char stringChar = '\0';
        for (int i = 0; i < cssText.Length; i++)
        {
            char c = cssText[i];
            if (inString)
            {
                if (c == stringChar && (i == 0 || cssText[i - 1] != '\\'))
                    inString = false;
                continue;
            }
            if (c == '\'' || c == '"')
            {
                inString = true;
                stringChar = c;
                continue;
            }
            cleaned.Append(c);
        }
        cssText = cleaned.ToString();

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

            // Skip at-rules entirely (not supported yet)
            if (selectorPart.StartsWith("@")) continue;

            var selectors = ParseSelectors(selectorPart);
            var (properties, importantProps) = ParsePropertiesWithImportance(bodyPart);

            foreach (var selector in selectors)
            {
                var rule = new CssRule
                {
                    Selector = selector,
                    Specificity = CalculateSpecificity(selector),
                    Properties = new Dictionary<string, string>(properties),
                    ImportantProperties = new HashSet<string>(importantProps, StringComparer.OrdinalIgnoreCase)
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

    public (Dictionary<string, string> properties, HashSet<string> importantProps) ParsePropertiesWithImportance(string body)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var importantProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declarations = body.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var decl in declarations)
        {
            var match = PropertyRegex.Match(decl.Trim());
            if (match.Success)
            {
                var name = match.Groups[1].Value.ToLowerInvariant();
                var value = match.Groups[2].Value.Trim();
                if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                {
                    value = value[..^"!important".Length].Trim();
                    importantProps.Add(name);
                }
                properties[name] = value;
            }
        }

        return (properties, importantProps);
    }

    private Dictionary<string, string> ParseProperties(string body)
    {
        return ParsePropertiesWithImportance(body).properties;
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
                else { i++; }

                int nameStart = i;
                while (i < selector.Length && selector[i] != '(' && selector[i] != ' ') i++;
                string pseudoName = selector[nameStart..i].ToLowerInvariant();

                // :is(), :not(), :has() use the highest specificity of their arguments
                // :where() has zero specificity
                bool isZeroSpec = pseudoName == "where";
                bool isArgSpec = pseudoName == "is" || pseudoName == "not" || pseudoName == "has";

                if (!isZeroSpec && !isArgSpec)
                    c++;

                if (i < selector.Length && selector[i] == '(')
                {
                    int parenDepth = 1;
                    int argStart = i + 1;
                    i++;
                    while (i < selector.Length && parenDepth > 0)
                    {
                        if (selector[i] == '(') parenDepth++;
                        else if (selector[i] == ')') parenDepth--;
                        i++;
                    }
                    string args = selector[argStart..(i - 1)];

                    if (isArgSpec)
                    {
                        // Compute max specificity across all arguments
                        (int a, int b, int c, int d) argMax = (0, 0, 0, 0);
                        foreach (var arg in SplitSelectorsForSpecificity(args))
                        {
                            var argSpec = CalculateSpecificity(arg.Trim());
                            if (argSpec.a > argMax.a ||
                                (argSpec.a == argMax.a && argSpec.b > argMax.b) ||
                                (argSpec.a == argMax.a && argSpec.b == argMax.b && argSpec.c > argMax.c) ||
                                (argSpec.a == argMax.a && argSpec.b == argMax.b && argSpec.c == argMax.c && argSpec.d > argMax.d))
                            {
                                argMax = argSpec;
                            }
                        }
                        a += argMax.a;
                        b += argMax.b;
                        c += argMax.c;
                        d += argMax.d;
                    }
                    // :where() adds nothing
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

    private List<string> SplitSelectorsForSpecificity(string args)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
            {
                parts.Add(args[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < args.Length)
            parts.Add(args[start..].Trim());
        return parts;
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
    public HashSet<string> ImportantProperties { get; set; } = new();

    public bool IsPropertyImportant(string prop) => ImportantProperties.Contains(prop);
}

public class CssSelector
{
    public SelectorType Type { get; set; }
    public string? TagName { get; set; }
    public string? Namespace { get; set; }
    public string? Id { get; set; }
    public List<string> Classes { get; } = new();
    public string? AttributeName { get; set; }
    public string? AttributeValue { get; set; }
    public AttributeMatchType AttributeMatch { get; set; }
    public PseudoClassType? PseudoClass { get; set; }
    public string? PseudoClassArgument { get; set; }
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

        if (selector == "&")
        {
            s.Type = SelectorType.Nesting;
            return s;
        }

        int i = 0;
        char first = selector[0];

        if (first == '#')
        {
            s.Type = SelectorType.Id;
            s.Id = selector[1..];
            i = 1;
        }
        else if (first == '.')
        {
            s.Type = SelectorType.Class;
            s.Classes.Add(selector[1..]);
            i = 1;
        }
        else if (first == ':')
        {
            int doubleColon = selector.StartsWith("::") ? 1 : 0;
            int pseudoStart = doubleColon + 1;
            int parenIdx = selector.IndexOf('(');
            int pseudoEnd = parenIdx > 0 ? parenIdx : selector.Length;

            var pseudo = selector[pseudoStart..pseudoEnd].ToLowerInvariant();

            if (doubleColon > 0)
            {
                s.Type = SelectorType.PseudoElement;
                s.PseudoElement = ParsePseudoElementType(pseudo);
            }
            else
            {
                s.Type = SelectorType.PseudoClass;
                if (parenIdx > 0)
                {
                    var arg = selector[(parenIdx + 1)..^1].Trim();
                    s.PseudoClassArgument = arg;
                }
                s.PseudoClass = ParsePseudoClassType(pseudo);
            }
            i = selector.Length;
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
            i = selector.Length;
        }
        else
        {
            s.Type = SelectorType.Tag;
            var pipeIdx = selector.IndexOf('|');
            var bracketIdx = selector.IndexOf('[');

            if (pipeIdx > 0 && (bracketIdx < 0 || pipeIdx < bracketIdx))
            {
                s.Namespace = selector[..pipeIdx].ToLowerInvariant();
                var rest = selector[(pipeIdx + 1)..];
                if (rest == "*")
                {
                    s.Type = SelectorType.Universal;
                    s.TagName = null;
                }
                else
                {
                    s.TagName = rest.ToLowerInvariant();
                }
                i = selector.Length;
            }
            else if (bracketIdx > 0)
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
                int doubleColon = (i + 1 < selector.Length && selector[i + 1] == ':') ? 1 : 0;
                int pseudoStart = i + 1 + doubleColon;
                int parenIdx = selector.IndexOf('(', pseudoStart);
                int pseudoEnd = parenIdx > 0 ? parenIdx : selector.Length;

                var pseudo = selector[pseudoStart..pseudoEnd].ToLowerInvariant();

                if (doubleColon > 0)
                {
                    s.PseudoElement = ParsePseudoElementType(pseudo);
                }
                else
                {
                    if (parenIdx > 0)
                    {
                        var endParen = selector.IndexOf(')', parenIdx);
                        s.PseudoClassArgument = endParen > 0 ? selector[(parenIdx + 1)..endParen].Trim() : "";
                    }
                    s.PseudoClass = ParsePseudoClassType(pseudo);
                }
                var skipTo = selector.Length;
                if (parenIdx > 0)
                {
                    var endParen = selector.IndexOf(')', parenIdx);
                    if (endParen > 0) skipTo = endParen + 1;
                }
                else
                {
                    skipTo = pseudoEnd;
                }
                i = skipTo;
            }
            else i++;
        }

        return s;
    }

    private static PseudoClassType? ParsePseudoClassType(string name)
    {
        return name switch
        {
            "active" => PseudoClassType.Active,
            "any-link" => PseudoClassType.AnyLink,
            "autofill" => PseudoClassType.AutoFill,
            "before" => PseudoClassType.Before,
            "checked" => PseudoClassType.Checked,
            "default" => PseudoClassType.Default,
            "defined" => PseudoClassType.Defined,
            "disabled" => PseudoClassType.Disabled,
            "empty" => PseudoClassType.Empty,
            "enabled" => PseudoClassType.Enabled,
            "first" => PseudoClassType.First,
            "first-child" => PseudoClassType.FirstChild,
            "first-of-type" => PseudoClassType.FirstOfType,
            "focus" => PseudoClassType.Focus,
            "focus-visible" => PseudoClassType.FocusVisible,
            "focus-within" => PseudoClassType.FocusWithin,
            "fullscreen" => PseudoClassType.Fullscreen,
            "hover" => PseudoClassType.Hover,
            "in-range" => PseudoClassType.InRange,
            "indeterminate" => PseudoClassType.Indeterminate,
            "invalid" => PseudoClassType.Invalid,
            "last-child" => PseudoClassType.LastChild,
            "last-of-type" => PseudoClassType.LastOfType,
            "left" => PseudoClassType.Left,
            "link" => PseudoClassType.Link,
            "modal" => PseudoClassType.Modal,
            "only-child" => PseudoClassType.OnlyChild,
            "only-of-type" => PseudoClassType.OnlyOfType,
            "optional" => PseudoClassType.Optional,
            "out-of-range" => PseudoClassType.OutOfRange,
            "placeholder-shown" => PseudoClassType.PlaceholderShown,
            "popover-open" => PseudoClassType.PopoverOpen,
            "read-only" => PseudoClassType.ReadOnly,
            "read-write" => PseudoClassType.ReadWrite,
            "required" => PseudoClassType.Required,
            "right" => PseudoClassType.Right,
            "root" => PseudoClassType.Root,
            "scope" => PseudoClassType.Scope,
            "target" => PseudoClassType.Target,
            "user-invalid" => PseudoClassType.UserInvalid,
            "user-valid" => PseudoClassType.UserValid,
            "valid" => PseudoClassType.Valid,
            "visited" => PseudoClassType.Visited,
            "after" => PseudoClassType.After,
            "not" => PseudoClassType.Not,
            "nth-child" => PseudoClassType.NthChild,
            "nth-last-child" => PseudoClassType.NthLastChild,
            "nth-of-type" => PseudoClassType.NthOfType,
            "nth-last-of-type" => PseudoClassType.NthLastOfType,
            "is" => PseudoClassType.Is,
            "where" => PseudoClassType.Where,
            "has" => PseudoClassType.Has,
            "lang" => PseudoClassType.Lang,
            "dir" => PseudoClassType.Dir,
            "state" => PseudoClassType.State,
            _ => null
        };
    }

    private static PseudoElementType ParsePseudoElementType(string name)
    {
        return name switch
        {
            "before" => PseudoElementType.Before,
            "after" => PseudoElementType.After,
            "backdrop" => PseudoElementType.Backdrop,
            "file-selector-button" => PseudoElementType.FileSelectorButton,
            "first-letter" => PseudoElementType.FirstLetter,
            "first-line" => PseudoElementType.FirstLine,
            "grammar-error" => PseudoElementType.GrammarError,
            "marker" => PseudoElementType.Marker,
            "placeholder" => PseudoElementType.Placeholder,
            "selection" => PseudoElementType.Selection,
            "spelling-error" => PseudoElementType.SpellingError,
            "view-transition" => PseudoElementType.ViewTransition,
            "view-transition-group" => PseudoElementType.ViewTransitionGroup,
            "view-transition-image-pair" => PseudoElementType.ViewTransitionImagePair,
            "view-transition-new" => PseudoElementType.ViewTransitionNew,
            "view-transition-old" => PseudoElementType.ViewTransitionOld,
            _ => PseudoElementType.Before
        };
    }

    public bool Matches(Element element, Element? parent)
    {
        if (!MatchesSimple(this, element)) return false;

        if (Parent == null) return true;

        var ancestor = parent;
        while (ancestor != null)
        {
            if (MatchesSimple(Parent, ancestor)) return true;
            if (Combinator == CombinatorType.Child) break;
            ancestor = ancestor.ParentElement;
        }

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
            SelectorType.Nesting => true,
            SelectorType.Tag => MatchTag(selector, element),
            SelectorType.Id => element.Id == selector.Id,
            SelectorType.Class => selector.Classes.All(c => element.HasClass(c)),
            SelectorType.Attribute => MatchAttribute(selector, element),
            SelectorType.PseudoClass => MatchesPseudoClass(selector, element),
            SelectorType.PseudoElement => true,
            _ => true
        };
    }

    private bool MatchTag(CssSelector selector, Element element)
    {
        var tagMatch = selector.TagName == null || element.TagName.Equals(selector.TagName, StringComparison.OrdinalIgnoreCase);
        if (!tagMatch) return false;

        if (selector.Namespace != null)
        {
            var elNs = element.NamespaceUri ?? "";
            if (selector.Namespace == "*") return true;
            if (selector.Namespace == "ns")
                return elNs == "http://www.w3.org/2000/svg" || elNs == "http://www.w3.org/1998/Math/MathML";
            return elNs.EndsWith(selector.Namespace, StringComparison.OrdinalIgnoreCase);
        }
        return true;
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
        if (selector.PseudoClass == null) return true;

        return selector.PseudoClass switch
        {
            PseudoClassType.FirstChild => element.ParentElement?.Children.OfType<Element>().FirstOrDefault() == element,
            PseudoClassType.LastChild => element.ParentElement?.Children.OfType<Element>().LastOrDefault() == element,
            PseudoClassType.FirstOfType => IsFirstOfType(element),
            PseudoClassType.LastOfType => IsLastOfType(element),
            PseudoClassType.OnlyChild => element.ParentElement?.Children.OfType<Element>().Count() == 1,
            PseudoClassType.OnlyOfType => element.ParentElement?.Children.OfType<Element>().Count(e => e.TagName == element.TagName) == 1,
            PseudoClassType.NthChild => MatchesNth(selector.PseudoClassArgument, element, false),
            PseudoClassType.NthLastChild => MatchesNth(selector.PseudoClassArgument, element, true),
            PseudoClassType.NthOfType => MatchesNthOfType(selector.PseudoClassArgument, element, false),
            PseudoClassType.NthLastOfType => MatchesNthOfType(selector.PseudoClassArgument, element, true),
            PseudoClassType.Root => element.Parent is Document,
            PseudoClassType.Empty => element.Children.Count == 0 && string.IsNullOrEmpty(element.TextContent),
            PseudoClassType.Link => element.TagName == "A" && element.HasAttribute("href"),
            PseudoClassType.Visited => element.TagName == "A" && element.HasAttribute("href"),
            PseudoClassType.Active => element.IsFocused,
            PseudoClassType.Hover => element.IsFocused,
            PseudoClassType.Focus => element.IsFocused,
            PseudoClassType.FocusVisible => element.IsFocused,
            PseudoClassType.FocusWithin => IsFocusWithin(element),
            PseudoClassType.Enabled => !element.HasAttribute("disabled") && IsFormLike(element),
            PseudoClassType.Disabled => element.HasAttribute("disabled"),
            PseudoClassType.Checked => element.HasAttribute("checked") || element.HasAttribute("selected"),
            PseudoClassType.Required => element.HasAttribute("required"),
            PseudoClassType.Optional => !element.HasAttribute("required") && IsFormLike(element),
            PseudoClassType.Valid => true,
            PseudoClassType.Invalid => false,
            PseudoClassType.InRange => true,
            PseudoClassType.OutOfRange => false,
            PseudoClassType.UserValid => true,
            PseudoClassType.UserInvalid => false,
            PseudoClassType.Default => element.HasAttribute("checked") || element.HasAttribute("selected"),
            PseudoClassType.Indeterminate => false,
            PseudoClassType.PlaceholderShown => element.HasAttribute("placeholder") && string.IsNullOrEmpty(element.Value),
            PseudoClassType.ReadOnly => element.HasAttribute("readonly"),
            PseudoClassType.ReadWrite => !element.HasAttribute("readonly") && IsFormLike(element),
            PseudoClassType.Target => false,
            PseudoClassType.Scope => true,
            PseudoClassType.Defined => true,
            PseudoClassType.AnyLink => element.TagName == "A" && element.HasAttribute("href"),
            PseudoClassType.AutoFill => false,
            PseudoClassType.Modal => false,
            PseudoClassType.PopoverOpen => false,
            PseudoClassType.Fullscreen => false,
            PseudoClassType.Not => true,
            PseudoClassType.Is => true,
            PseudoClassType.Where => true,
            PseudoClassType.Has => true,
            PseudoClassType.Lang => MatchesLang(selector.PseudoClassArgument, element),
            PseudoClassType.Dir => MatchesDir(selector.PseudoClassArgument, element),
            PseudoClassType.State => false,
            PseudoClassType.First => false,
            PseudoClassType.Left => false,
            PseudoClassType.Right => false,
            PseudoClassType.Before => true,
            PseudoClassType.After => true,
            _ => true
        };
    }

    private static bool IsFormLike(Element element) => element.TagName is "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON" or "OPTION" or "DATALIST" or "METEr" or "PROGRESS";

    private static bool IsFirstOfType(Element element)
    {
        var siblings = element.ParentElement?.Children.OfType<Element>() ?? Enumerable.Empty<Element>();
        return siblings.FirstOrDefault(e => e.TagName == element.TagName) == element;
    }

    private static bool IsLastOfType(Element element)
    {
        var siblings = element.ParentElement?.Children.OfType<Element>() ?? Enumerable.Empty<Element>();
        return siblings.LastOrDefault(e => e.TagName == element.TagName) == element;
    }

    private static bool IsFocusWithin(Element element)
    {
        if (element.IsFocused) return true;
        return element.Children.OfType<Element>().Any(IsFocusWithin);
    }

    private static bool MatchesNth(string? argument, Element element, bool fromEnd)
    {
        if (string.IsNullOrEmpty(argument)) return false;

        int index;
        if (fromEnd)
        {
            var all = element.ParentElement?.Children.OfType<Element>().Reverse().ToList() ?? new List<Element>();
            index = all.IndexOf(element) + 1;
        }
        else
        {
            var all = element.ParentElement?.Children.OfType<Element>().ToList() ?? new List<Element>();
            index = all.IndexOf(element) + 1;
        }

        if (index <= 0) return false;

        var trimmed = argument.Trim().ToLowerInvariant();

        if (trimmed == "odd") return index % 2 == 1;
        if (trimmed == "even") return index % 2 == 0;

        var match = Regex.Match(trimmed, @"^\s*(?:([+-]?\d*)\s*[nN]\s*([+-]\s*\d+)?|([+-]?\d+))\s*$");
        if (!match.Success) return int.TryParse(trimmed, out var exact) && index == exact;

        if (!string.IsNullOrEmpty(match.Groups[3].Value))
            return index == int.Parse(match.Groups[3].Value);

        int a = string.IsNullOrEmpty(match.Groups[1].Value) ? 1 : (match.Groups[1].Value == "-" ? -1 : int.Parse(match.Groups[1].Value));
        int b = string.IsNullOrEmpty(match.Groups[2].Value) ? 0 : int.Parse(match.Groups[2].Value.Replace(" ", ""));

        if (a == 0) return index == b;

        var n = (index - b) / (double)a;
        return n >= 0 && Math.Abs(n - Math.Round(n)) < 0.0001;
    }

    private static bool MatchesNthOfType(string? argument, Element element, bool fromEnd)
    {
        if (string.IsNullOrEmpty(argument)) return false;

        int index;
        var sameType = element.ParentElement?.Children.OfType<Element>().Where(e => e.TagName == element.TagName).ToList() ?? new List<Element>();

        if (fromEnd)
        {
            sameType.Reverse();
        }

        index = sameType.IndexOf(element) + 1;
        if (index <= 0) return false;

        var trimmed = argument.Trim().ToLowerInvariant();

        if (trimmed == "odd") return index % 2 == 1;
        if (trimmed == "even") return index % 2 == 0;

        var match = Regex.Match(trimmed, @"^\s*(?:([+-]?\d*)\s*[nN]\s*([+-]\s*\d+)?|([+-]?\d+))\s*$");
        if (!match.Success) return int.TryParse(trimmed, out var exact) && index == exact;

        if (!string.IsNullOrEmpty(match.Groups[3].Value))
            return index == int.Parse(match.Groups[3].Value);

        int a = string.IsNullOrEmpty(match.Groups[1].Value) ? 1 : (match.Groups[1].Value == "-" ? -1 : int.Parse(match.Groups[1].Value));
        int b = string.IsNullOrEmpty(match.Groups[2].Value) ? 0 : int.Parse(match.Groups[2].Value.Replace(" ", ""));

        if (a == 0) return index == b;

        var n = (index - b) / (double)a;
        return n >= 0 && Math.Abs(n - Math.Round(n)) < 0.0001;
    }

    private static bool MatchesLang(string? argument, Element element)
    {
        if (string.IsNullOrEmpty(argument)) return false;
        var lang = element.GetAttribute("lang");
        if (lang == null) return false;
        return lang.Equals(argument, StringComparison.OrdinalIgnoreCase) ||
               lang.StartsWith(argument + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDir(string? argument, Element element)
    {
        if (string.IsNullOrEmpty(argument)) return false;
        var dir = element.GetAttribute("dir");
        if (dir == null) return false;
        return dir.Equals(argument, StringComparison.OrdinalIgnoreCase);
    }
}

public enum SelectorType { Universal, Tag, Id, Class, Attribute, PseudoClass, PseudoElement, Nesting }
public enum CombinatorType { Descendant, Child, AdjacentSibling, GeneralSibling }
public enum AttributeMatchType { Exact, WhitespaceSeparated, StartsWith, EndsWith, Contains, DashSeparator }
public enum PseudoClassType
{
    Hover, Active, Focus, FocusVisible, FocusWithin,
    FirstChild, LastChild, FirstOfType, LastOfType,
    OnlyChild, OnlyOfType,
    NthChild, NthLastChild, NthOfType, NthLastOfType,
    Root, Empty,
    Link, Visited, AnyLink,
    Enabled, Disabled, Checked, Required, Optional,
    Valid, Invalid, InRange, OutOfRange,
    UserValid, UserInvalid,
    Default, Indeterminate,
    PlaceholderShown, ReadOnly, ReadWrite,
    Target, Scope, Defined,
    Not, Is, Where, Has,
    Lang, Dir,
    Before, After,
    First, Left, Right,
    AutoFill, Modal, PopoverOpen, Fullscreen,
    State,
    // Legacy aliases
    FirstLine, FirstLetter
}
public enum PseudoElementType
{
    Before, After, Backdrop, FileSelectorButton,
    FirstLetter, FirstLine, GrammarError,
    Marker, Placeholder, Selection, SpellingError,
    ViewTransition, ViewTransitionGroup, ViewTransitionImagePair,
    ViewTransitionNew, ViewTransitionOld
}
