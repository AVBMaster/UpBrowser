using System.Collections;
using DomElement = UpBrowser.Core.Dom.Element;

namespace UpBrowser.Core.JavaScript;

public class JsHtmlCollection : IEnumerable<object>
{
    private readonly DomElement _root;
    private readonly string? _tagName;
    private readonly string? _className;
    private readonly string? _name;

    public JsHtmlCollection(DomElement root, string? tagName = null, string? className = null, string? name = null)
    {
        _root = root;
        _tagName = tagName;
        _className = className;
        _name = name;
    }

    public int length => GetFilteredElements().Count;
    public int Length => GetFilteredElements().Count;

    public object? this[int index] => GetItem(index);

    public object? GetItem(int index)
    {
        var elements = GetFilteredElements();
        return index >= 0 && index < elements.Count ? elements[index] : null;
    }

    public object? NamedItem(string name)
    {
        var elements = GetFilteredElements();
        return elements.FirstOrDefault(e =>
        {
            if (e is ElementHost el)
            {
                return el.id == name || el.getAttribute("name") == name;
            }
            return false;
        });
    }

    public IEnumerator<object> GetEnumerator() => GetFilteredElements().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public object?[] ToArray() => GetFilteredElements().ToArray();

    private List<object> GetFilteredElements()
    {
        var elements = new List<object>();
        if (_className != null)
            CollectByClassName(_root, _className, elements);
        else if (_tagName != null)
            CollectByTagName(_root, _tagName, elements);
        else if (_name != null)
            CollectByName(_root, _name, elements);
        else
            CollectAllChildren(_root, elements);
        return elements;
    }

    private void CollectByTagName(DomElement root, string tagName, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase) ||
                tagName == "*")
                result.Add(new ElementHost(child));
            CollectByTagName(child, tagName, result);
        }
    }

    private void CollectByClassName(DomElement root, string className, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.HasClass(className))
                result.Add(new ElementHost(child));
            CollectByClassName(child, className, result);
        }
    }

    private void CollectByName(DomElement root, string name, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            if (child.GetAttribute("name") == name)
                result.Add(new ElementHost(child));
            CollectByName(child, name, result);
        }
    }

    private void CollectAllChildren(DomElement root, List<object> result)
    {
        foreach (var child in root.Children.OfType<DomElement>())
        {
            result.Add(new ElementHost(child));
            CollectAllChildren(child, result);
        }
    }
}

public class JsNodeList : IEnumerable<object>
{
    private readonly DomElement _root;
    private readonly Func<DomElement, List<DomElement>>? _filter;
    private List<DomElement>? _cached;

    public JsNodeList(DomElement root, Func<DomElement, List<DomElement>>? filter = null)
    {
        _root = root;
        _filter = filter;
    }

    public int length => GetNodes().Count;
    public int Length => GetNodes().Count;

    public object? this[int index] => GetItem(index);

    public object? GetItem(int index)
    {
        var nodes = GetNodes();
        return index >= 0 && index < nodes.Count ? WrapNode(nodes[index]) : null;
    }

    public IEnumerator<object> GetEnumerator() => GetNodes().Select(WrapNode).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public object?[] ToArray() => GetNodes().Select(WrapNode).ToArray();

    private List<DomElement> GetNodes()
    {
        if (_filter != null)
            return _filter(_root);
        return _root.Children.OfType<DomElement>().ToList();
    }

    private static object WrapNode(DomElement el) => new ElementHost(el);
}
