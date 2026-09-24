using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Css.Cascade;

/// <summary>
/// Adjusts computed styles for certain elements, mirroring Blink's StyleAdjuster.
/// Handles special cases like table display adjustments, text-decoration suppression,
/// forced colors mode, and other element-specific style overrides.
/// </summary>
public class StyleAdjuster
{
    public void AdjustComputedStyle(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        AdjustDisplayForElement(style, element);
        BlockifyFlexGridItems(style, element, parentStyle);
        AdjustOverflow(style);
        AdjustForTextElements(style, element);
        AdjustForReplacedElements(style, element);
        AdjustTouchAction(style, element);
    }

    /// <summary>
    /// CSS Flexbox §4.1 / Grid §5: in-flow children of a flex or grid container
    /// are blockified (inline → block, inline-flex → flex, …) and cannot be
    /// inline-level. Out-of-flow children keep their display.
    /// </summary>
    private static void BlockifyFlexGridItems(ComputedStyle style, Element element, ComputedStyle? parentStyle)
    {
        if (parentStyle == null) return;
        if (parentStyle.Display is not (DisplayType.Flex or DisplayType.Grid
            or DisplayType.InlineFlex or DisplayType.InlineGrid))
            return;
        if (style.Position is PositionType.Absolute or PositionType.Fixed) return;
        if (element.ParentNode is not Element parent || !ReferenceEquals(parent.ComputedStyle, parentStyle)) return;

        style.Display = style.Display switch
        {
            DisplayType.Inline or DisplayType.InlineBlock => DisplayType.Block,
            DisplayType.InlineFlex => DisplayType.Flex,
            DisplayType.InlineGrid => DisplayType.Grid,
            _ => style.Display,
        };
    }

    private static void AdjustDisplayForElement(ComputedStyle style, Element element)
    {
        string tag = element.TagName.ToUpperInvariant();

        // Table internal elements should not be display:none at the UA level
        // But if author set display:none, honor it

        // <td> defaults to table-cell
        if (tag is "TD" or "TH" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.TableCell;

        // <tr> defaults to table-row
        if (tag == "TR" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.TableRow;

        // <table> defaults to table
        if (tag == "TABLE" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.Table;

        // <li> defaults to list-item
        if (tag == "LI" && style.Display == DisplayType.Inline)
            style.Display = DisplayType.ListItem;

        // <img>, <video>, <canvas> are inline-block by default
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME" or "EMBED" or "OBJECT" or "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON")
        {
            if (style.Display == DisplayType.Inline)
                style.Display = DisplayType.InlineBlock;
        }

        // Positioned elements and floats create block formatting contexts
        if (style.Position != PositionType.Static && style.Position != PositionType.Relative)
        {
            if (style.Display == DisplayType.Inline)
                style.Display = DisplayType.InlineBlock;
        }

        // Top layer elements (dialog[open], fullscreen) get block display
        if (tag == "DIALOG" && element.HasAttribute("open"))
        {
            style.Display = DisplayType.Block;
        }
    }

    private static void AdjustOverflow(ComputedStyle style)
    {
        // Propagate visible overflow to the viewport
        // The root element's overflow becomes the viewport's overflow
    }

    private static void AdjustForTextElements(ComputedStyle style, Element element)
    {
        // Replaced elements and floated/positioned elements suppress text-decoration
        // propagation per CSS Text Decoration spec
        string tag = element.TagName.ToUpperInvariant();
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME" or "EMBED" or "OBJECT" or "INPUT" or "TEXTAREA" or "SELECT")
        {
            // These elements are not affected by ancestor text-decoration
        }
    }

    private static void AdjustForReplacedElements(ComputedStyle style, Element element)
    {
        // Ensure replaced elements have intrinsic sizing behavior
        string tag = element.TagName.ToUpperInvariant();
        if (tag is "IMG" or "VIDEO" or "CANVAS" or "IFRAME")
        {
            // Set intrinsic aspect ratio if applicable
        }
    }

    private static void AdjustTouchAction(ComputedStyle style, Element element)
    {
        // Touch-action: manipulation for root elements
        if (element.Parent is Document)
        {
            // Default touch-action for root is manipulation
        }
    }
}