using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;

namespace UpBrowser.Core.Layout;

public class LayoutIFrame : LayoutEmbeddedContent
{
    public LayoutIFrame(HTMLIFrameElement? element) : base(element)
    {
    }

    public override string GetName() => "LayoutIFrame";

    public override bool IsLayoutIFrame => true;
}