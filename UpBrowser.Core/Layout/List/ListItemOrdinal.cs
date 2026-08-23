using UpBrowser.Core.Dom;

namespace UpBrowser.Core.Layout.List;

public class ListItemOrdinal
{
    private int? _explicitValue;
    private int _cachedValue = 1;
    private bool _dirty = true;

    public int Value(Node node)
    {
        if (_dirty)
        {
            RecalcValue(node);
            _dirty = false;
        }
        return _cachedValue;
    }

    public void SetExplicit(int value)
    {
        _explicitValue = value;
        _dirty = true;
    }

    public void Reset()
    {
        _explicitValue = null;
        _dirty = true;
    }

    public static void ItemInsertedOrRemoved(LayoutObject item)
    {
        var parent = item.Parent;
        if (parent == null) return;
        MarkDirty(parent);
        foreach (var child in parent.Children)
            MarkDirty(child);
    }

    private static void MarkDirty(LayoutObject obj)
    {
        if (obj is LayoutListItem li)
            li.OrdinalDirty = true;
        else if (obj is LayoutInlineListItem inlineLi)
            inlineLi.OrdinalDirty = true;
    }

    internal void SetDirty() => _dirty = true;

    private void RecalcValue(Node node)
    {
        if (_explicitValue.HasValue)
        {
            _cachedValue = _explicitValue.Value;
            return;
        }

        int index = 1;
        var parent = (node as Element)?.ParentElement;
        if (parent == null) { _cachedValue = 1; return; }

        bool foundSelf = false;
        foreach (var child in parent.Children)
        {
            if (child == node) { foundSelf = true; break; }
            if (child is Element el && el.ComputedStyle?.Display == DisplayType.ListItem)
                index++;
        }
        _cachedValue = foundSelf ? index : 1;
    }
}