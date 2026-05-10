using UpBrowser.Core.Dom;
using Microsoft.ClearScript;

namespace UpBrowser.Core.JavaScript;

public class ElementHost
{
    private readonly Element _element;
    private StyleHost? _styleHost;
    private readonly Dictionary<string, List<ScriptObject>> _eventListeners = new();

    public ElementHost(Element element)
    {
        _element = element;
    }

    public Element NativeElement => _element;

    public string id
    {
        get => _element.Id ?? "";
        set => _element.Id = value;
    }

    public string className
    {
        get => _element.ClassName ?? "";
        set => _element.ClassName = value;
    }

    public string tagName => _element.TagName;

    public string nodeName => _element.NodeName;

    public string? textContent
    {
        get => GetTextContent();
        set
        {
            _element.Children.Clear();
            if (!string.IsNullOrEmpty(value))
                _element.AppendChild(new TextNode(value));
        }
    }

    public string? innerHTML
    {
        get => BuildInnerHtml();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _element.Children.Clear();
                return;
            }
            var parsed = HtmlParser.ParseFragment(value, _element.TagName);
            _element.Children.Clear();
            foreach (var child in parsed)
                _element.AppendChild(child);
        }
    }

    public string? outerHTML => BuildOuterHtml();

    public StyleHost style => _styleHost ??= new StyleHost(_element);

    public string? value
    {
        get => _element.Value;
        set => _element.Value = value;
    }

    public int selectionStart
    {
        get => _element.SelectionStart;
        set => _element.SelectionStart = value;
    }

    public int selectionEnd
    {
        get => _element.SelectionEnd;
        set => _element.SelectionEnd = value;
    }

    public ElementHost? parentElement
    {
        get
        {
            var parent = _element.ParentElement;
            return parent != null ? new ElementHost(parent) : null;
        }
    }

    public ElementHost? parentNode => parentElement;

    public object? previousElementSibling
    {
        get
        {
            var parent = _element.ParentElement;
            if (parent == null) return null;
            int idx = parent.Children.IndexOf(_element);
            for (int i = idx - 1; i >= 0; i--)
            {
                if (parent.Children[i] is Element e)
                    return new ElementHost(e);
            }
            return null;
        }
    }

    public object? nextElementSibling
    {
        get
        {
            var parent = _element.ParentElement;
            if (parent == null) return null;
            int idx = parent.Children.IndexOf(_element);
            for (int i = idx + 1; i < parent.Children.Count; i++)
            {
                if (parent.Children[i] is Element e)
                    return new ElementHost(e);
            }
            return null;
        }
    }

    public object? firstChild
    {
        get
        {
            var first = _element.Children.FirstOrDefault();
            return first != null ? WrapNode(first) : null;
        }
    }

    public object? lastChild
    {
        get
        {
            var last = _element.Children.LastOrDefault();
            return last != null ? WrapNode(last) : null;
        }
    }

    public object[] children =>
        _element.Children.OfType<Element>().Select(e => (object)new ElementHost(e)).ToArray();

    public int childElementCount => _element.Children.OfType<Element>().Count();

    public object[] childNodes =>
        _element.Children.Select(WrapNode).ToArray();

    public string? getAttribute(string name) => _element.GetAttribute(name);

    public void setAttribute(string name, string value) => _element.SetAttribute(name, value);

    public void removeAttribute(string name) => _element.RemoveAttribute(name);

    public bool hasAttribute(string name) => _element.HasAttribute(name);

    public bool hasChildNodes() => _element.Children.Count > 0;

    public object? closest(string selector)
    {
        var el = _element;
        while (el != null)
        {
            if (MatchesSelector(el, selector))
                return new ElementHost(el);
            el = el.ParentElement;
        }
        return null;
    }

    public object? querySelector(string selector)
    {
        var result = QuerySelectorAllInternal(_element, selector).FirstOrDefault();
        return result != null ? new ElementHost(result) : null;
    }

    public object[] querySelectorAll(string selector) =>
        QuerySelectorAllInternal(_element, selector).Select(e => (object)new ElementHost(e)).ToArray();

    public object[] getElementsByTagName(string tagName) =>
        GetElementsByTagNameInternal(_element, tagName).Select(e => (object)new ElementHost(e)).ToArray();

    public object[] getElementsByClassName(string className) =>
        GetElementsByClassNameInternal(_element, className).Select(e => (object)new ElementHost(e)).ToArray();

    public void appendChild(object child)
    {
        var host = child as ElementHost;
        if (host == null) return;
        _element.AppendChild(host.NativeElement);
    }

    public void insertBefore(object newChild, object? refChild)
    {
        var newHost = newChild as ElementHost;
        if (newHost == null) return;
        Element? refEl = null;
        if (refChild is ElementHost refHost)
            refEl = refHost.NativeElement;
        _element.InsertBefore(newHost.NativeElement, refEl);
    }

    public void removeChild(object child)
    {
        var host = child as ElementHost;
        if (host == null) return;
        _element.RemoveChild(host.NativeElement);
    }

    public void replaceChild(object newChild, object oldChild)
    {
        var newHost = newChild as ElementHost;
        var oldHost = oldChild as ElementHost;
        if (newHost == null || oldHost == null) return;
        int idx = _element.Children.IndexOf(oldHost.NativeElement);
        if (idx >= 0)
        {
            _element.Children[idx] = newHost.NativeElement;
            newHost.NativeElement.Parent = _element;
            oldHost.NativeElement.Parent = null;
        }
    }

    public object cloneNode(bool deep)
    {
        var cloned = CloneElement(_element, deep);
        return new ElementHost(cloned);
    }

    public bool contains(object? other)
    {
        if (other is ElementHost host)
            return ContainsElement(_element, host.NativeElement);
        return false;
    }

    public void addEventListener(string type, ScriptObject callback)
    {
        if (!_eventListeners.TryGetValue(type, out var list))
        {
            list = new List<ScriptObject>();
            _eventListeners[type] = list;
        }
        if (!list.Contains(callback))
            list.Add(callback);
    }

    public void removeEventListener(string type, ScriptObject callback)
    {
        if (_eventListeners.TryGetValue(type, out var list))
            list.Remove(callback);
    }

    public void click()
    {
        DispatchEvent(new ScriptEvent("click", this));
    }

    public void focus()
    {
        DispatchEvent(new ScriptEvent("focus", this));
    }

    public void blur()
    {
        DispatchEvent(new ScriptEvent("blur", this));
    }

    public void scrollIntoView(bool alignToTop = true)
    {
    }

    public string? getBoundingClientRect()
    {
        var box = _element.LayoutBox;
        if (box == null) return null;
        return $"{{\"top\":{box.BorderBox.Top},\"right\":{box.BorderBox.Right},\"bottom\":{box.BorderBox.Bottom},\"left\":{box.BorderBox.Left},\"width\":{box.BorderBox.Width},\"height\":{box.BorderBox.Height}}}";
    }

    public object[] classListValues => _element.ClassList.Select(c => (object)c).ToArray();

    public bool classList_contains(string className) => _element.HasClass(className);

    public void classList_add(string className)
    {
        var current = _element.ClassName ?? "";
        var parts = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!parts.Contains(className))
        {
            parts.Add(className);
            _element.ClassName = string.Join(" ", parts);
        }
    }

    public void classList_remove(string className)
    {
        var current = _element.ClassName ?? "";
        var parts = current.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        parts.Remove(className);
        _element.ClassName = string.Join(" ", parts);
    }

    public bool classList_toggle(string className)
    {
        if (_element.HasClass(className))
        {
            classList_remove(className);
            return false;
        }
        classList_add(className);
        return true;
    }

    public string? getAttributeNS(string ns, string name) => _element.GetAttribute(name);

    public void setAttributeNS(string ns, string name, string value) => _element.SetAttribute(name, value);
    public void removeAttributeNS(string ns, string name) => _element.RemoveAttribute(name);

    public object? dispatchEvent(ScriptEvent evt)
    {
        DispatchEvent(evt);
        return !evt.DefaultPrevented;
    }

    // Internal event dispatching (called from InputHandler)
    internal bool DispatchEvent(ScriptEvent evt)
    {
        if (string.IsNullOrEmpty(evt.type)) return true;

        if (_eventListeners.TryGetValue(evt.type, out var list))
        {
            foreach (var cb in list.ToList())
            {
                try { cb.Invoke(false, evt); }
                catch { }
            }
        }

        var attrName = "on" + evt.type;
        var attr = _element.GetAttribute(attrName);
        if (!string.IsNullOrEmpty(attr))
        {
            var engine = JavaScriptEngine.Current;
            if (engine != null)
            {
                try { engine.Evaluate(attr); }
                catch { }
            }
        }

        return !evt.DefaultPrevented;
    }

    internal IEnumerable<ElementHost> GetChildHosts() =>
        _element.Children.OfType<Element>().Select(e => new ElementHost(e));

    // ===== Private helpers =====

    private string GetTextContent()
    {
        var sb = new System.Text.StringBuilder();
        CollectText(_element, sb);
        return sb.ToString();
    }

    private static void CollectText(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
            sb.Append(tn.TextContent);
        foreach (var child in node.Children)
            CollectText(child, sb);
    }

    private string BuildInnerHtml()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in _element.Children)
            SerializeNode(child, sb);
        return sb.ToString();
    }

    private string BuildOuterHtml()
    {
        var sb = new System.Text.StringBuilder();
        SerializeNode(_element, sb);
        return sb.ToString();
    }

    private static void SerializeNode(Node node, System.Text.StringBuilder sb)
    {
        if (node is TextNode tn)
        {
            sb.Append(System.Net.WebUtility.HtmlEncode(tn.TextContent ?? ""));
        }
        else if (node is Element el)
        {
            sb.Append('<').Append(el.TagName.ToLowerInvariant());
            foreach (var attr in el.Attributes)
            {
                sb.Append(' ').Append(attr.Key);
                if (!string.IsNullOrEmpty(attr.Value))
                    sb.Append("=\"").Append(System.Net.WebUtility.HtmlEncode(attr.Value)).Append('"');
            }
            sb.Append('>');
            foreach (var child in el.Children)
                SerializeNode(child, sb);
            sb.Append("</").Append(el.TagName.ToLowerInvariant()).Append('>');
        }
    }

    private static bool MatchesSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }

    private static List<Element> QuerySelectorAllInternal(Element root, string selector)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (MatchesSelector(child, selector))
                result.Add(child);
            result.AddRange(QuerySelectorAllInternal(child, selector));
        }
        return result;
    }

    private static List<Element> GetElementsByTagNameInternal(Element root, string tagName)
    {
        tagName = tagName.ToUpperInvariant();
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.TagName == tagName)
                result.Add(child);
            result.AddRange(GetElementsByTagNameInternal(child, tagName));
        }
        return result;
    }

    private static List<Element> GetElementsByClassNameInternal(Element root, string className)
    {
        var result = new List<Element>();
        foreach (var child in root.Children.OfType<Element>())
        {
            if (child.HasClass(className))
                result.Add(child);
            result.AddRange(GetElementsByClassNameInternal(child, className));
        }
        return result;
    }

    private static Element CloneElement(Element source, bool deep)
    {
        var clone = new HtmlElement(source.TagName);
        foreach (var attr in source.Attributes)
            clone.Attributes[attr.Key] = attr.Value;
        foreach (var kv in source.Style)
            clone.Style[kv.Key] = kv.Value;
        if (deep)
        {
            foreach (var child in source.Children)
            {
                if (child is Element childEl)
                    clone.AppendChild(CloneElement(childEl, true));
                else if (child is TextNode tn)
                    clone.AppendChild(new TextNode(tn.TextContent ?? ""));
            }
        }
        return clone;
    }

    private static bool ContainsElement(Element parent, Element child)
    {
        if (parent == child) return true;
        foreach (var c in parent.Children.OfType<Element>())
        {
            if (ContainsElement(c, child))
                return true;
        }
        return false;
    }

    private static object WrapNode(Node node)
    {
        if (node is Element e) return new ElementHost(e);
        return new TextNodeWrapper((TextNode)node);
    }
}

public class TextNodeWrapper
{
    private readonly TextNode _node;
    public TextNodeWrapper(TextNode node) => _node = node;
    public string nodeName => "#text";
    public string? textContent { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public string? data { get => _node.TextContent; set => _node.Data = value ?? ""; }
    public int nodeType => 3;
    public string? wholeText => _node.TextContent;
}

public class ScriptEvent
{
    public string type { get; }
    public ElementHost? target { get; }
    public ElementHost? currentTarget { get; set; }
    public bool bubbles { get; set; } = true;
    public bool cancelable { get; set; } = true;
    public bool DefaultPrevented { get; private set; }
    public long timeStamp { get; }

    public ScriptEvent(string type, ElementHost? target)
    {
        this.type = type;
        this.target = target;
        timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void preventDefault() => DefaultPrevented = true;
    public void stopPropagation() => bubbles = false;
    public void stopImmediatePropagation() => bubbles = false;
}
