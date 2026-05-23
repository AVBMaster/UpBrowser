using SkiaSharp;

namespace UpBrowser.Core.Dom;

public class Document : Node
{
    private Element? _documentElement;
    public Element? DocumentElement
    {
        get => _documentElement;
        set
        {
            _documentElement = value;
            if (value != null && !Children.Contains(value))
                AppendChild(value);
        }
    }
    public Element? Body { get; set; }
    public Element? Head { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    public Document() : base("#document") { }

    public override NodeType NodeType => NodeType.Document;
    public override string NodeName => "#document";
}

public enum NodeType
{
    Element,
    Text,
    Document,
    Comment,
    DocumentType
}

public abstract class Node
{
    public virtual string NodeName { get; protected set; } = string.Empty;
    public Node? Parent { get; set; }
    public List<Node> Children { get; } = new();
    public abstract NodeType NodeType { get; }
    public virtual string? TextContent => null;
    public virtual bool IsConnected => false;

    public Node(string nodeName)
    {
        NodeName = nodeName;
    }

    public void AppendChild(Node child)
    {
        if (child.Parent != null)
            child.Parent.RemoveChild(child);
        child.Parent = this;
        Children.Add(child);
    }

    public void InsertBefore(Node newChild, Node? refChild)
    {
        if (refChild == null)
        {
            AppendChild(newChild);
            return;
        }
        int index = Children.IndexOf(refChild);
        if (index < 0)
        {
            AppendChild(newChild);
            return;
        }
        if (newChild.Parent != null)
            newChild.Parent.RemoveChild(newChild);
        newChild.Parent = this;
        Children.Insert(index, newChild);
    }

    public void RemoveChild(Node child)
    {
        Children.Remove(child);
        child.Parent = null;
    }

    public T? FirstChild<T>() where T : Node => Children.FirstOrDefault() as T;
    public IEnumerable<T> ChildrenOfType<T>() where T : Node => Children.OfType<T>();

    public Node? NextSibling => Parent?.Children.ElementAtOrDefault(Parent.Children.IndexOf(this) + 1);
    public Node? PreviousSibling => Parent?.Children.ElementAtOrDefault(Parent.Children.IndexOf(this) - 1);

    public bool IsElement => this is Element;
    public bool IsTextNode => this is TextNode;
    public Element? ParentElement => Parent as Element;
}

public abstract class Element : Node
{
    public string TagName { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; } = new();
    public Dictionary<string, string> Style { get; } = new();
    public ComputedStyle? ComputedStyle { get; set; }
    public LayoutBox? LayoutBox { get; set; }
    public string? NamespaceUri { get; set; }

    private string[]? _classList;
    public string[] ClassList
    {
        get
        {
            if (_classList == null)
            {
                var classAttr = GetAttribute("class");
                _classList = string.IsNullOrWhiteSpace(classAttr)
                    ? Array.Empty<string>()
                    : classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
            return _classList;
        }
    }

    public bool HasClass(string className) => ClassList.Contains(className);

    public Element(string tagName) : base(tagName.ToUpperInvariant())
    {
        TagName = tagName.ToUpperInvariant();
    }

    public override NodeType NodeType => NodeType.Element;
    public override string NodeName => TagName;

    public string? GetAttribute(string name) => Attributes.GetValueOrDefault(name);
    public bool HasAttribute(string name) => Attributes.ContainsKey(name);
    public void SetAttribute(string name, string value) => Attributes[name] = value;
    public void RemoveAttribute(string name) => Attributes.Remove(name);

    public string? Id
    {
        get => GetAttribute("id");
        set => Attributes["id"] = value ?? string.Empty;
    }

    public string? ClassName
    {
        get => GetAttribute("class");
        set
        {
            Attributes["class"] = value ?? string.Empty;
            _classList = null;
        }
    }

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
            Attributes["value"] = value ?? "";
        }
    }
    public string? Name => GetAttribute("name");
    public string? InputType => GetAttribute("type");
    public bool IsFormElement => TagName is "INPUT" or "TEXTAREA" or "SELECT" or "BUTTON";

    public int SelectionStart { get; set; }
    public int SelectionEnd { get; set; }
    public bool IsFocused { get; set; }

    // ===== 新增标准 DOM 布局属性（修复问题1、2） =====

    /// <summary>
    /// 标准 DOM API: getBoundingClientRect()
    /// 对 inline 元素聚合所有子文本片段的边界
    /// </summary>
    public SKRect GetBoundingClientRect()
    {
        if (LayoutBox != null && LayoutBox.BorderBox.Width > 0 && LayoutBox.BorderBox.Height > 0)
        {
            return LayoutBox.BorderBox;
        }

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

    /// <summary>
    /// offsetParent: 最近的定位祖先或特殊元素
    /// </summary>
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

    /// <summary>
    /// offsetTop: 相对于 offsetParent 的顶部偏移
    /// </summary>
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

    /// <summary>
    /// offsetLeft: 相对于 offsetParent 的左侧偏移
    /// </summary>
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

    /// <summary>
    /// offsetWidth: 包含 border + padding + content
    /// </summary>
    public float OffsetWidth => LayoutBox?.BorderBox.Width ?? 0;

    /// <summary>
    /// offsetHeight: 包含 border + padding + content
    /// </summary>
    public float OffsetHeight => LayoutBox?.BorderBox.Height ?? 0;

    /// <summary>
    /// clientWidth: 包含 padding + content (不含 border)
    /// </summary>
    public float ClientWidth => LayoutBox?.PaddingBox.Width ?? 0;

    /// <summary>
    /// clientHeight: 包含 padding + content (不含 border)
    /// </summary>
    public float ClientHeight => LayoutBox?.PaddingBox.Height ?? 0;

    /// <summary>
    /// scrollWidth/Height: 内容溢出尺寸
    /// </summary>
    public float ScrollWidth => Math.Max(ClientWidth, LayoutBox?.ContentBox.Width ?? 0);
    public float ScrollHeight => Math.Max(ClientHeight, LayoutBox?.ContentBox.Height ?? 0);
}

public class TextNode : Node
{
    private string _text;

    public TextNode(string text) : base("#text")
    {
        _text = text;
    }

    public override NodeType NodeType => NodeType.Text;
    public override string NodeName => "#text";
    public override string? TextContent => _text;

    public string Data
    {
        get => _text;
        set => _text = value;
    }

    public bool IsWhitespaceOnly => string.IsNullOrWhiteSpace(_text);
}

public class CommentNode : Node
{
    private readonly string _data;

    public CommentNode(string data) : base("#comment")
    {
        _data = data;
    }

    public override NodeType NodeType => NodeType.Comment;
    public override string NodeName => "#comment";
    public override string? TextContent => _data;
}

public class DocumentTypeNode : Node
{
    public string? PublicId { get; }
    public string? SystemId { get; }

    public DocumentTypeNode(string? publicId = null, string? systemId = null) : base("#document-type")
    {
        PublicId = publicId;
        SystemId = systemId;
    }

    public override NodeType NodeType => NodeType.DocumentType;
    public override string NodeName => "DOCTYPE";
}

public class DocumentFragment : Node
{
    public DocumentFragment() : base("#document-fragment") { }

    public override NodeType NodeType => NodeType.Document;
    public override string NodeName => "#document-fragment";
}