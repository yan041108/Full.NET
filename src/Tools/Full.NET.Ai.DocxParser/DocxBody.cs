using System.Text;
using System.Xml.Linq;

namespace Full.NET.Ai.DocxParser;

/// <summary>只消费明确支持的正文结构，不从字段、修订、绘图或未知包装推测可见内容。</summary>
internal sealed class DocxBody(CancellationToken token)
{
    private readonly DocxOutput output = new();
    private int paragraphs; private int tables; private int rows; private int cells;

    internal static DocxOutput Parse(XDocument document, CancellationToken token)
    {
        if (document.Root?.Name != DocxXml.Word + "document") throw new DocxRejectedException("fn_ai_docx_invalid_package");
        var bodies = Children(document.Root!).ToArray();
        if (bodies.Length != 1 || bodies[0].Name != DocxXml.Word + "body") throw new DocxRejectedException("fn_ai_docx_invalid_package");
        var parser = new DocxBody(token);
        foreach (var element in Children(bodies[0]))
        {
            token.ThrowIfCancellationRequested();
            if (element.Name == DocxXml.Word + "p") parser.output.Add(parser.Paragraph(element), "\n", parser.paragraphs);
            else if (element.Name == DocxXml.Word + "tbl") parser.Table(element);
            else if (element.Name != DocxXml.Word + "sectPr") Unsupported();
        }
        return parser.output;
    }

    private string Paragraph(XElement paragraph)
    {
        token.ThrowIfCancellationRequested();
        if (++paragraphs > DocxOutput.MaxBlocks) throw new DocxRejectedException("fn_ai_docx_structure_limit");
        var text = new StringBuilder();
        foreach (var child in Children(paragraph))
        {
            if (child.Name == DocxXml.Word + "pPr") continue;
            if (child.Name == DocxXml.Word + "r") Run(child, text);
            else if (child.Name == DocxXml.Word + "hyperlink")
            {
                // 只提取已显示的文字，不读取关系 ID、URI 或其他目标。
                foreach (var run in Children(child)) { if (run.Name != DocxXml.Word + "r") Unsupported(); Run(run, text); }
            }
            else Unsupported();
        }
        return text.ToString();
    }

    private void Run(XElement run, StringBuilder text)
    {
        foreach (var child in Children(run))
        {
            token.ThrowIfCancellationRequested();
            if (child.Name == DocxXml.Word + "rPr" || child.Name == DocxXml.Word + "lastRenderedPageBreak") continue;
            string value;
            if (child.Name == DocxXml.Word + "t") { if (child.HasElements) Unsupported(); value = child.Value; }
            else if (child.Name == DocxXml.Word + "tab") { if (child.HasElements) Unsupported(); value = "\t"; }
            else if (child.Name == DocxXml.Word + "br" || child.Name == DocxXml.Word + "cr") { if (child.HasElements) Unsupported(); value = "\n"; }
            else { Unsupported(); return; }
            if (value.Length > DocxOutput.MaxCharacters - text.Length) throw new DocxRejectedException("fn_ai_docx_output_limit");
            text.Append(value);
        }
    }

    private void Table(XElement table)
    {
        if (++tables > 128) throw new DocxRejectedException("fn_ai_docx_structure_limit");
        var tableNumber = tables; var rowNumber = 0;
        foreach (var row in Children(table))
        {
            token.ThrowIfCancellationRequested();
            if (row.Name == DocxXml.Word + "tblPr" || row.Name == DocxXml.Word + "tblGrid") continue;
            if (row.Name != DocxXml.Word + "tr") Unsupported();
            if (++rows > 4096) throw new DocxRejectedException("fn_ai_docx_structure_limit");
            rowNumber++;
            var rowCells = new List<XElement>(); var header = false; var hasProperties = false;
            foreach (var child in Children(row))
            {
                if (child.Name == DocxXml.Word + "trPr")
                {
                    if (hasProperties) Unsupported(); hasProperties = true;
                    var headers = child.Elements(DocxXml.Word + "tblHeader").ToArray();
                    if (headers.Length > 1) Unsupported();
                    header = headers.Any(value => (string?)value.Attribute(DocxXml.Word + "val") is not ("0" or "false" or "off")); continue;
                }
                if (child.Name != DocxXml.Word + "tc") Unsupported();
                rowCells.Add(child);
            }
            if (rowCells.Count is 0 or > 64) throw new DocxRejectedException("fn_ai_docx_structure_limit");
            for (var index = 0; index < rowCells.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                if (++cells > DocxOutput.MaxBlocks) throw new DocxRejectedException("fn_ai_docx_structure_limit");
                var cellText = new StringBuilder(); var firstParagraph = paragraphs + 1; var paragraphCount = 0;
                foreach (var child in Children(rowCells[index]))
                {
                    if (child.Name == DocxXml.Word + "tcPr")
                    { if (child.Elements().Any(value => value.Name == DocxXml.Word + "gridSpan" || value.Name == DocxXml.Word + "vMerge" || value.Name == DocxXml.Word + "hMerge")) Unsupported(); continue; }
                    if (child.Name != DocxXml.Word + "p") Unsupported();
                    var value = Paragraph(child);
                    if (value.Length + (paragraphCount > 0 ? 1 : 0) > DocxOutput.MaxCharacters - cellText.Length) throw new DocxRejectedException("fn_ai_docx_output_limit");
                    if (paragraphCount++ > 0) cellText.Append('\n');
                    cellText.Append(value);
                }
                if (paragraphCount == 0) Unsupported();
                output.Add(cellText.ToString(), index + 1 == rowCells.Count ? "\n" : "\t", firstParagraph, tableNumber, rowNumber, index + 1, paragraphCount, header);
            }
        }
        if (rowNumber == 0) Unsupported();
    }

    private static IEnumerable<XElement> Children(XElement element)
    {
        // 不接受游离正文；只保留 XML 排版空白，未知内容不能在 Elements() 遍历时被静默忽略。
        if (element.Nodes().Any(node => node is XText text && !string.IsNullOrWhiteSpace(text.Value))) Unsupported();
        return element.Elements();
    }
    private static void Unsupported() => throw new DocxRejectedException("fn_ai_docx_unsupported_structure");
}
