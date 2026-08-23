using UpBrowser.Core.Css.ElementStyles;
using UpBrowser.Core.Css.Matcher;
using UpBrowser.Core.Css.Properties;
using UpBrowser.Core.Css.Rules;
using UpBrowser.Core.Css.Tokenizer;
using UpBrowser.Core.Css.Values;
using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.Cascade;

/// <summary>
/// Resolves ComputedStyle for elements based on the document's stylesheets.
/// Mirrors Blink's StyleResolver: collects matching rules, applies the cascade,
/// then adjusts the resulting style.
/// </summary>
public class StyleResolver
{
    private readonly StyleSheetContents? _authorSheet;
    private readonly CssPropertyValueSet _inlineStyle = new();
    private readonly SelectorChecker _checker = new();
    private readonly StyleAdjuster _adjuster = new();
    private int _treeOrderCounter;
    private float _viewportWidth = 1024;
    private float _viewportHeight = 768;
    private string _colorScheme = "light";

    public StyleResolver(StyleSheetContents? authorSheet = null)
    {
        _authorSheet = authorSheet;
    }

    public void SetViewport(float width, float height, string colorScheme = "light")
    {
        _viewportWidth = width;
        _viewportHeight = height;
        _colorScheme = colorScheme;
    }

    /// <summary>
    /// Resolve the computed style for a single element, given its parent's computed style.
    /// </summary>
    public ComputedStyle? ResolveStyle(Element element, ComputedStyle? parentStyle)
    {
        var style = new ComputedStyle();
        if (parentStyle != null)
            InheritProperties(style, parentStyle);

        var state = new CascadeResolverState
        {
            Element = element,
            ParentStyle = parentStyle,
            ViewportWidth = _viewportWidth,
            ViewportHeight = _viewportHeight,
            RootFontSize = parentStyle?.FontSize ?? 16,
            ColorScheme = _colorScheme,
        };
        element.ComputedStyle = style;

        var cascade = new StyleCascade(state);

        // 1. UA styles
        MatchUAStyles(element, state);

        // 2. Author styles
        if (_authorSheet != null)
            MatchAuthorStyles(element, state);

        // 3. Inline style
        MatchInlineStyle(element, state);

        // 4. Apply the cascade
        cascade.Apply();

        // 5. Adjust the computed style
        _adjuster.AdjustComputedStyle(style, element, parentStyle);

        return style;
    }

    public void ResolveDocument(Document document)
    {
        _treeOrderCounter = 0;
        var root = document.DocumentElement ?? document.Body;
        if (root == null) return;

        ComputedStyle? parent = null;
        ResolveTree(root, parent);
    }

    private void ResolveTree(Element element, ComputedStyle? parent)
    {
        var style = ResolveStyle(element, parent);
        foreach (var child in element.Children)
        {
            if (child is Element childEl)
                ResolveTree(childEl, style);
        }
    }

    private void MatchUAStyles(Element element, CascadeResolverState state)
    {
        // Apply UA default styles from the UA stylesheet registry
        var uaStyle = new ComputedStyle();
        ElementStyleRegistry.ApplyUserAgentStyle(uaStyle, element.TagName, element);

        var props = new CssPropertyValueSet(CssParserMode.UACSS);
        CollectNonDefaultProperties(uaStyle, props);

        if (props.PropertyCount > 0)
        {
            state.MatchedRules.Add(new MatchedRuleEntry
            {
                Properties = props,
                Priority = new CascadePriority(CascadeOrigin.UserAgent, 0, _treeOrderCounter++, 0, false)
            });
        }
    }

    private void MatchAuthorStyles(Element element, CascadeResolverState state)
    {
        int treeOrder = _treeOrderCounter++;
        foreach (var rule in _authorSheet!.ChildRules)
        {
            if (rule is StyleRule styleRule)
            {
                if (MatchesAnySelector(styleRule, element))
                {
                    state.MatchedRules.Add(new MatchedRuleEntry
                    {
                        Properties = styleRule.Properties,
                        Priority = new CascadePriority(CascadeOrigin.Author, 0, treeOrder, 0, false)
                    });
                }
            }
            else if (rule is StyleRuleMedia media)
            {
                if (MediaQueryEvaluator.Evaluate(media.ConditionText, _viewportWidth, _viewportHeight, _colorScheme))
                {
                    MatchGroupRules(media.ChildRules, element, state, treeOrder);
                }
            }
            else if (rule is StyleRuleSupports supports)
            {
                if (EvaluateSupportsCondition(supports.ConditionText))
                {
                    MatchGroupRules(supports.ChildRules, element, state, treeOrder);
                }
            }
            else if (rule is StyleRuleLayerBlock layer)
            {
                MatchGroupRules(layer.ChildRules, element, state, treeOrder);
            }
            else if (rule is StyleRuleStartingStyle starting)
            {
                MatchGroupRules(starting.ChildRules, element, state, treeOrder);
            }
        }
    }

    private void MatchGroupRules(List<StyleRuleBase> rules, Element element, CascadeResolverState state, int treeOrder)
    {
        foreach (var rule in rules)
        {
            if (rule is StyleRule styleRule && MatchesAnySelector(styleRule, element))
            {
                state.MatchedRules.Add(new MatchedRuleEntry
                {
                    Properties = styleRule.Properties,
                    Priority = new CascadePriority(CascadeOrigin.Author, 0, treeOrder, 0, false)
                });
            }
        }
    }

    private bool MatchesAnySelector(StyleRule rule, Element element)
    {
        foreach (var selector in rule.Selectors)
        {
            if (_checker.Match(selector, element))
                return true;
        }
        return false;
    }

    private void MatchInlineStyle(Element element, CascadeResolverState state)
    {
        var inlineText = element.GetAttribute("style");
        if (string.IsNullOrWhiteSpace(inlineText)) return;

        var parser = new Tokenizer.CssParserImpl(CssParserContext.Default());
        var rules = Tokenizer.CssParserImpl.ParseRuleList(inlineText, CssParserContext.Default());
        var props = new CssPropertyValueSet();
        foreach (var rule in rules)
        {
            if (rule is StyleRule sr)
            {
                foreach (var p in sr.Properties.Properties)
                    props.SetLonghandProperty(p);
            }
        }

        if (props.PropertyCount > 0)
        {
            state.MatchedRules.Add(new MatchedRuleEntry
            {
                Properties = props,
                Priority = new CascadePriority(CascadeOrigin.Author, 0, int.MaxValue, 0, false)
            });
        }
    }

    private static bool EvaluateSupportsCondition(string condition) => true;

    private static void InheritProperties(ComputedStyle style, ComputedStyle parent)
    {
        style.Color = parent.Color;
        style.FontFamily = parent.FontFamily;
        style.FontSize = parent.FontSize;
        style.FontWeight = parent.FontWeight;
        style.FontStyle = parent.FontStyle;
        style.LineHeight = parent.LineHeight;
        // 'line-height' inherits its computed value, so the 'normal' flag and any
        // absolute length must travel with the multiplier.
        style.LineHeightIsNormal = parent.LineHeightIsNormal;
        style.LineHeightPx = parent.LineHeightPx;
        style.TextAlign = parent.TextAlign;
        style.Visibility = parent.Visibility;
        style.WhiteSpace = parent.WhiteSpace;
        style.Direction = parent.Direction;
        style.TextTransform = parent.TextTransform;
        style.LetterSpacing = parent.LetterSpacing;
        style.WordSpacing = parent.WordSpacing;
        style.TextIndent = parent.TextIndent;
        style.Cursor = parent.Cursor;
        style.ListStyleType = parent.ListStyleType;
        style.ListStylePosition = parent.ListStylePosition;
        style.WordBreak = parent.WordBreak;
        style.OverflowWrap = parent.OverflowWrap;
        style.FontVariant = parent.FontVariant;
        style.FontKerning = parent.FontKerning;
        style.FontStretch = parent.FontStretch;
    }

    private static void CollectNonDefaultProperties(ComputedStyle source, CssPropertyValueSet target)
    {
        DumpStyleProperties(source, target);
    }

    private static void DumpStyleProperties(ComputedStyle s, CssPropertyValueSet target)
    {
        target.SetProperty(CssPropertyId.Color, new CssColorValue(s.Color));
        target.SetProperty(CssPropertyId.FontSize, CssNumericLiteralValue.Create(s.FontSize, CssUnitType.Pixels));
        target.SetProperty(CssPropertyId.FontWeight, CssIdentifierValue.Create(s.FontWeight == Dom.FontWeight.Bold ? CssValueId.Bold : CssValueId.Normal));
        target.SetProperty(CssPropertyId.FontFamily, new CssStringValue(s.FontFamily));
        target.SetProperty(CssPropertyId.FontStyle, CssIdentifierValue.Create(s.FontStyle switch
        {
            Dom.FontStyleType.Italic => CssValueId.Italic,
            Dom.FontStyleType.Oblique => CssValueId.Oblique,
            _ => CssValueId.Normal
        }));
        target.SetProperty(CssPropertyId.LineHeight, CssNumericLiteralValue.Create(s.LineHeight, CssUnitType.Number));
        target.SetProperty(CssPropertyId.Display, CssIdentifierValue.Create(DisplayToId(s.Display)));
        target.SetProperty(CssPropertyId.Position, CssIdentifierValue.Create(PositionToId(s.Position)));
        target.SetProperty(CssPropertyId.WhiteSpace, CssIdentifierValue.Create(WhiteSpaceToId(s.WhiteSpace)));
        target.SetProperty(CssPropertyId.TextAlign, CssIdentifierValue.Create(TextAlignToId(s.TextAlign)));
    }

    private static CssValueId DisplayToId(Dom.DisplayType d) => d switch
    {
        Dom.DisplayType.None => CssValueId.None,
        Dom.DisplayType.Block => CssValueId.Block,
        Dom.DisplayType.Inline => CssValueId.Inline,
        Dom.DisplayType.InlineBlock => CssValueId.InlineBlock,
        Dom.DisplayType.Flex => CssValueId.Flex,
        Dom.DisplayType.InlineFlex => CssValueId.InlineFlex,
        Dom.DisplayType.Grid => CssValueId.Grid,
        Dom.DisplayType.InlineGrid => CssValueId.InlineGrid,
        Dom.DisplayType.Table => CssValueId.Table,
        Dom.DisplayType.ListItem => CssValueId.Block,
        _ => CssValueId.Block
    };

    private static CssValueId PositionToId(Dom.PositionType p) => p switch
    {
        Dom.PositionType.Relative => CssValueId.Relative,
        Dom.PositionType.Absolute => CssValueId.Absolute,
        Dom.PositionType.Fixed => CssValueId.Fixed,
        Dom.PositionType.Sticky => CssValueId.Sticky,
        _ => CssValueId.Static
    };

    private static CssValueId WhiteSpaceToId(Dom.WhiteSpaceMode w) => w switch
    {
        Dom.WhiteSpaceMode.Pre => CssValueId.Pre,
        Dom.WhiteSpaceMode.PreWrap => CssValueId.PreWrap,
        Dom.WhiteSpaceMode.PreLine => CssValueId.PreLine,
        Dom.WhiteSpaceMode.Nowrap => CssValueId.Nowrap,
        _ => CssValueId.Normal
    };

    private static CssValueId TextAlignToId(Dom.TextAlignType t) => t switch
    {
        Dom.TextAlignType.Left => CssValueId.Left,
        Dom.TextAlignType.Right => CssValueId.Right,
        Dom.TextAlignType.Center => CssValueId.Center,
        Dom.TextAlignType.Justify => CssValueId.Justify,
        Dom.TextAlignType.End => CssValueId.End,
        _ => CssValueId.Start
    };
}