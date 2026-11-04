using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Full.NET.Abstractions.Text;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

/// <summary>
/// 为租户职位导入导出提供固定结构的 Open XML 工作簿边界。
/// </summary>
/// <remarks>
/// 该类型只理解本功能的固定列，不承担通用 Excel 映射职责；严格限制压缩包、XML、行数并拒绝公式，
/// 防止不可信工作簿借助公式、外部关系或压缩膨胀扩大服务端攻击面。
/// </remarks>
internal static class OrganizationPositionWorkbookCodec
{
    internal const int MaximumDataRows = 1_000;
    internal const long MaximumUploadBytes = 1024 * 1024;

    private const int MaximumArchiveEntries = 32;
    private const long MaximumUncompressedBytes = 4 * 1024 * 1024;
    private const long MaximumXmlCharacters = 2 * 1024 * 1024;
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private const string SharedStringsPath = "xl/sharedStrings.xml";

    private static readonly string[] ImportHeaders =
    [
        "code",
        "name",
        "displayOrder",
        "unitCode",
        "positionLevelCode",
    ];

    private static readonly string[] ExportHeaders =
    [
        "code",
        "name",
        "displayOrder",
        "unitCode",
        "unitName",
        "positionLevelCode",
        "positionLevelName",
        "isActive",
        "createdAtUtc",
    ];

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] CreateImportTemplate() =>
        CreateWorkbook(ImportHeaders, []);

    public static byte[] Export(IReadOnlyList<OrganizationPositionResponse> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var rows = positions.Select(position => new[]
        {
            EscapeFormulaText(position.Code),
            EscapeFormulaText(position.Name),
            position.DisplayOrder.ToString(CultureInfo.InvariantCulture),
            EscapeFormulaText(position.UnitCode ?? string.Empty),
            EscapeFormulaText(position.UnitName ?? string.Empty),
            EscapeFormulaText(position.PositionLevelCode ?? string.Empty),
            EscapeFormulaText(position.PositionLevelName ?? string.Empty),
            position.IsActive ? "true" : "false",
            position.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });
        return CreateWorkbook(ExportHeaders, rows);
    }

    public static async Task<IReadOnlyList<ImportOrganizationPositionRow>> ParseImportAsync(
        Stream source,
        long declaredLength,
        CancellationToken cancellationToken)
        => (await ParseImportRowsAsync(source, declaredLength, cancellationToken).ConfigureAwait(false))
            .Select(row => row.Row).ToArray();

    /// <summary>持久任务保留物理行号，列映射只改变取值位置，不改变恢复幂等标识。</summary>
    internal static async Task<IReadOnlyList<ParsedPositionWorkbookRow>> ParseImportRowsAsync(
        Stream source,
        long declaredLength,
        CancellationToken cancellationToken,
        ImportColumnMappingRequest? columnMapping = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (declaredLength is <= 0 or > MaximumUploadBytes)
        {
            throw new InvalidDataException("职位导入工作簿大小必须在 1 字节到 1 MiB 之间。");
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
                throw new InvalidDataException("职位导入工作簿超过 1 MiB 上限。");
            }

            await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        if (copied != declaredLength)
        {
            throw new InvalidDataException("职位导入工作簿实际大小与声明大小不一致。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        buffered.Position = 0;
        using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true);
        await ValidateArchiveAsync(archive, cancellationToken).ConfigureAwait(false);
        var columnBudget = columnMapping is null ? ImportHeaders.Length : StaticImportHeaderMapping.MaximumSourceColumns;
        var sharedStrings = await ReadSharedStringsAsync(archive, columnBudget, cancellationToken).ConfigureAwait(false);
        var worksheet = await ReadXmlEntryAsync(archive, WorksheetPath, required: true, cancellationToken).ConfigureAwait(false);
        return ParseRows(worksheet, sharedStrings, columnBudget, columnMapping, cancellationToken);
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
                                new XAttribute("name", "Positions"),
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

    /// <summary>保持条目、唯一部件路径、解压声明大小和外部关系限制，逐条核对取消。</summary>
    /// <param name="archive">已缓冲且只读的工作簿 ZIP。</param>
    /// <param name="cancellationToken">本次导入取消令牌。</param>
    private static async Task ValidateArchiveAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (archive.Entries.Count is 0 or > MaximumArchiveEntries)
        {
            throw new InvalidDataException("职位导入工作簿包含非法数量的压缩条目。");
        }

        long totalLength = 0;
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.Add(entry.FullName)
                || entry.FullName.Contains("..", StringComparison.Ordinal)
                || entry.FullName.StartsWith("/", StringComparison.Ordinal)
                || entry.FullName.StartsWith('\\'))
            {
                throw new InvalidDataException("职位导入工作簿包含非法压缩路径。");
            }

            totalLength += entry.Length;
            if (entry.Length > MaximumUncompressedBytes
                || totalLength > MaximumUncompressedBytes)
            {
                throw new InvalidDataException("职位导入工作簿解压后超过 4 MiB 上限。");
            }

            if (entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                var relationships = await ReadXmlEntryAsync(archive, entry.FullName, required: true, cancellationToken).ConfigureAwait(false);
                if (relationships
                    .Descendants(PackageRelationshipNamespace + "Relationship")
                    .Any(node => string.Equals(
                        (string?)node.Attribute("TargetMode"),
                        "External",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("职位导入工作簿不得包含外部关系。");
                }
            }
        }
    }

    /// <summary>读取有界共享字符串，超出条目数时不继续物化剩余字符串。</summary>
    /// <param name="archive">工作簿 ZIP。</param>
    /// <param name="cancellationToken">本次导入取消令牌。</param>
    private static async Task<IReadOnlyList<string>> ReadSharedStringsAsync(ZipArchive archive, int columnBudget, CancellationToken cancellationToken)
    {
        var document = await ReadXmlEntryAsync(archive, SharedStringsPath, required: false, cancellationToken).ConfigureAwait(false);
        if (document.Root is null)
        {
            return [];
        }

        var values = document
            .Descendants(SpreadsheetNamespace + "si")
            .Take((MaximumDataRows + 1) * columnBudget + 1)
            .Select(item => string.Concat(
                item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
        if (values.Length > (MaximumDataRows + 1) * columnBudget)
        {
            throw new InvalidDataException("职位导入工作簿共享字符串数量超过上限。");
        }

        return values;
    }

    private static IReadOnlyList<ParsedPositionWorkbookRow> ParseRows(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings,
        int columnBudget,
        ImportColumnMappingRequest? columnMapping,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 只解释固定工作表的直接数据行，不能从其他根或嵌套结构提取伪工作簿。
        if (worksheet.Root?.Name != SpreadsheetNamespace + "worksheet")
            throw new InvalidDataException("导入工作簿缺少有效工作表根。");
        var rows = worksheet.Root.Element(SpreadsheetNamespace + "sheetData")?
            .Elements(SpreadsheetNamespace + "row").ToArray() ?? [];
        if (rows.Length == 0 || rows.Length > MaximumDataRows + 1)
        {
            throw new InvalidDataException("职位导入工作簿缺少表头。");
        }

        var headerLine = RowNumber(rows[0], 1);
        if (headerLine != 1) throw new InvalidDataException("职位导入表头必须位于第一行。");
        var headers = ReadRow(rows[0], sharedStrings, columnBudget, headerLine, cancellationToken);
        IReadOnlyList<int> layout;
        if (columnMapping is not null)
            layout = StaticImportHeaderMapping.Bind(ImportHeaders, headers, columnMapping);
        else if (!headers.SequenceEqual(ImportHeaders, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"职位导入工作簿表头必须严格为：{string.Join(", ", ImportHeaders)}。");
        }
        else layout = Enumerable.Range(0, ImportHeaders.Length).ToArray();

        var requests = new List<ParsedPositionWorkbookRow>(Math.Min(rows.Length - 1, MaximumDataRows));
        var previousLine = 1;
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var physicalLine = RowNumber(row, previousLine + 1);
            if (physicalLine <= previousLine || physicalLine > MaximumDataRows + 1)
                throw new InvalidDataException("职位导入工作簿行号无效或超过上限。");
            previousLine = physicalLine;
            var sourceCells = ReadRow(row, sharedStrings, columnBudget, physicalLine, cancellationToken);
            var cells = layout.Select(index => sourceCells[index]).ToArray();
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (requests.Count >= MaximumDataRows)
            {
                throw new InvalidDataException($"职位导入工作簿最多允许 {MaximumDataRows} 行数据。");
            }

            requests.Add(new ParsedPositionWorkbookRow(physicalLine, new ImportOrganizationPositionRow(
                Cell(cells, 0),
                Cell(cells, 1),
                ParseDisplayOrder(Cell(cells, 2)),
                NullIfEmpty(Cell(cells, 3)),
                NullIfEmpty(Cell(cells, 4)))));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return requests;
    }

    private static int ParseDisplayOrder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 10;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order)
            || order is < 0 or > 999_999)
        {
            throw new InvalidDataException("职位导入工作簿 displayOrder 必须是 0 到 999999 之间的整数。");
        }

        return order;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>固定列按原始单元格身份读取，拒绝重复列覆盖和明确的行地址冲突。</summary>
    /// <param name="row">当前原始 XML 行。</param>
    /// <param name="sharedStrings">已验证的共享字符串。</param>
    /// <param name="cancellationToken">本次导入取消令牌。</param>
    private static IReadOnlyList<string> ReadRow(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        int columnBudget,
        int physicalLine, CancellationToken cancellationToken)
    {
        var values = Enumerable.Repeat(string.Empty, columnBudget).ToArray();
        var seen = new HashSet<int>();
        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cell.Element(SpreadsheetNamespace + "f") is not null)
            {
                throw new InvalidDataException("职位导入工作簿不得包含公式单元格。");
            }

            var reference = (string?)cell.Attribute("r") ?? string.Empty;
            var columnIndex = ColumnIndex(reference, columnBudget, physicalLine);
            if (!seen.Add(columnIndex)) throw new InvalidDataException("职位导入工作簿包含重复单元格。");

            values[columnIndex] = ReadCellValue(cell, sharedStrings);
        }

        return values;
    }

    /// <summary>只解释固定模板支持的文本与数字编码，Excel 错误或未知类型不能被当作业务文本。</summary>
    /// <param name="cell">当前单元格 XML。</param>
    /// <param name="sharedStrings">已核对的共享字符串。</param>
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
            throw new InvalidDataException("导入工作簿包含不支持的单元格类型。");

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 0
            || index >= sharedStrings.Count)
        {
            throw new InvalidDataException("职位导入工作簿包含非法共享字符串索引。");
        }

        return sharedStrings[index];
    }

    /// <summary>异步加载 XML 时传递取消，DTD、实体和文档字符限制保持启用。</summary>
    /// <param name="archive">工作簿 ZIP。</param>
    /// <param name="path">固定工作簿部件路径。</param>
    /// <param name="required">是否必须存在该部件。</param>
    /// <param name="cancellationToken">本次导入取消令牌。</param>
    private static async Task<XDocument> ReadXmlEntryAsync(
        ZipArchive archive,
        string path,
        bool required,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            if (required)
            {
                throw new InvalidDataException($"职位导入工作簿缺少必要部件 {path}。");
            }

            return new XDocument();
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaximumXmlCharacters,
            XmlResolver = null,
        });
        try
        {
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return document;
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"职位导入工作簿部件 {path} 不是有效 XML。", exception);
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

    private static string EscapeFormulaText(string value) =>
        !string.IsNullOrEmpty(value) && value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;

    // 职位、机构与职级显示名保留合法代理对，只删除 XML 不允许的字符。
    private static string SanitizeXmlText(string? value) =>
        XmlTextSanitizer.Sanitize(value ?? string.Empty);

    private static string CellReference(int columnIndex, int rowNumber) =>
        $"{(char)('A' + columnIndex)}{rowNumber}";

    private static int ColumnIndex(string reference, int columnBudget, int expectedRow)
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

            // 在乘法前限制宽度，映射模式也不能用超长引用绕过有界解析。
            if (column >= columnBudget || index >= 2)
            {
                throw new InvalidDataException("职位导入工作簿包含非法单元格引用。");
            }

            column = (column * 26) + ordinal;
            if (column > columnBudget)
            {
                throw new InvalidDataException("职位导入工作簿包含未知列。");
            }

            index++;
        }

        if (index == 0
            || index == reference.Length
            || reference.AsSpan(index).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new InvalidDataException("职位导入工作簿包含非法单元格引用。");
        }

        if (!int.TryParse(
                reference.AsSpan(index),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rowNumber)
            || rowNumber != expectedRow)
        {
            throw new InvalidDataException("职位导入工作簿包含非法单元格引用。");
        }

        return column - 1;
    }

    private static string Cell(IReadOnlyList<string> cells, int index) =>
        index < cells.Count ? cells[index] : string.Empty;

    private static int RowNumber(XElement row, int fallback) =>
        row.Attribute("r") is not { } attribute ? fallback :
        int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number : throw new InvalidDataException("职位导入工作簿包含非法行号。");
}

/// <summary>物理行号在列重排及跳过空白行后仍保持稳定。</summary>
internal sealed record ParsedPositionWorkbookRow(int LineNumber, ImportOrganizationPositionRow Row);
