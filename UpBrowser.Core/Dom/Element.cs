using SkiaSharp;

namespace UpBrowser.Core.Dom;

public abstract class Element : Node
{
    private string[]? _classListCache;

    protected Element(string tagName) : base()
    {
        InternalState.LocalName = tagName;
        InternalState.NamespaceUri = "http://www.w3.org/1999/xhtml";
        TagName = tagName.ToUpperInvariant();
    }

    public override NodeType NodeType => NodeType.Element;

    public string TagName { get; set; } = string.Empty;
    public string? NamespaceUri { get => InternalState.NamespaceUri; set => InternalState.NamespaceUri = value; }
    public string? Prefix { get => InternalState.Prefix; set => InternalState.Prefix = value; }
    public string LocalName => InternalState.LocalName;
    public override string NodeName => TagName;

    // ===== Attributes =====
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasAttributes() => Attributes.Count > 0;
    public string[] GetAttributeNames() => Attributes.Keys.ToArray();

    public string? GetAttribute(string name) =>
        Attributes.TryGetValue(name, out var val) ? val : null;

    public void SetAttribute(string name, string value)
    {
        var oldValue = Attributes.GetValueOrDefault(name);
        Attributes[name] = value;
        _classListCache = null;

        OnAttributeChanged(name, oldValue, value);

        if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            _classListCache = null;
    }

    public void RemoveAttribute(string name)
    {
        var oldValue = Attributes.GetValueOrDefault(name);
        Attributes.Remove(name);
        _classListCache = null;

        OnAttributeChanged(name, oldValue, null);

        if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            _classListCache = null;
    }

    public bool HasAttribute(string name) => Attributes.ContainsKey(name);

    public string? GetAttributeNS(string? ns, string name) => GetAttribute(name);
    public void SetAttributeNS(string? ns, string name, string value) => SetAttribute(name, value);
    public void RemoveAttributeNS(string? ns, string name) => RemoveAttribute(name);
    public bool HasAttributeNS(string? ns, string name) => HasAttribute(name);

    // ===== ClassList =====
    public string[] ClassList
    {
        get
        {
            if (_classListCache == null)
            {
                var classAttr = GetAttribute("class");
                _classListCache = string.IsNullOrWhiteSpace(classAttr)
                    ? Array.Empty<string>()
                    : classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
            return _classListCache;
        }
    }

    public bool HasClass(string className) => ClassList.Contains(className);

    public string? ClassName
    {
        get => GetAttribute("class");
        set
        {
            if (value != null) SetAttribute("class", value);
            else RemoveAttribute("class");
            _classListCache = null;
        }
    }

    public string? Id
    {
        get => GetAttribute("id");
        set
        {
            if (value != null) SetAttribute("id", value);
            else RemoveAttribute("id");
        }
    }

    // ===== DOM Layout Properties =====
    private string? _slot;
    public string? Slot
    {
        get => _slot ?? GetAttribute("slot");
        set { _slot = value; if (value != null) SetAttribute("slot", value); else RemoveAttribute("slot"); }
    }

    public ComputedStyle? ComputedStyle { get; set; }
    public LayoutBox? LayoutBox { get; set; }

    public Dictionary<string, string> Style { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string>? BeforeStyles { get; set; }
    public Dictionary<string, string>? AfterStyles { get; set; }
    public bool HasGeneratedBefore { get; set; }
    public bool HasGeneratedAfter { get; set; }

    // ===== Common Attribute Shortcuts =====
    public string? Src => GetAttribute("src");
    public string? Href => GetAttribute("href");
    public string? StyleAttr => GetAttribute("style");
    public string? Alt => GetAttribute("alt");
    public string? Type => GetAttribute("type");

    private string? _value;
    public string? Value
    {
        get => _value ??= GetAttribute("value");
        set
        {
            _value = value;
            SetAttribute("value", value ?? "");
        }
    }

    public string? Name => GetAttribute("name");
    public string? InputType => GetAttribute("type");
    public bool IsFormElement => TagName is "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON";

    public int SelectionStart { get; set; }
    public int SelectionEnd { get; set; }
    public bool IsFocused { get; set; }
    public bool IsHovered { get; set; }

    // ===== Shadow DOM =====
    public ShadowRoot? ShadowRoot => InternalState.ShadowRoot;

    public ShadowRoot AttachShadow(ShadowRootInit init)
    {
        if (InternalState.ShadowRoot != null)
            throw new DOMException("Shadow root already exists", "NotSupportedError");

        var shadowRoot = new ShadowRoot(this, init.Mode, init.DelegatesFocus, init.SlotAssignment)
        {
            Clonable = init.Clonable,
            Serializable = init.Serializable
        };
        InternalState.ShadowRoot = shadowRoot;
        return shadowRoot;
    }

    // ===== Geometry =====
    public SKRect GetBoundingClientRect()
    {
        if (LayoutBox != null && LayoutBox.BorderBox.Width > 0 && LayoutBox.BorderBox.Height > 0)
            return LayoutBox.BorderBox;

        var rect = new SKRect(0, 0, 0, 0);
        bool first = true;

        if (LayoutBox?.LineRuns != null)
        {
            foreach (var run in LayoutBox.LineRuns)
            {
                if (run.Width > 0 || run.Height > 0)
                {
                    var runRect = new SKRect(run.X, 0, run.X + run.Width, run.Height);
                    rect = first ? runRect : SKRect.Union(rect, runRect);
                    first = false;
                }
            }
        }

        foreach (var child in Children.OfType<Element>())
        {
            var childRect = child.GetBoundingClientRect();
            if (childRect.Width > 0 || childRect.Height > 0)
            {
                rect = first ? childRect : SKRect.Union(rect, childRect);
                first = false;
            }
        }

        return rect;
    }

    public Element? OffsetParent
    {
        get
        {
            var parent = ParentElement;
            while (parent != null)
            {
                var pos = parent.ComputedStyle?.Position;
                if (pos != PositionType.Static ||
                    parent.TagName is "TD" or "TH" or "TABLE" or "BODY")
                    return parent;
                parent = parent.ParentElement;
            }
            return null;
        }
    }

    public float OffsetTop
    {
        get
        {
            if (LayoutBox == null) return 0;
            var myTop = LayoutBox.BorderBox.Top;
            var parentTop = OffsetParent?.LayoutBox?.BorderBox.Top ?? 0;
            return myTop - parentTop;
        }
    }

    public float OffsetLeft
    {
        get
        {
            if (LayoutBox == null) return 0;
            var myLeft = LayoutBox.BorderBox.Left;
            var parentLeft = OffsetParent?.LayoutBox?.BorderBox.Left ?? 0;
            return myLeft - parentLeft;
        }
    }

    public float OffsetWidth => LayoutBox?.BorderBox.Width ?? 0;
    public float OffsetHeight => LayoutBox?.BorderBox.Height ?? 0;
    public float ClientWidth => LayoutBox?.PaddingBox.Width ?? 0;
    public float ClientHeight => LayoutBox?.PaddingBox.Height ?? 0;
    public float ScrollWidth => Math.Max(ClientWidth, LayoutBox?.ContentBox.Width ?? 0);
    public float ScrollHeight => Math.Max(ClientHeight, LayoutBox?.ContentBox.Height ?? 0);

    // ===== Spec Methods =====
    public Element? Closest(string selector)
    {
        var el = this;
        while (el != null)
        {
            if (MatchesSelector(el, selector))
                return el;
            el = el.ParentElement;
        }
        return null;
    }

    public bool Matches(string selector)
    {
        var parsed = Css.CssSelector.Parse(selector);
        return parsed.Matches(this, ParentElement);
    }

    public void Prepend(params Node[] nodes)
    {
        for (int i = nodes.Length - 1; i >= 0; i--)
            InsertBefore(nodes[i], FirstChild);
    }

    public void Append(params Node[] nodes)
    {
        foreach (var node in nodes)
            AppendChild(node);
    }

    public void ReplaceChildren(params Node[] nodes)
    {
        Children.Clear();
        foreach (var node in nodes)
            AppendChild(node);
    }

    public void Before(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        foreach (var node in nodes)
            parent.InsertBefore(node, this);
    }

    public void After(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public void ReplaceWith(params Node[] nodes)
    {
        var parent = ParentNode;
        if (parent == null) return;
        var next = NextSibling;
        Remove();
        foreach (var node in nodes)
            parent.InsertBefore(node, next);
    }

    public void InsertAdjacentElement(string position, Element element)
    {
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                ParentNode?.InsertBefore(element, this);
                break;
            case "afterbegin":
                InsertBefore(element, FirstChild);
                break;
            case "beforeend":
                AppendChild(element);
                break;
            case "afterend":
                ParentNode?.InsertBefore(element, NextSibling);
                break;
        }
    }

    public void InsertAdjacentText(string position, string text)
    {
        var textNode = new TextNode(text);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                ParentNode?.InsertBefore(textNode, this);
                break;
            case "afterbegin":
                InsertBefore(textNode, FirstChild);
                break;
            case "beforeend":
                AppendChild(textNode);
                break;
            case "afterend":
                ParentNode?.InsertBefore(textNode, NextSibling);
                break;
        }
    }

    public void InsertAdjacentHTML(string position, string text)
    {
        var parsed = JavaScript.HtmlParser.ParseFragment(text, TagName);
        switch (position.ToLowerInvariant())
        {
            case "beforebegin":
                var parent = ParentNode;
                if (parent != null)
                    for (int i = parsed.Count - 1; i >= 0; i--)
                        parent.InsertBefore(parsed[i], this);
                break;
            case "afterbegin":
                for (int i = parsed.Count - 1; i >= 0; i--)
                    InsertBefore(parsed[i], FirstChild);
                break;
            case "beforeend":
                foreach (var node in parsed)
                    AppendChild(node);
                break;
            case "afterend":
                var parent2 = ParentNode;
                var next = NextSibling;
                if (parent2 != null)
                    foreach (var node in parsed)
                        parent2.InsertBefore(node, next);
                break;
        }
    }

    // ===== Hooks =====
    protected override void OnAttributeChanged(string name, string? oldValue, string? newValue)
    {
        base.OnAttributeChanged(name, oldValue, newValue);
    }

    // ===== Private =====
    private static bool MatchesSelector(Element el, string selector)
    {
        selector = selector.Trim();
        if (selector.StartsWith('#'))
            return el.Id == selector[1..];
        if (selector.StartsWith('.'))
            return el.HasClass(selector[1..]);
        return el.TagName.Equals(selector, StringComparison.OrdinalIgnoreCase);
    }
}
