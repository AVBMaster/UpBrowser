using UpBrowser.Core.Dom;
using UpBrowser.Core.Dom.Html;
using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

public class LayoutEmbeddedObject : LayoutEmbeddedContent
{
    public enum PluginAvailability
    {
        Available,
        Missing,
        BlockedByContentSecurityPolicy
    }

    private PluginAvailability _pluginAvailability = PluginAvailability.Available;
    private string _unavailablePluginReplacementText = string.Empty;

    public LayoutEmbeddedObject(HtmlElement? element) : base(element)
    {
    }

    public void SetPluginAvailability(PluginAvailability availability)
    {
        _pluginAvailability = availability;
    }

    public bool ShowsUnavailablePluginIndicator() => _pluginAvailability != PluginAvailability.Available;

    public override string GetName() => "LayoutEmbeddedObject";

    public string UnavailablePluginReplacementText => _unavailablePluginReplacementText;

    public override void PaintReplaced(PaintInfo? paintInfo, PhysicalOffset paintOffset)
    {
    }

    public override void UpdateAfterLayout()
    {
    }

    public override bool IsEmbeddedObject => true;

    public override void ComputeIntrinsicSizingInfo(IntrinsicSizingInfo? info)
    {
    }
}