using UpBrowser.Core.Dom;

namespace UpBrowser.Core.JavaScript;

public class ElementHost
{
    private readonly Element _element;
    private StyleHost? _styleHost;
    private readonly Dictionary<string, List<int>> _eventCallbackIds = new();

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

    public void addEventListener(string type, object callback)
    {
        var engine = JavaScriptEngine.Current;
        if (engine == null) return;

        var cbId = engine.StoreCallbackRef(callback);
        if (!_eventCallbackIds.TryGetValue(type, out var list))
        {
            list = new List<int>();
            _eventCallbackIds[type] = list;
        }
        list.Add(cbId);
    }

    public void removeEventListener(string type, object callback)
    {
        if (_eventCallbackIds.TryGetValue(type, out var list))
        {
            foreach (var cbId in list)
            {
                var engine = JavaScriptEngine.Current;
                engine?.RemoveCallback(cbId);
            }
            list.Clear();
        }
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

    public object? getBoundingClientRect()
    {
        var box = _element.LayoutBox;
        if (box == null)
        {
            // Return zeros instead of null so JS callers can safely call properties/toFixed
            return new Dictionary<string, double>
            {
                ["top"] = 0,
                ["right"] = 0,
                ["bottom"] = 0,
                ["left"] = 0,
                ["width"] = 0,
                ["height"] = 0
            };
        }

        return new Dictionary<string, double>
        {
            ["top"] = box.BorderBox.Top,
            ["right"] = box.BorderBox.Right,
            ["bottom"] = box.BorderBox.Bottom,
            ["left"] = box.BorderBox.Left,
            ["width"] = box.BorderBox.Width,
            ["height"] = box.BorderBox.Height
        };
    }

    // Layout dimension properties
    public float offsetWidth 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Width;
        }
    }

    public float offsetHeight 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Height;
        }
    }

    public float clientWidth 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Width;
        }
    }

    public float clientHeight 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Height;
        }
    }

    public float scrollWidth 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Width;
        }
    }

    public float scrollHeight 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.ContentBox.Height;
        }
    }

    public float offsetTop 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Top;
        }
    }

    public float offsetLeft 
    {
        get 
        {
            var box = _element.LayoutBox;
            if (box == null) return 0;
            return box.BorderBox.Left;
        }
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

    internal bool DispatchEvent(ScriptEvent evt)
    {
        if (string.IsNullOrEmpty(evt.type)) return true;

        // Capturing phase (not implemented yet, would go from root to target)
        
        // Target phase
        if (_eventCallbackIds.TryGetValue(evt.type, out var cbIds))
        {
            var engine = JavaScriptEngine.Current;
            if (engine != null)
            {
                foreach (var cbId in cbIds.ToList())
                {
                    try
                    {
                        evt.currentTarget = this;
                        evt.eventPhase = 2; // AT_TARGET
                        engine.InvokeCallbackWith(cbId, evt);
                    }
                    catch { }
                }
            }
        }

        // Bubbling phase
        if (evt.bubbles)
        {
            var parent = _element.ParentElement;
            if (parent != null)
            {
                var parentHost = new ElementHost(parent);
                evt.eventPhase = 3; // BUBBLING_PHASE
                parentHost.DispatchEvent(evt);
            }
            else if (_element.Parent is Core.Dom.Document)
            {
                // Dispatch to document when bubbling reaches root element
                var engine = JavaScriptEngine.Current;
                if (engine?.DocumentHost != null)
                {
                    try
                    {
                        evt.eventPhase = 3;
                        engine.DocumentHost.dispatchEvent(evt);
                    }
                    catch { }
                }
            }
        }

        // Check for inline event handler
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

    public int getClientRects() => 1;

    public bool hasAttributes() => _element.Attributes.Count > 0;

    public ElementHost? firstElementChild
    {
        get
        {
            foreach (var child in _element.Children)
            {
                if (child is Element el) return new ElementHost(el);
            }
            return null;
        }
    }

    public ElementHost? lastElementChild
    {
        get
        {
            Element? last = null;
            foreach (var child in _element.Children)
            {
                if (child is Element el) last = el;
            }
            return last != null ? new ElementHost(last) : null;
        }
    }

    public string? namespaceURI => "http://www.w3.org/1999/xhtml";

    public string? prefix => null;

    public string localName => _element.TagName.ToLowerInvariant();

    public string baseURI => "";

    public bool isConnected => _element.ParentElement != null || _element.TagName == "HTML";

    public object? ownerDocument => null;

    public bool matches(string selector)
    {
        var parsed = UpBrowser.Core.Css.CssSelector.Parse(selector);
        return parsed.Matches(_element, _element.ParentElement);
    }

    public void insertAdjacentHTML(string position, string text)
    {
        var parsed = HtmlParser.ParseFragment(text, _element.TagName);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                var parent = _element.ParentElement;
                parent?.InsertBefore(parsed[0], _element);
                break;
            case "afterbegin":
                for (int i = parsed.Count - 1; i >= 0; i--)
                    _element.InsertBefore(parsed[i], _element.Children.FirstOrDefault());
                break;
            case "beforeend":
                foreach (var node in parsed)
                    _element.AppendChild(node);
                break;
            case "afterend":
                var parent2 = _element.ParentElement;
                var next = _element.NextSibling;
                foreach (var node in parsed)
                    parent2?.InsertBefore(node, next);
                break;
        }
    }

    public void insertAdjacentText(string position, string text)
    {
        insertAdjacentHTML(position, text);
    }

    public void remove()
    {
        _element.ParentElement?.RemoveChild(_element);
    }

    public object? replaceWith(object newElement)
    {
        if (newElement is ElementHost host)
        {
            var parent = _element.ParentElement;
            if (parent != null)
            {
                parent.RemoveChild(_element);
                parent.AppendChild(host._element);
            }
        }
        return null;
    }

    public object? before(object newElement)
    {
        if (newElement is ElementHost host)
        {
            _element.ParentElement?.InsertBefore(host._element, _element);
        }
        return null;
    }

    public object? after(object newElement)
    {
        if (newElement is ElementHost host)
        {
            _element.ParentElement?.InsertBefore(host._element, _element.NextSibling);
        }
        return null;
    }

    public bool hasAttributeNS(string ns, string name)
    {
        return _element.HasAttribute(name);
    }

    public object? dataset => null;

    public string? dir
    {
        get => _element.GetAttribute("dir");
        set => _element.SetAttribute("dir", value);
    }

    public string? lang
    {
        get => _element.GetAttribute("lang");
        set => _element.SetAttribute("lang", value);
    }

    public string? title
    {
        get => _element.GetAttribute("title");
        set => _element.SetAttribute("title", value);
    }

    public bool hidden
    {
        get => _element.GetAttribute("hidden") != null;
        set
        {
            if (value) _element.SetAttribute("hidden", "");
            else _element.RemoveAttribute("hidden");
        }
    }

    public bool draggable
    {
        get => _element.GetAttribute("draggable") == "true";
        set => _element.SetAttribute("draggable", value ? "true" : "false");
    }

    public string? accessKey
    {
        get => _element.GetAttribute("accesskey");
        set => _element.SetAttribute("accesskey", value);
    }

    public int tabIndex
    {
        get => int.TryParse(_element.GetAttribute("tabindex"), out var i) ? i : 0;
        set => _element.SetAttribute("tabindex", value.ToString());
    }

    public void animate() { }

    public string? slot
    {
        get => _element.GetAttribute("slot");
        set => _element.SetAttribute("slot", value);
    }

    public bool spellcheck
    {
        get => _element.GetAttribute("spellcheck") == "true";
        set => _element.SetAttribute("spellcheck", value ? "true" : "false");
    }

    public string? translate
    {
        get => _element.GetAttribute("translate");
        set => _element.SetAttribute("translate", value);
    }

    public void attachShadow(object? mode) { }

    public object? shadowRoot => null;

    public bool assignedSlot => false;

    public int nodeType => 1;

    public string? nodeValue { get; set; }

    public void toggleAttribute(string name)
    {
        if (_element.HasAttribute(name))
            _element.RemoveAttribute(name);
        else
            _element.SetAttribute(name, "");
    }

    public object? getAttributeNode(string name) => null;

    public object? getAttributeNodeNS(string ns, string name) => null;

    public object? setAttributeNode(object attr) => null;

    public object? setAttributeNodeNS(object attr) => null;

    public object? removeAttributeNode(object attr) => null;

    public void normalize()
    {
        // Merge adjacent text nodes
        var newChildren = new List<Node>();
        TextNode? lastText = null;
        foreach (var child in _element.Children)
        {
            if (child is TextNode tn)
            {
                if (lastText != null)
                {
                    lastText.Data += tn.Data;
                }
                else
                {
                    lastText = tn;
                    newChildren.Add(tn);
                }
            }
            else
            {
                lastText = null;
                newChildren.Add(child);
            }
        }
        _element.Children.Clear();
        foreach (var child in newChildren)
            _element.AppendChild(child);
    }

    public bool isDefaultNamespace(string ns) => false;

    public string? lookupNamespaceURI(string prefix) => null;

    public string? lookupPrefix(string ns) => null;

    public bool isEqualNode(object? other)
    {
        if (other is ElementHost host)
            return _element.TagName == host.NativeElement.TagName;
        return false;
    }

    public bool isSameNode(object? other)
    {
        if (other is ElementHost host)
            return _element == host.NativeElement;
        return false;
    }

    public int compareDocumentPosition(object? other) => 0;

    public object? getRootNode(object? options) => null;

    public void setPointerCapture(int pointerId) { }

    public void releasePointerCapture(int pointerId) { }

    public bool hasPointerCapture(int pointerId) => false;
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
    public string type { get; set; }
    public ElementHost? target { get; }
    public ElementHost? currentTarget { get; set; }
    public bool bubbles { get; set; } = true;
    public bool cancelable { get; set; } = true;
    public bool DefaultPrevented { get; private set; }
    public long timeStamp { get; }
    public int eventPhase { get; set; }
    public object? detail { get; set; }
    public bool composed { get; set; } = false;
    public bool isTrusted { get; } = true;
    public string? returnValue { get; set; }
    public string? srcElement => target?.ToString();
    public bool cancelBubble { get => !bubbles; set => bubbles = !value; }

    // Keyboard event properties
    public string? key { get; set; }
    public string? code { get; set; }
    public bool ctrlKey { get; set; }
    public bool shiftKey { get; set; }
    public bool altKey { get; set; }
    public bool metaKey { get; set; }
    public bool repeat { get; set; }
    public bool isComposing { get; set; }
    public int keyCode { get; set; }
    public int which { get; set; }
    public bool charCode { get; set; }

    // Mouse event properties
    public double clientX { get; set; }
    public double clientY { get; set; }
    public double screenX { get; set; }
    public double screenY { get; set; }
    public int button { get; set; }
    public int buttons { get; set; }
    public ElementHost? relatedTarget { get; set; }

    // Wheel event properties
    public double deltaX { get; set; }
    public double deltaY { get; set; }
    public double deltaZ { get; set; }
    public int deltaMode { get; set; }

    public ScriptEvent(string type, ElementHost? target)
    {
        this.type = type;
        this.target = target;
        timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void preventDefault() => DefaultPrevented = true;
    public void stopPropagation() => bubbles = false;
    public void stopImmediatePropagation() => bubbles = false;
    public void initEvent(string type, bool bubbles = true, bool cancelable = true)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
    }
    public void initUIEvent(string type, bool bubbles = true, bool cancelable = true, object? view = null, int detail = 0)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
        this.detail = detail;
    }
    public void initMouseEvent(string type, bool bubbles = true, bool cancelable = true, object? view = null, int detail = 0, int screenX = 0, int screenY = 0, int clientX = 0, int clientY = 0, bool ctrlKey = false, bool altKey = false, bool shiftKey = false, bool metaKey = false, int button = 0, object? relatedTarget = null)
    {
        this.type = type;
        this.bubbles = bubbles;
        this.cancelable = cancelable;
        this.detail = detail;
        this.screenX = screenX;
        this.screenY = screenY;
        this.clientX = clientX;
        this.clientY = clientY;
        this.ctrlKey = ctrlKey;
        this.altKey = altKey;
        this.shiftKey = shiftKey;
        this.metaKey = metaKey;
        this.button = button;
    }
}

public class DomRect
{
    public float x { get; }
    public float y { get; }
    public float width { get; }
    public float height { get; }
    public float top => y;
    public float right => x + width;
    public float bottom => y + height;
    public float left => x;

    public DomRect(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}
