using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;

namespace UpBrowser.Core.Layout;

public class LayoutWordBreak : LayoutText
{
    public LayoutWordBreak(HtmlElement node) : base(node, "\u200B")
    {
    }

    public Position? PositionForCaretOffset(uint offset) => null;

    public uint? CaretOffsetForPosition(Position? position) => null;

    public override string GetName() => "LayoutWordBreak";

    public override bool IsWordBreak => true;
}