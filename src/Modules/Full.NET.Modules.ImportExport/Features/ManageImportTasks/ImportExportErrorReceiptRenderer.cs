using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Full.NET.Abstractions.Text;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>将失败执行行渲染为固定结构的 Open XML 错误回执工作簿。</summary>
internal static class ImportExportErrorReceiptRenderer
{
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    private static readonly string[] ReceiptHeaders =
    [
        "lineNumber",
        "errorCode",
        "message",
    ];

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>生成仅包含失败行的 xlsx 错误回执。</summary>
    public static byte[] Render(IReadOnlyList<StaticImportRowExecutionResult> failedRows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failedRows);
        ImportExportErrorReceiptBudget.Validate(failedRows, cancellationToken);
        var rows = failedRows.Select(row => new[]
        {
            EscapeFormulaText(row.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            EscapeFormulaText(row.ErrorCode ?? string.Empty),
            EscapeFormulaText(row.Message ?? string.Empty),
        });
        return CreateWorkbook(ReceiptHeaders, rows, cancellationToken);
    }

    private static byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        using var output = new ImportExportErrorReceiptOutputStream(cancellationToken);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                new XDocument(
                    new XElement(
                        XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
                        new XElement(
                            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default",
                            new XAttribute("Extension", "rels"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                        new XElement(
                            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default",
                            new XAttribute("Extension", "xml"),
                            new XAttribute("ContentType", "application/xml")),
                        new XElement(
                            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Override",
                            new XAttribute("PartName", "/xl/workbook.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                        new XElement(
                            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Override",
                            new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            WriteEntry(
                archive,
                "_rels/.rels",
                new XDocument(
                    new XElement(
                        PackageRelationshipNamespace + "Relationships",
                        new XElement(
                            PackageRelationshipNamespace + "Relationship",
                            new XAttribute("Id", "rId1"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                            new XAttribute("Target", "xl/workbook.xml")))));
            WriteEntry(
                archive,
                "xl/workbook.xml",
                new XDocument(
                    new XElement(
                        SpreadsheetNamespace + "workbook",
                        new XAttribute(XNamespace.Xmlns + "r", RelationshipNamespace),
                        new XElement(
                            SpreadsheetNamespace + "sheets",
                            new XElement(
                                SpreadsheetNamespace + "sheet",
                                new XAttribute("name", "Errors"),
                                new XAttribute("sheetId", "1"),
                                new XAttribute(RelationshipNamespace + "id", "rId1"))))));
            WriteEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                new XDocument(
                    new XElement(
                        PackageRelationshipNamespace + "Relationships",
                        new XElement(
                            PackageRelationshipNamespace + "Relationship",
                            new XAttribute("Id", "rId1"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                            new XAttribute("Target", "worksheets/sheet1.xml")))));

            // 回执逐行写入，不建立与全部失败文本同大小的 XML 对象树。
            var entry = archive.CreateEntry(WorksheetPath, CompressionLevel.Fastest);
            using var sheetStream = entry.Open();
            using var writer = XmlWriter.Create(sheetStream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
            writer.WriteStartElement("worksheet", SpreadsheetNamespace.NamespaceName);
            writer.WriteStartElement("sheetData", SpreadsheetNamespace.NamespaceName);
            var rowNumber = 1;
            WriteRow(writer, headers, rowNumber++, cancellationToken);
            foreach (var row in rows) WriteRow(writer, row, rowNumber++, cancellationToken);
            writer.WriteEndElement(); writer.WriteEndElement();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private static void WriteRow(XmlWriter writer, IReadOnlyList<string> values, int rowNumber, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        writer.WriteStartElement("row", SpreadsheetNamespace.NamespaceName);
        writer.WriteAttributeString("r", rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 0; index < values.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            writer.WriteStartElement("c", SpreadsheetNamespace.NamespaceName);
            writer.WriteAttributeString("r", CellReference(index, rowNumber));
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", SpreadsheetNamespace.NamespaceName);
            writer.WriteStartElement("t", SpreadsheetNamespace.NamespaceName);
            writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
            writer.WriteString(XmlTextSanitizer.Sanitize(values[index], token));
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteEntry(
        ZipArchive archive,
        string path,
        XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
        });
        document.Save(writer);
    }

    private static string EscapeFormulaText(string value) =>
        !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;

    private static string CellReference(int columnIndex, int rowNumber) =>
        $"{(char)('A' + columnIndex)}{rowNumber}";
}
