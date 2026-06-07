namespace UpBrowser.Core.Dom;

public enum SupportedType
{
    TextHtml,
    TextXml,
    ApplicationXml,
    ApplicationXhtmlXml,
    ImageSvgXml
}

public class DomParser
{
    public Document ParseFromString(string source, SupportedType type)
    {
        switch (type)
        {
            case SupportedType.TextHtml:
                return ParseHtml(source);
            case SupportedType.TextXml:
            case SupportedType.ApplicationXml:
            case SupportedType.ApplicationXhtmlXml:
            case SupportedType.ImageSvgXml:
                return ParseXml(source);
            default:
                throw new ArgumentException($"Unsupported type: {type}", nameof(type));
        }
    }

    private Document ParseHtml(string source)
    {
        var doc = new Document();
        doc.ParseHtml(source);
        return doc;
    }

    private Document ParseXml(string source)
    {
        var doc = new Document();
        doc.ParseXml(source);
        return doc;
    }
}

public class XmlSerializer
{
    public string SerializeToString(Node root)
    {
        var sb = new System.Text.StringBuilder();
        SerializeNode(root, sb);
        return sb.ToString();
    }

    private static void SerializeNode(Node node, System.Text.StringBuilder sb)
    {
        switch (node.NodeType)
        {
            case NodeType.Element:
                var element = (Element)node;
                sb.Append('<').Append(element.TagName);
                foreach (var attr in element.Attributes)
                {
                    sb.Append(' ').Append(attr.Key).Append("=\"").Append(EscapeXml(attr.Value)).Append('"');
                }
                if (element.ChildNodes.Count == 0)
                {
                    sb.Append("/>");
                }
                else
                {
                    sb.Append('>');
                    foreach (var child in element.ChildNodes)
                        SerializeNode(child, sb);
                    sb.Append("</").Append(element.TagName).Append('>');
                }
                break;
            case NodeType.Text:
                sb.Append(EscapeXml(((TextNode)node).Data));
                break;
            case NodeType.Comment:
                sb.Append("<!--").Append(((CommentNode)node).Data).Append("-->");
                break;
            case NodeType.Document:
                var doc = (Document)node;
                sb.Append("<!DOCTYPE html>\n");
                if (doc.DocumentElement != null)
                    SerializeNode(doc.DocumentElement, sb);
                break;
            case NodeType.DocumentFragment:
                foreach (var child in node.ChildNodes)
                    SerializeNode(child, sb);
                break;
            case NodeType.ProcessingInstruction:
                var pi = (ProcessingInstruction)node;
                sb.Append("<?").Append(pi.Target).Append(' ').Append(pi.Data).Append("?>");
                break;
            case NodeType.CDataSection:
                sb.Append("<![CDATA[").Append(((CDataSection)node).Data).Append("]]>");
                break;
            case NodeType.DocumentType:
                var doctype = (DocumentTypeNode)node;
                sb.Append("<!DOCTYPE html>");
                break;
        }
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
