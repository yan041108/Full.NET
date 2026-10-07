using System.Xml;
using System.Xml.Linq;

namespace Full.NET.Ai.DocxParser;

/// <summary>禁用 DTD、实体与解析器网络访问，先限制深度/节点/文本再构造有界 XML 树。</summary>
internal static class DocxXml
{
    internal static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string MainType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private static readonly HashSet<string> UnsupportedProperties = new(StringComparer.Ordinal)
    {
        "pPrChange", "rPrChange", "sectPrChange", "tblPrChange", "tblGridChange", "trPrChange", "tcPrChange", "cellIns", "cellDel", "cellMerge",
        "ins", "del", "moveFrom", "moveTo", "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
        "vanish", "webHidden", "specVanish", "headerReference", "footerReference", "footnoteReference", "endnoteReference",
        "numPr", "gridBefore", "gridAfter", "fldSimple", "fldChar", "instrText",
    };

    internal static XDocument Read(byte[] bytes, CancellationToken token)
    {
        Check(bytes, token);
        using var source = new MemoryStream(bytes, false);
        using var bounded = XmlReader.Create(source, Settings());
        var result = XDocument.Load(bounded, LoadOptions.PreserveWhitespace);
        token.ThrowIfCancellationRequested();
        return result;
    }

    internal static void Check(byte[] bytes, CancellationToken token)
    {
        using var source = new MemoryStream(bytes, false);
        // 样式等其他 XML 部件只执行同一有界预检，不为未消费部件创建 DOM。
        using (var reader = XmlReader.Create(source, Settings()))
        {
            var nodes = 0; var characters = 0;
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.Depth > 32) Reject("fn_ai_docx_xml_depth_limit");
                if (reader.Depth == 0 && reader.NamespaceURI == Word.NamespaceName && reader.LocalName is "hdr" or "ftr" or "footnotes" or "endnotes")
                    Reject("fn_ai_docx_unsupported_structure");
                // 属性不会成为 Read() 节点；必须在建树前计入数量与值长度，防止碎片属性绕过预算。
                if (reader.AttributeCount > 64 || reader.AttributeCount + 1 > 65536 - nodes) Reject("fn_ai_docx_xml_node_limit");
                nodes += reader.AttributeCount + 1;
                if (reader.NamespaceURI == Word.NamespaceName && UnsupportedProperties.Contains(reader.LocalName)) Reject("fn_ai_docx_unsupported_structure");
                if (reader.HasAttributes)
                {
                    while (reader.MoveToNextAttribute())
                    {
                        token.ThrowIfCancellationRequested();
                        if (reader.Value.Length > DocxOutput.MaxCharacters - characters) Reject("fn_ai_docx_output_limit");
                        characters += reader.Value.Length;
                    }
                    reader.MoveToElement();
                }
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (reader.Value.Length > DocxOutput.MaxCharacters - characters) Reject("fn_ai_docx_output_limit");
                    characters += reader.Value.Length;
                }
            }
        }
    }

    private static XmlReaderSettings Settings()
        => new()
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CheckCharacters = true,
            MaxCharactersInDocument = DocxArchive.MaxEntryBytes, MaxCharactersFromEntities = 1,
        };

    internal static void ValidatePackage(IReadOnlyDictionary<string, byte[]> parts, CancellationToken token)
    {
        var types = Read(parts["[Content_Types].xml"], token);
        if (types.Root?.Name != Types + "Types") Reject("fn_ai_docx_invalid_package");
        var main = types.Root!.Elements().Where(value => (string?)value.Attribute("PartName") == "/word/document.xml").ToArray();
        if (main.Length != 1 || main[0].Name != Types + "Override" || (string?)main[0].Attribute("ContentType") != MainType)
            Reject("fn_ai_docx_invalid_package");
        foreach (var element in types.Root!.Elements())
        {
            var type = (string?)element.Attribute("ContentType") ?? "";
            if (type.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) || type.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
                || type.Contains("oleObject", StringComparison.OrdinalIgnoreCase) || type.Contains("activeX", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith(".header+xml", StringComparison.OrdinalIgnoreCase) || type.EndsWith(".footer+xml", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith(".footnotes+xml", StringComparison.OrdinalIgnoreCase) || type.EndsWith(".endnotes+xml", StringComparison.OrdinalIgnoreCase))
                Reject("fn_ai_docx_unsupported_structure");
        }
        var relationships = Read(parts["_rels/.rels"], token);
        if (relationships.Root?.Name != Relationships + "Relationships") Reject("fn_ai_docx_invalid_package");
        var office = relationships.Root!.Elements().Where(value => (string?)value.Attribute("Type") == OfficeRelationship).ToArray();
        if (office.Length != 1 || office[0].Name != Relationships + "Relationship" || (string?)office[0].Attribute("Target") != "word/document.xml"
            || (string?)office[0].Attribute("TargetMode") is not (null or "Internal") || string.IsNullOrEmpty((string?)office[0].Attribute("Id")))
            Reject("fn_ai_docx_invalid_package");
        foreach (var part in parts)
        {
            token.ThrowIfCancellationRequested();
            if (part.Key is "[Content_Types].xml" or "_rels/.rels" or "word/document.xml") continue;
            // OPC 可以使用任意部件名；声明的 XML 类型及常见 XML/关系扩展名均复用预检。
            var declared = types.Root.Elements().Where(value => value.Name == Types + "Override"
                && string.Equals((string?)value.Attribute("PartName"), "/" + part.Key, StringComparison.OrdinalIgnoreCase))
                .Select(value => (string?)value.Attribute("ContentType"));
            var defaults = types.Root.Elements().Where(value => value.Name == Types + "Default"
                && string.Equals((string?)value.Attribute("Extension"), Path.GetExtension(part.Key).TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                .Select(value => (string?)value.Attribute("ContentType"));
            if (part.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || part.Key.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                || declared.Concat(defaults).Any(value => value is not null && (value.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("application/xml", StringComparison.OrdinalIgnoreCase) || value.Equals("text/xml", StringComparison.OrdinalIgnoreCase))))
                Check(part.Value, token);
        }
        token.ThrowIfCancellationRequested();
    }
    private static void Reject(string code) => throw new DocxRejectedException(code);
}
