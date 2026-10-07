using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>
/// 为租户企业申请导入导出提供固定结构的 Open XML 工作簿边界。
/// </summary>
/// <remarks>
/// 该类型只理解本功能的固定列，不承担通用 Excel 映射职责；严格限制压缩包、XML、行数并拒绝公式，
/// 防止不可信工作簿借助公式、外部关系或压缩膨胀扩大服务端攻击面。
/// </remarks>
internal static class EnterpriseRequestWorkbookCodec
{
    internal const int MaximumDataRows = 1_000;
    internal const long MaximumUploadBytes = 1024 * 1024;

    private const int MaximumArchiveEntries = 32;
    private const long MaximumUncompressedBytes = 4 * 1024 * 1024;
    private const long MaximumXmlCharacters = 2 * 1024 * 1024;
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private const string SharedStringsPath = "xl/sharedStrings.xml";

    internal static readonly string[] ImportHeaders =
        ["requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId"];

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] CreateImportTemplate() =>
        CreateWorkbook(ImportHeaders, []);

    public static async Task<IReadOnlyList<EnterpriseRequestWorkbookRow>> ParseImportAsync(
        Stream source,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (declaredLength is <= 0 or > MaximumUploadBytes)
        {
            throw new InvalidDataException("企业申请导入工作簿大小必须在 1 字节到 1 MiB 之间。");
        }

        await using var buffered = new MemoryStream((int)declaredLength);
        var buffer = new byte[16 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            copied += read;
            if (copied > MaximumUploadBytes)
            {
                throw new InvalidDataException("企业申请导入工作簿超过 1 MiB 上限。");
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        if (copied != declaredLength)
        {
            throw new InvalidDataException("企业申请导入工作簿实际大小与声明大小不一致。");
        }

        buffered.Position = 0;
        using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchive(archive);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheet = ReadXmlEntry(archive, WorksheetPath, required: true);
        return ParseRows(worksheet, sharedStrings, cancellationToken);
    }

    private static byte[] CreateWorkbook(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
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
                                new XAttribute("name", "Requests"),
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
                        value))));
            WriteEntry(
                archive,
                WorksheetPath,
                new XDocument(
                    new XElement(
                        SpreadsheetNamespace + "worksheet",
                        new XElement(SpreadsheetNamespace + "sheetData", sheetRows))));
        }

        return output.ToArray();
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumArchiveEntries)
        {
            throw new InvalidDataException("企业申请导入工作簿包含非法数量的压缩条目。");
        }

        long totalLength = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!paths.Add(entry.FullName)
                || entry.FullName.Contains("..", StringComparison.Ordinal)
                || entry.FullName.StartsWith("/", StringComparison.Ordinal)
                || entry.FullName.StartsWith('\\'))
            {
                throw new InvalidDataException("企业申请导入工作簿包含非法压缩路径。");
            }

            totalLength += entry.Length;
            if (entry.Length > MaximumUncompressedBytes
                || totalLength > MaximumUncompressedBytes)
            {
                throw new InvalidDataException("企业申请导入工作簿解压后超过 4 MiB 上限。");
            }

            if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                var relationships = ReadXmlEntry(archive, entry.FullName, required: true);
                if (relationships
                    .Descendants(PackageRelationshipNamespace + "Relationship")
                    .Any(node => string.Equals(
                        (string?)node.Attribute("TargetMode"),
                        "External",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("企业申请导入工作簿不得包含外部关系。");
                }
            }
        }
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var document = ReadXmlEntry(archive, SharedStringsPath, required: false);
        if (document.Root is null)
        {
            return [];
        }

        var values = document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(
                item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
        if (values.Length > (MaximumDataRows + 1) * ImportHeaders.Length)
        {
            throw new InvalidDataException("企业申请导入工作簿共享字符串数量超过上限。");
        }

        return values;
    }

    private static IReadOnlyList<EnterpriseRequestWorkbookRow> ParseRows(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings,
        CancellationToken cancellationToken)
    {
        if (worksheet.Root?.Name != SpreadsheetNamespace + "worksheet")
            throw new InvalidDataException("企业申请工作簿缺少工作表。");
        var rows = worksheet.Root.Element(SpreadsheetNamespace + "sheetData")?.Elements(SpreadsheetNamespace + "row").ToArray() ?? [];
        if (rows.Length == 0 || rows.Length > MaximumDataRows + 1)
            throw new InvalidDataException("企业申请工作簿缺少表头或超过行数上限。");
        var requests = new List<EnterpriseRequestWorkbookRow>();
        var previousLine = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var line)
                || line <= previousLine || line > 1_048_576)
                throw new InvalidDataException("企业申请工作簿行号必须严格递增且有效。");
            previousLine = line;
            var cells = ReadRow(row, sharedStrings);
            if (requests.Count == 0 && ReferenceEquals(row, rows[0]))
            {
                if (line != 1 || !cells.SequenceEqual(ImportHeaders, StringComparer.Ordinal))
                    throw new InvalidDataException("企业申请工作簿表头与模板不一致。");
                continue;
            }
            if (!cells.All(string.IsNullOrWhiteSpace)) requests.Add(new(line, cells));
        }
        return requests;
    }

    private static IReadOnlyList<string> ReadRow(
        XElement row,
        IReadOnlyList<string> sharedStrings)
    {
        var values = Enumerable.Repeat(string.Empty, ImportHeaders.Length).ToArray();
        var occupied = new HashSet<int>();
        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            if (cell.Element(SpreadsheetNamespace + "f") is not null)
            {
                throw new InvalidDataException("企业申请导入工作簿不得包含公式单元格。");
            }

            var reference = (string?)cell.Attribute("r") ?? string.Empty;
            var columnIndex = ColumnIndex(reference);
            if (columnIndex < 0 || columnIndex >= ImportHeaders.Length)
            {
                var extraValue = ReadCellValue(cell, sharedStrings);
                if (!string.IsNullOrWhiteSpace(extraValue))
                {
                    throw new InvalidDataException("企业申请导入工作簿包含未知列。");
                }

                continue;
            }

            if (!occupied.Add(columnIndex)
                || !string.Equals(reference[1..], (string?)row.Attribute("r"), StringComparison.Ordinal))
                throw new InvalidDataException("企业申请工作簿包含重复或错位单元格。");
            values[columnIndex] = ReadCellValue(cell, sharedStrings);
        }

        return values;
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(
                cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
        }

        var value = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
        if (type is null or "n" or "str") return value;
        if (!string.Equals(type, "s", StringComparison.Ordinal))
            throw new InvalidDataException("企业申请工作簿包含不支持的单元格类型。");

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 0
            || index >= sharedStrings.Count)
        {
            throw new InvalidDataException("企业申请导入工作簿包含非法共享字符串索引。");
        }

        return sharedStrings[index];
    }

    private static XDocument ReadXmlEntry(
        ZipArchive archive,
        string path,
        bool required)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            if (required)
            {
                throw new InvalidDataException($"企业申请导入工作簿缺少必要部件 {path}。");
            }

            return new XDocument();
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = false,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaximumXmlCharacters,
            XmlResolver = null,
        });
        try
        {
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"企业申请导入工作簿部件 {path} 不是有效 XML。", exception);
        }
    }

    private static XElement CreateInlineCell(string reference, string value) =>
        new(
            SpreadsheetNamespace + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(
                SpreadsheetNamespace + "is",
                new XElement(
                    SpreadsheetNamespace + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    SanitizeXmlText(value))));

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

    private static string SanitizeXmlText(string? value) =>
        string.Concat((value ?? string.Empty).Where(XmlConvert.IsXmlChar));

    private static string CellReference(int columnIndex, int rowNumber) =>
        $"{(char)('A' + columnIndex)}{rowNumber}";

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        var column = 0;
        while (index < reference.Length)
        {
            var character = reference[index];
            var ordinal = character switch
            {
                >= 'A' and <= 'Z' => character - 'A' + 1,
                >= 'a' and <= 'z' => character - 'a' + 1,
                _ => 0,
            };
            if (ordinal == 0)
            {
                break;
            }

            // 固定模板只有五列；先限制列序号，避免恶意超长引用触发整数溢出。
            if (column >= ImportHeaders.Length)
            {
                throw new InvalidDataException("企业申请导入工作簿包含非法单元格引用。");
            }

            column = (column * 26) + ordinal;
            if (column > ImportHeaders.Length)
            {
                throw new InvalidDataException("企业申请导入工作簿包含未知列。");
            }

            index++;
        }

        if (index == 0
            || index == reference.Length
            || reference.AsSpan(index).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new InvalidDataException("企业申请导入工作簿包含非法单元格引用。");
        }

        if (!int.TryParse(
                reference.AsSpan(index),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rowNumber)
            || rowNumber < 1)
        {
            throw new InvalidDataException("企业申请导入工作簿包含非法单元格引用。");
        }

        return column - 1;
    }

}

internal sealed record EnterpriseRequestWorkbookRow(int LineNumber, IReadOnlyList<string> Fields);
