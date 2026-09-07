using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>将报表查询结果渲染为 Open XML Excel 工作簿；对单元格文本做公式注入转义。</summary>
internal static class ReportingExcelExportRenderer
{
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private const string WorksheetName = "Export";

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>根据可见列与数据行生成 xlsx 字节数组。</summary>
    /// <param name="columns">允许导出的可见列。</param>
    /// <param name="rows">已收集且仍需预算验证的数据行。</param>
    public static byte[] Render(
        IReadOnlyList<ReportingExecutionColumnDefinition> columns,
        IReadOnlyList<ReportingExecutionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var budget = new ReportingExportBudget();
        budget.AddColumns(columns);
        foreach (var row in rows) budget.AddRow(row);

        var headers = columns.Select(column => EscapeFormulaText(column.DisplayName)).ToArray();
        var dataRows = rows.Select(row => columns
            .Select(column => EscapeFormulaText(
                row.Values.TryGetValue(column.ColumnKey, out var value) ? value : null))
            .ToArray());
        return CreateWorkbook(headers, dataRows);
    }

    /// <summary>生成固定元数据和流式工作表。</summary>
    /// <param name="headers">已转义的表头。</param>
    /// <param name="rows">延迟枚举的数据行。</param>
    private static byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        using var output = new ReportingExportOutputStream();
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
                                new XAttribute("name", WorksheetName),
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

            // 工作表逐行写入 ZIP；不建立与全部单元格等大的 XML 对象树。
            var entry = archive.CreateEntry(WorksheetPath, CompressionLevel.Fastest);
            using var sheetStream = entry.Open();
            using var writer = XmlWriter.Create(sheetStream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                CloseOutput = false,
            });
            writer.WriteStartElement("worksheet", SpreadsheetNamespace.NamespaceName);
            writer.WriteStartElement("sheetData", SpreadsheetNamespace.NamespaceName);
            var rowNumber = 1;
            WriteRow(writer, headers, rowNumber++);
            foreach (var row in rows) WriteRow(writer, row, rowNumber++);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return output.ToArray();
    }

    /// <summary>逐个写入单元格，保留文本空白并避免公式执行。</summary>
    /// <param name="writer">当前工作表 XML 写入器。</param>
    /// <param name="values">当前行的单元格文本。</param>
    /// <param name="rowNumber">从一开始的工作表行号。</param>
    private static void WriteRow(XmlWriter writer, IReadOnlyList<string?> values, int rowNumber)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace.NamespaceName);
        writer.WriteAttributeString("r", rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 0; index < values.Count; index++)
        {
            writer.WriteStartElement("c", SpreadsheetNamespace.NamespaceName);
            writer.WriteAttributeString("r", CellReference(index, rowNumber));
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", SpreadsheetNamespace.NamespaceName);
            writer.WriteStartElement("t", SpreadsheetNamespace.NamespaceName);
            writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
            writer.WriteString(SanitizeXmlText(values[index] ?? string.Empty));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    /// <summary>写入大小固定的工作簿元数据。</summary>
    /// <param name="archive">目标压缩包。</param>
    /// <param name="path">条目路径。</param>
    /// <param name="document">元数据文档。</param>
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

    /// <summary>对以公式前缀开头的文本加单引号，防止 Excel 公式注入。</summary>
    /// <param name="value">原始单元格文本。</param>
    internal static string EscapeFormulaText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
    }

    /// <summary>移除 XML 不支持的字符。</summary>
    /// <param name="value">待清理文本。</param>
    private static string SanitizeXmlText(string value) =>
        string.Concat(value.Where(XmlConvert.IsXmlChar));

    /// <summary>计算 Excel 单元格坐标。</summary>
    /// <param name="columnIndex">从零开始的列号。</param>
    /// <param name="rowNumber">从一开始的行号。</param>
    private static string CellReference(int columnIndex, int rowNumber)
    {
        var columnName = string.Empty;
        var index = columnIndex;
        do
        {
            columnName = (char)('A' + index % 26) + columnName;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return $"{columnName}{rowNumber}";
    }
}
