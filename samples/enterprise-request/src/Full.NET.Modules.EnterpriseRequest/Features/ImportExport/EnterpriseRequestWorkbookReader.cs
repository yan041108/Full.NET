using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>读取申请单固定五列 XLSX；限制压缩、XML、行列并拒绝公式和外部关系。</summary>
internal static class EnterpriseRequestWorkbookReader
{
    internal static readonly string[] Headers = ["requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId"];
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string RowInvalid = "enterprise_request.import.row_invalid";

    internal static async Task<IReadOnlyList<Row>> ReadAsync(Stream stream, long length, CancellationToken token, ImportColumnMappingRequest? columnMapping = null)
    {
        if (length is <= 0 or > 1024 * 1024) throw new InvalidDataException("Invalid workbook size.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > length) throw new InvalidDataException("Workbook exceeds declared size.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != length) throw new InvalidDataException("Workbook size mismatch.");
        buffer.Position = 0;
        try
        {
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count > 32 || archive.Entries.Sum(entry => entry.Length) > 4 * 1024 * 1024
                || archive.Entries.Any(entry => entry.Length > 2 * 1024 * 1024
                    || entry.FullName.Contains('\\') || entry.FullName.StartsWith('/')
                    || entry.FullName.Split('/').Contains("..") || entry.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
                throw new InvalidDataException("Invalid workbook archive.");
            foreach (var relation in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.Ordinal)))
                if (Load(relation).Descendants().Any(element => string.Equals((string?)element.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("External relationships are forbidden.");
            var workbook = Load(Required(archive, "xl/workbook.xml"));
            if (workbook.Descendants(Ns + "sheet").Count() != 1) throw new InvalidDataException("Exactly one worksheet is required.");
            _ = Load(Required(archive, "[Content_Types].xml"));
            var relations = Load(Required(archive, "xl/_rels/workbook.xml.rels")).Root?.Elements()
                .Where(element => ((string?)element.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true).ToArray() ?? [];
            if (relations.Length != 1 || (string?)relations[0].Attribute("Target") is not ("worksheets/sheet1.xml" or "/xl/worksheets/sheet1.xml"))
                throw new InvalidDataException("Unsupported worksheet relationship.");
            var sheet = Load(Required(archive, "xl/worksheets/sheet1.xml"));
            if (sheet.Descendants(Ns + "f").Any()) throw new InvalidDataException("Formulas are forbidden.");
            var strings = archive.GetEntry("xl/sharedStrings.xml") is { } shared
                ? Load(shared).Descendants(Ns + "si").Select(element => string.Concat(element.Descendants(Ns + "t").Select(text => text.Value))).ToArray() : [];
            var columnBudget = columnMapping is null ? Headers.Length : StaticImportHeaderMapping.MaximumSourceColumns;
            if (strings.Length > 1001 * columnBudget) throw new InvalidDataException("Too many shared strings.");
            var sheetRows = sheet.Descendants(Ns + "row").Take(1002).ToArray();
            if (sheetRows.Length is 0 or > 1001) throw new InvalidDataException("Invalid workbook row count.");
            var rows = new List<Row>();
            var previousLine = 0;
            IReadOnlyList<int>? layout = null;
            for (var index = 0; index < sheetRows.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var row = sheetRows[index];
                var line = row.Attribute("r") is { } position ? int.Parse(position.Value, CultureInfo.InvariantCulture) : previousLine + 1;
                if (line <= previousLine || line > 1001) throw new InvalidDataException("Invalid worksheet row position.");
                previousLine = line;
                var values = new string[columnBudget];
                Array.Fill(values, string.Empty);
                var occupied = new HashSet<int>();
                foreach (var cell in row.Elements(Ns + "c"))
                {
                    if ((string?)cell.Attribute("t") is not (null or "n" or "s" or "str" or "inlineStr"))
                        throw new InvalidDataException("Unsupported cell type.");
                    var reference = (string?)cell.Attribute("r") ?? throw new InvalidDataException("Missing cell reference.");
                    var prefix = 0; var column = 0;
                    while (prefix < reference.Length && reference[prefix] is >= 'A' and <= 'Z')
                    {
                        if (prefix >= 2) throw new InvalidDataException("Cell column exceeds its budget.");
                        column = column * 26 + reference[prefix++] - 'A' + 1;
                        if (column > columnBudget) throw new InvalidDataException("Cell column exceeds its budget.");
                    }
                    if (prefix == 0 || column == 0 || !int.TryParse(reference.AsSpan(prefix), out var cellLine) || cellLine != line
                        || !occupied.Add(column - 1)) throw new InvalidDataException("Invalid or duplicate cell reference.");
                    var text = (string?)cell.Attribute("t") == "inlineStr"
                        ? string.Concat(cell.Descendants(Ns + "t").Select(element => element.Value)) : cell.Element(Ns + "v")?.Value ?? string.Empty;
                    if ((string?)cell.Attribute("t") == "s")
                    {
                        if (!int.TryParse(text, out var stringIndex) || stringIndex < 0 || stringIndex >= strings.Length)
                            throw new InvalidDataException("Invalid shared string reference.");
                        text = strings[stringIndex];
                    }
                    if (text.Length > 2048) throw new InvalidDataException("Cell text exceeds budget.");
                    values[column - 1] = text;
                }
                if (index == 0)
                {
                    if (line != 1) throw new InvalidDataException("Workbook header row must be first.");
                    if (columnMapping is null)
                    {
                        if (!values.SequenceEqual(Headers)) throw new InvalidDataException("Workbook headers do not match.");
                    }
                    else layout = StaticImportHeaderMapping.Bind(Headers, values, columnMapping);
                }
                else
                {
                    var canonical = layout is null ? values : layout.Select(column => values[column]).ToArray();
                    if (columnMapping is null ? canonical.Any(value => value.Length > 0) : canonical.Any(value => !string.IsNullOrWhiteSpace(value)))
                        rows.Add(Parse(line, canonical));
                }
            }
            return rows;
        }
        catch (Exception error) when (error is XmlException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid workbook structure.", error);
        }
    }

    private static Row Parse(int line, string[] values)
    {
        // 预览与执行使用同一字段边界；不把解析成功或有效 GUID 当作数据权限授权。
        var valid = !string.IsNullOrWhiteSpace(values[0]) && values[0].Length <= 64
            && !string.IsNullOrWhiteSpace(values[1]) && values[1].Length <= 200;
        valid &= decimal.TryParse(values[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount) && decimal.Round(amount, 2) == amount
            && amount > -10000000000000000m && amount < 10000000000000000m;
        valid &= Guid.TryParseExact(values[3], "D", out var applicant) && applicant != Guid.Empty;
        valid &= Guid.TryParseExact(values[4], "D", out var organization) && organization != Guid.Empty;
        return new(line, new(values[0], values[1], EnterpriseRequestStatusKeys.Draft, amount, applicant), organization, valid ? null : RowInvalid);
    }

    private static ZipArchiveEntry Required(ZipArchive archive, string path) => archive.GetEntry(path)
        ?? throw new InvalidDataException("Missing required workbook part.");

    private static XDocument Load(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024,
        });
        return XDocument.Load(reader);
    }

    internal sealed record Row(int LineNumber, CreateEnterpriseRequestRequest Request, Guid OrganizationUnitId, string? ErrorCode);
}
