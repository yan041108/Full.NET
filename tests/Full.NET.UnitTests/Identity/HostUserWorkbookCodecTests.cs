using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserWorkbookCodecTests
{
    [TestMethod]
    public void CreateImportTemplate_emits_fixed_users_sheet_and_headers()
    {
        var workbook = HostUserWorkbookCodec.CreateImportTemplate();

        using var archive = Open(workbook);
        var sheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var texts = sheet.Descendants(SpreadsheetNamespace + "t")
            .Select(node => node.Value)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "username", "displayName", "password", "accountType" },
            texts.Take(4).ToArray());
    }

    [TestMethod]
    public void Export_escapes_formula_prefixes_as_text()
    {
        var users = new[]
        {
            new HostUserResponse(
                Guid.Parse("0198fa17-3233-7f21-80a0-123456789abc"),
                "safe-user",
                "=WEBSERVICE(\"https://example.invalid\")",
                "standard",
                true,
                DateTimeOffset.Parse("2026-08-30T00:00:00Z"),
                null,
                1),
        };

        var workbook = HostUserWorkbookCodec.Export(users);

        using var archive = Open(workbook);
        var sheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        Assert.IsFalse(sheet.Descendants(SpreadsheetNamespace + "f").Any());
        StringAssert.Contains(sheet.ToString(SaveOptions.DisableFormatting), "'=WEBSERVICE");
    }

    [TestMethod]
    public async Task ParseImportAsync_reads_inline_and_shared_strings()
    {
        var workbook = CreateImportWorkbook(
            useSharedStrings: true,
            rows:
            [
                ["username", "displayName", "password", "accountType"],
                ["alice", "Alice", "Correct-Horse-2026!", "standard"],
            ]);

        await using var stream = new MemoryStream(workbook, writable: false);
        var rows = await HostUserWorkbookCodec.ParseImportAsync(
            stream,
            workbook.LongLength,
            CancellationToken.None);

        Assert.HasCount(1, rows);
        Assert.AreEqual("alice", rows[0].Username);
        Assert.AreEqual("Alice", rows[0].DisplayName);
        Assert.AreEqual("Correct-Horse-2026!", rows[0].Password);
        Assert.AreEqual("standard", rows[0].AccountType);
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_formula_cells()
    {
        var workbook = CreateImportWorkbook(
            useSharedStrings: false,
            rows:
            [
                ["username", "displayName", "password", "accountType"],
                ["alice", "Alice", "Correct-Horse-2026!", "standard"],
            ],
            formulaCellReference: "B2");

        await using var stream = new MemoryStream(workbook, writable: false);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                stream,
                workbook.LongLength,
                CancellationToken.None));

        StringAssert.Contains(exception.Message, "公式");
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_unknown_headers_and_excess_rows()
    {
        var invalidHeaders = CreateImportWorkbook(
            useSharedStrings: false,
            rows:
            [
                ["username", "displayName", "password", "unexpected"],
                ["alice", "Alice", "Correct-Horse-2026!", "standard"],
            ]);
        await using var invalidHeaderStream = new MemoryStream(invalidHeaders, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                invalidHeaderStream,
                invalidHeaders.LongLength,
                CancellationToken.None));

        var rows = new List<string[]>
        {
            new[] { "username", "displayName", "password", "accountType" },
        };
        rows.AddRange(Enumerable.Range(1, HostUserWorkbookCodec.MaximumDataRows + 1)
            .Select(index => new[]
            {
                $"user-{index}",
                $"User {index}",
                "Correct-Horse-2026!",
                "standard",
            }));
        var excessiveRows = CreateImportWorkbook(false, rows);
        await using var excessiveRowsStream = new MemoryStream(excessiveRows, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                excessiveRowsStream,
                excessiveRows.LongLength,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_external_relationships_and_hostile_cell_references()
    {
        var externalRelationshipWorkbook = CreateImportWorkbook(
            false,
            [
                ["username", "displayName", "password", "accountType"],
            ],
            includeExternalRelationship: true);
        await using var externalStream = new MemoryStream(externalRelationshipWorkbook, writable: false);
        var externalException = await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                externalStream,
                externalRelationshipWorkbook.LongLength,
                CancellationToken.None));
        StringAssert.Contains(externalException.Message, "外部关系");

        var hostileReferenceWorkbook = CreateImportWorkbook(
            false,
            [
                ["username", "displayName", "password", "accountType"],
                ["alice", "Alice", "Correct-Horse-2026!", "standard"],
            ],
            overriddenCellReference: new string('A', 512));
        await using var hostileStream = new MemoryStream(hostileReferenceWorkbook, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                hostileStream,
                hostileReferenceWorkbook.LongLength,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_declared_size_over_limit_and_honors_cancellation()
    {
        await using var oversized = new MemoryStream([1], writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                oversized,
                HostUserWorkbookCodec.MaximumUploadBytes + 1,
                CancellationToken.None));

        var workbook = CreateImportWorkbook(
            false,
            [
                ["username", "displayName", "password", "accountType"],
            ]);
        await using var canceledStream = new MemoryStream(workbook, writable: false);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HostUserWorkbookCodec.ParseImportAsync(
                canceledStream,
                workbook.LongLength,
                source.Token));
    }

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static ZipArchive Open(byte[] bytes) =>
        new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new AssertFailedException($"工作簿缺少 {path}。");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static byte[] CreateImportWorkbook(
        bool useSharedStrings,
        IReadOnlyList<string[]> rows,
        string? formulaCellReference = null,
        bool includeExternalRelationship = false,
        string? overriddenCellReference = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                  <Default Extension="xml" ContentType="application/xml" />
                </Types>
                """);

            if (includeExternalRelationship)
            {
                WriteEntry(
                    archive,
                    "_rels/.rels",
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdExternal" Type="test" Target="https://example.invalid/data" TargetMode="External" />
                    </Relationships>
                    """);
            }

            var shared = rows.SelectMany(row => row).ToArray();
            if (useSharedStrings)
            {
                var sharedDocument = new XDocument(
                    new XElement(
                        SpreadsheetNamespace + "sst",
                        new XAttribute("count", shared.Length),
                        new XAttribute("uniqueCount", shared.Length),
                        shared.Select(value => new XElement(
                            SpreadsheetNamespace + "si",
                            new XElement(SpreadsheetNamespace + "t", value)))));
                WriteEntry(archive, "xl/sharedStrings.xml", sharedDocument.ToString());
            }

            var sharedIndex = 0;
            var sheetRows = rows.Select((row, rowIndex) =>
                new XElement(
                    SpreadsheetNamespace + "row",
                    new XAttribute("r", rowIndex + 1),
                    row.Select((value, columnIndex) =>
                    {
                        var reference = overriddenCellReference is not null
                            && rowIndex == 1
                            && columnIndex == 0
                                ? overriddenCellReference
                                : $"{(char)('A' + columnIndex)}{rowIndex + 1}";
                        if (string.Equals(reference, formulaCellReference, StringComparison.Ordinal))
                        {
                            return new XElement(
                                SpreadsheetNamespace + "c",
                                new XAttribute("r", reference),
                                new XElement(SpreadsheetNamespace + "f", value),
                                new XElement(SpreadsheetNamespace + "v", "0"));
                        }

                        if (useSharedStrings)
                        {
                            return new XElement(
                                SpreadsheetNamespace + "c",
                                new XAttribute("r", reference),
                                new XAttribute("t", "s"),
                                new XElement(
                                    SpreadsheetNamespace + "v",
                                    (sharedIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                        }

                        return new XElement(
                            SpreadsheetNamespace + "c",
                            new XAttribute("r", reference),
                            new XAttribute("t", "inlineStr"),
                            new XElement(
                                SpreadsheetNamespace + "is",
                                new XElement(SpreadsheetNamespace + "t", value)));
                    })));
            var sheetDocument = new XDocument(
                new XElement(
                    SpreadsheetNamespace + "worksheet",
                    new XElement(SpreadsheetNamespace + "sheetData", sheetRows)));
            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheetDocument.ToString());
        }

        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
