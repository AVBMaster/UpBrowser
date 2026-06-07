namespace UpBrowser.Core.Dom;

public class TextNode : CharacterData
{
    public TextNode(string text) : base(text, "#text")
    {
    }

    public override NodeType NodeType => NodeType.Text;
    public override string NodeName => "#text";

    public bool IsWhitespaceOnly => string.IsNullOrWhiteSpace(Data);

    public string WholeText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var current = this;
            while (current.PreviousSibling is TextNode prev)
                current = prev;
            while (current != null && current is TextNode tn)
            {
                sb.Append(tn.Data);
                current = current.NextSibling as TextNode;
            }
            return sb.ToString();
        }
    }

    public TextNode SplitText(int offset)
    {
        if (offset < 0 || offset > Data.Length)
            throw new DOMException("Index out of bounds", "IndexSizeError");

        var newData = Data[offset..];
        Data = Data[..offset];

        var newNode = new TextNode(newData);
        if (ParentNode != null)
            ParentNode.InsertBefore(newNode, NextSibling);
        return newNode;
    }

    public Element? AssignedSlot => null;

    protected override Node CloneNodeInternal(bool deep) => new TextNode(Data);
}
