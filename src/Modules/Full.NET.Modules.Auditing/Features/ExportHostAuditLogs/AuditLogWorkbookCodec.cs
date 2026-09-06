using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Full.NET.Modules.Auditing.Features.ExportHostAuditLogs;

/// <summary>审计日志导出专用 Open XML 工作簿编码器（仅导出，不解析上传）。</summary>
internal static class AuditLogWorkbookCodec
{
    internal const int MaximumExportRows = 20_000;

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Export(
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var materializedRows = rows.Take(MaximumExportRows + 1).ToArray();
        if (materializedRows.Length > MaximumExportRows)
        {
            throw new InvalidOperationException("Export row count exceeds workbook limit.");
        }

        return CreateWorkbook(sheetName, headers, materializedRows);
    }

    internal static string EscapeFormulaText(string value) =>
        !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;

    private static byte[] CreateWorkbook(
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var output = new MemoryStream();
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
                                new XAttribute("name", SanitizeSheetName(sheetName)),
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

            var allRows = new[] { headers }.Concat(rows).ToArray();
            var sheetRows = allRows.Select((row, rowIndex) =>
                new XElement(
                    SpreadsheetNamespace + "row",
                    new XAttribute("r", rowIndex + 1),
                    row.Select((value, columnIndex) => CreateInlineCell(
                        CellReference(columnIndex, rowIndex + 1),
                        SanitizeXmlText(value)))));
            WriteEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                new XDocument(
                    new XElement(
                        SpreadsheetNamespace + "worksheet",
                        new XElement(SpreadsheetNamespace + "sheetData", sheetRows))));
        }

        return output.ToArray();
    }

    private static string SanitizeSheetName(string value)
    {
        var sanitized = string.Concat(value.Where(character => character is not ':' and not '\\' and not '/' and not '?' and not '*' and not '[' and not ']'));
        return sanitized.Length > 31 ? sanitized[..31] : sanitized;
    }

    private static XElement CreateInlineCell(string reference, string value) =>
        new(
            SpreadsheetNamespace + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(
                SpreadsheetNamespace + "is",
                new XElement(SpreadsheetNamespace + "t", value)));

    private static void WriteEntry(ZipArchive archive, string entryName, XDocument document)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
        });
        document.Save(writer);
    }

    private static string SanitizeXmlText(string? value) =>
        string.Concat((value ?? string.Empty).Where(XmlConvert.IsXmlChar));

    private static string CellReference(int columnIndex, int rowNumber)
    {
        var column = string.Empty;
        var index = columnIndex;
        do
        {
            column = (char)('A' + (index % 26)) + column;
            index = (index / 26) - 1;
        }
        while (index >= 0);

        return $"{column}{rowNumber.ToString(CultureInfo.InvariantCulture)}";
    }
}
