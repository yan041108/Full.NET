using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class OrganizationPositionWorkbookCodecTests
{
    [TestMethod]
    [DataRow("duplicate-part", false)]
    [DataRow("duplicate-part", true)]
    [DataRow("wrong-root", false)]
    [DataRow("wrong-root", true)]
    [DataRow("nested-sheet-data", false)]
    [DataRow("nested-sheet-data", true)]
    [DataRow("row-zero", false)]
    [DataRow("row-zero", true)]
    [DataRow("row-malformed", false)]
    [DataRow("row-malformed", true)]
    [DataRow("cell-mismatch", false)]
    [DataRow("cell-mismatch", true)]
    [DataRow("error-type", false)]
    [DataRow("error-type", true)]
    public async Task Contradictory_workbook_structure_cannot_silently_import_rows(string kind, bool shared)
    {
        var workbook = MutateWorkbook(CreateImportWorkbook(shared, [["code", "name", "displayOrder", "unitCode", "positionLevelCode"], ["engineer", "Engineer", "10", "", ""]]), kind);
        await using var source = new MemoryStream(workbook);
        await Assert.ThrowsAsync<InvalidDataException>(() => OrganizationPositionWorkbookCodec.ParseImportAsync(source, workbook.LongLength, default));
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    [DataRow("missing-row-reference", false)]
    [DataRow("missing-row-reference", true)]
    [DataRow("sparse-row", false)]
    [DataRow("sparse-row", true)]
    public async Task Compatible_missing_and_sparse_row_metadata_keep_existing_business_rows(string kind, bool shared)
    {
        var workbook = MutateWorkbook(CreateImportWorkbook(shared, [["code", "name", "displayOrder", "unitCode", "positionLevelCode"], ["engineer", "Engineer", "10", "", ""]]), kind);
        await using var source = new MemoryStream(workbook);
        var rows = await OrganizationPositionWorkbookCodec.ParseImportAsync(source, workbook.LongLength, default);
        Assert.HasCount(1, rows);
        Assert.AreEqual("Engineer", rows[0].Name);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>仅改当前测试工作簿的 ZIP 或行元数据，保留真实 inline/shared 两种编码。</summary>
    private static byte[] MutateWorkbook(byte[] source, string kind)
    {
        using var output = new MemoryStream(); output.Write(source); output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            const string path = "xl/worksheets/sheet1.xml";
            if (kind == "duplicate-part") WriteEntry(archive, path, "<duplicate/>");
            else
            {
                var document = ReadXml(archive, path); var root = document.Root!;
                var data = root.Element(SpreadsheetNamespace + "sheetData")!;
                var row = data.Elements(SpreadsheetNamespace + "row").Last();
                var cell = row.Elements(SpreadsheetNamespace + "c").First();
                switch (kind)
                {
                    case "wrong-root": root.Name = SpreadsheetNamespace + "document"; break;
                    case "nested-sheet-data": data.ReplaceWith(new XElement(SpreadsheetNamespace + "wrapper", new XElement(data))); break;
                    case "row-zero": row.SetAttributeValue("r", "0"); break;
                    case "row-malformed": row.SetAttributeValue("r", "invalid"); break;
                    case "cell-mismatch": cell.SetAttributeValue("r", "A99"); break;
                    case "error-type": cell.SetAttributeValue("t", "e"); cell.ReplaceNodes(new XElement(SpreadsheetNamespace + "v", "#VALUE!")); break;
                    case "missing-row-reference": row.Attribute("r")!.Remove(); break;
                    case "sparse-row": row.SetAttributeValue("r", "4096"); foreach (var item in row.Elements()) item.SetAttributeValue("r", ((string)item.Attribute("r")!)[0] + "4096"); break;
                }
                archive.GetEntry(path)!.Delete(); WriteEntry(archive, path, document.ToString());
            }
        }
        return output.ToArray();
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Cancellation_after_source_eof_stops_xml_parse_and_keeps_source_owned_by_caller(bool hasData, bool shared)
    {
        var rows = new List<string[]> { ["code", "name", "displayOrder", "unitCode", "positionLevelCode"] };
        if (hasData) rows.Add(["engineer", "Engineer", "10", "", ""]);
        var workbook = CreateImportWorkbook(shared, rows);
        using var cancellation = new CancellationTokenSource();
        await using var source = new CancelOnEofStream(workbook, cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => OrganizationPositionWorkbookCodec.ParseImportAsync(source, workbook.LongLength, cancellation.Token));
        Assert.IsTrue(source.CanRead);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Duplicate_cell_cannot_silently_replace_an_existing_column(bool shared)
    {
        var workbook = CreateImportWorkbook(shared, [["code", "name", "displayOrder", "unitCode", "positionLevelCode"], ["engineer", "Engineer", "10", "", ""]], overriddenCellReference: "B2");
        await using var source = new MemoryStream(workbook);
        await Assert.ThrowsAsync<InvalidDataException>(() => OrganizationPositionWorkbookCodec.ParseImportAsync(source, workbook.LongLength, default));
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>取消在源流 EOF 返回前发生，避免测试依赖定时或调度竞态。</summary>
    private sealed class CancelOnEofStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read == 0) cancellation.Cancel();
            return read;
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Export_preserves_supplementary_position_and_related_names(bool malformed)
    {
        // 同时覆盖职位、机构和职级显示名称；孤立代理项不写入特性参数。
        var name = malformed ? " 前\0😀\uD800𠮷\uDC00后 " : " 中文 😀 𠮷 \t";
        var expected = malformed ? " 前😀𠮷后 " : name;
        var position = new OrganizationPositionResponse(Guid.CreateVersion7(), "unicode-position", name,
            null, "unit", name, null, "level", name, 10, true,
            DateTimeOffset.Parse("2026-10-15T00:00:00Z"), null, 1);
        using var archive = Open(OrganizationPositionWorkbookCodec.Export([position]));
        var sheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        foreach (var reference in new[] { "B2", "E2", "G2" })
        {
            var value = sheet.Descendants(SpreadsheetNamespace + "c").Single(cell => (string?)cell.Attribute("r") == reference);
            Assert.AreEqual(expected, value.Descendants(SpreadsheetNamespace + "t").Single().Value);
        }
        Assert.IsFalse(sheet.Descendants(SpreadsheetNamespace + "f").Any());
    }

    [TestMethod]
    public void CreateImportTemplate_emits_fixed_positions_sheet_and_headers()
    {
        var workbook = OrganizationPositionWorkbookCodec.CreateImportTemplate();

        using var archive = Open(workbook);
        var sheet = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var texts = sheet.Descendants(SpreadsheetNamespace + "t")
            .Select(node => node.Value)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "code", "name", "displayOrder", "unitCode", "positionLevelCode" },
            texts.Take(5).ToArray());
    }

    [TestMethod]
    public void Export_escapes_formula_prefixes_as_text()
    {
        var positions = new[]
        {
            new OrganizationPositionResponse(
                Guid.Parse("0198fa17-3233-7f21-80a0-123456789abc"),
                "engineer",
                "=WEBSERVICE(\"https://example.invalid\")",
                null,
                null,
                null,
                null,
                null,
                null,
                10,
                true,
                DateTimeOffset.Parse("2026-08-30T00:00:00Z"),
                null,
                1),
        };

        var workbook = OrganizationPositionWorkbookCodec.Export(positions);

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
                ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
                ["engineer", "Engineer", "10", "", ""],
            ]);

        await using var stream = new MemoryStream(workbook, writable: false);
        var rows = await OrganizationPositionWorkbookCodec.ParseImportAsync(
            stream,
            workbook.LongLength,
            CancellationToken.None);

        Assert.HasCount(1, rows);
        Assert.AreEqual("engineer", rows[0].Code);
        Assert.AreEqual("Engineer", rows[0].Name);
        Assert.AreEqual(10, rows[0].DisplayOrder);
        Assert.IsNull(rows[0].UnitCode);
        Assert.IsNull(rows[0].PositionLevelCode);
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_formula_cells()
    {
        var workbook = CreateImportWorkbook(
            useSharedStrings: false,
            rows:
            [
                ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
                ["engineer", "Engineer", "10", "", "standard"],
            ],
            formulaCellReference: "B2");

        await using var stream = new MemoryStream(workbook, writable: false);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
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
                ["code", "name", "displayOrder", "unexpected"],
                ["engineer", "Engineer", "10", "", "standard"],
            ]);
        await using var invalidHeaderStream = new MemoryStream(invalidHeaders, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
                invalidHeaderStream,
                invalidHeaders.LongLength,
                CancellationToken.None));

        var rows = new List<string[]>
        {
            new[] { "code", "name", "displayOrder", "unitCode", "positionLevelCode" },
        };
        rows.AddRange(Enumerable.Range(1, OrganizationPositionWorkbookCodec.MaximumDataRows + 1)
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
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
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
                ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
            ],
            includeExternalRelationship: true);
        await using var externalStream = new MemoryStream(externalRelationshipWorkbook, writable: false);
        var externalException = await Assert.ThrowsAsync<InvalidDataException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
                externalStream,
                externalRelationshipWorkbook.LongLength,
                CancellationToken.None));
        StringAssert.Contains(externalException.Message, "外部关系");

        var hostileReferenceWorkbook = CreateImportWorkbook(
            false,
            [
                ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
                ["engineer", "Engineer", "10", "", "standard"],
            ],
            overriddenCellReference: new string('A', 512));
        await using var hostileStream = new MemoryStream(hostileReferenceWorkbook, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
                hostileStream,
                hostileReferenceWorkbook.LongLength,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ParseImportAsync_rejects_declared_size_over_limit_and_honors_cancellation()
    {
        await using var oversized = new MemoryStream([1], writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
                oversized,
                OrganizationPositionWorkbookCodec.MaximumUploadBytes + 1,
                CancellationToken.None));

        var workbook = CreateImportWorkbook(
            false,
            [
                ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
            ]);
        await using var canceledStream = new MemoryStream(workbook, writable: false);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => OrganizationPositionWorkbookCodec.ParseImportAsync(
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
