using System.IO.Compression;
using System.Text;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.RenderWorkbooks;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>真实工作簿必须在无数据库副作用的预览中显示有效行和具体原始错误行。</summary>
[TestClass]
public sealed class EnterpriseRequestImportPreviewTests
{
    private static readonly StaticImportPreviewContext Context = new(Guid.CreateVersion7(), new Dictionary<string, bool>());
    private static readonly string[] ValidRow = ["REQ-01", "采购申请", "12.50", Guid.CreateVersion7().ToString("D"), Guid.CreateVersion7().ToString("D")];

    private static EnterpriseRequestStaticImportSchemaHandler Handler() => new(null!, new ReportingWorkbookRenderer());

    [TestMethod]
    public async Task Template_is_real_xlsx_and_empty_template_has_no_data_rows()
    {
        var handler = Handler();
        var bytes = handler.CreateTemplate("requests");
        Assert.AreEqual((byte)'P', bytes[0]);
        Assert.AreEqual((byte)'K', bytes[1]);
        using var stream = new MemoryStream(bytes);
        var result = await handler.PreviewAsync(stream, bytes.Length, Context);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value!.TotalRows);
        Assert.ThrowsExactly<InvalidOperationException>(() => handler.CreateTemplate("unknown"));
    }

    [TestMethod]
    public async Task Preview_counts_real_rows_and_reports_original_line_numbers_without_writes()
    {
        var invalid = (string[])ValidRow.Clone();
        invalid[2] = "12.345";
        var bytes = Workbook([ValidRow, invalid]);
        using var stream = new MemoryStream(bytes);
        var result = await Handler().PreviewAsync(stream, bytes.Length, Context);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value!.TotalRows);
        Assert.AreEqual(1, result.Value.ValidRowCount);
        Assert.AreEqual(1, result.Value.InvalidRowCount);
        Assert.AreEqual(2, result.Value.Rows[0].LineNumber);
        Assert.AreEqual(3, result.Value.Rows[1].LineNumber);
        Assert.AreEqual(EnterpriseRequestWorkbookReader.RowInvalid, result.Value.Rows[1].ErrorCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.Rows[1].Message));
    }

    [TestMethod]
    public async Task Csv_disguised_as_xlsx_is_rejected()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join(',', EnterpriseRequestWorkbookReader.Headers));
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler().PreviewAsync(stream, bytes.Length, Context));
    }

    [TestMethod]
    [DataRow("formula")]
    [DataRow("external")]
    [DataRow("headers")]
    public async Task Hostile_or_wrong_workbook_is_rejected_before_business_execution(string mode)
    {
        var bytes = Workbook([ValidRow]);
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var path = mode == "external" ? "xl/_rels/workbook.xml.rels" : "xl/worksheets/sheet1.xml";
            var entry = zip.GetEntry(path)!;
            string xml;
            using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();
            xml = mode switch
            {
                "formula" => xml.Replace("</c>", "<f>1+1</f></c>", StringComparison.Ordinal),
                "external" => xml.Replace("Target=", "TargetMode=\"External\" Target=", StringComparison.Ordinal),
                _ => xml.Replace("requestNumber", "unknownColumn", StringComparison.Ordinal),
            };
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(xml);
        }
        buffer.Position = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler().PreviewAsync(buffer, buffer.Length, Context));
    }

    [TestMethod]
    public async Task Declared_file_length_cannot_hide_extra_bytes()
    {
        var bytes = Workbook([ValidRow]);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler().PreviewAsync(stream, bytes.Length - 1, Context));
    }

    [TestMethod]
    public async Task More_than_one_thousand_rows_is_rejected()
    {
        var bytes = Workbook(Enumerable.Repeat(ValidRow, 1001).ToArray());
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Handler().PreviewAsync(stream, bytes.Length, Context));
    }

    [TestMethod]
    public async Task Cancellation_is_not_reported_as_a_successful_empty_preview()
    {
        var bytes = Workbook([ValidRow]);
        using var stream = new MemoryStream(bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Handler().PreviewAsync(stream, bytes.Length, Context, cancellation.Token));
    }

    [TestMethod]
    public async Task Mapped_headers_read_reordered_values_and_skip_ignored_only_rows()
    {
        var targets = EnterpriseRequestWorkbookReader.Headers;
        var mapping = new ImportColumnMappingRequest(targets, targets.Select((header, index) => new ImportColumnMapping(header, "来源" + index)).ToArray());
        var headers = new[] { "ignored", "来源3", "来源0", "来源4", "来源1", "来源2" };
        var values = new[] { new[] { "extra", "", "", "", "", "" }, new[] { "extra", ValidRow[3], ValidRow[0], ValidRow[4], ValidRow[1], ValidRow[2] } };
        var columns = headers.Select(header => new ReportingExecutionColumnDefinition(header, header)).ToArray();
        var rows = values.Select(row => new ReportingExecutionRow(headers.Select((header, index) => (header, value: (string?)row[index])).ToDictionary(pair => pair.header, pair => pair.value))).ToArray();
        var bytes = new ReportingWorkbookRenderer().Render(columns, rows).Value!;
        using var stream = new MemoryStream(bytes);
        var parsed = await EnterpriseRequestWorkbookReader.ReadAsync(stream, bytes.Length, default, mapping);
        Assert.HasCount(1, parsed); Assert.AreEqual(3, parsed[0].LineNumber);
        Assert.AreEqual(ValidRow[0], parsed[0].Request.RequestNumber); Assert.AreEqual(12.50m, parsed[0].Request.TotalAmount);
        using var canonical = new MemoryStream(bytes);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => EnterpriseRequestWorkbookReader.ReadAsync(canonical, bytes.Length, default));
    }

    private static byte[] Workbook(IReadOnlyList<string[]> rows)
    {
        var columns = EnterpriseRequestWorkbookReader.Headers.Select(header => new ReportingExecutionColumnDefinition(header, header)).ToArray();
        var values = rows.Select(row => new ReportingExecutionRow(EnterpriseRequestWorkbookReader.Headers
            .Select((key, index) => (key, value: (string?)row[index])).ToDictionary(item => item.key, item => item.value))).ToArray();
        return new ReportingWorkbookRenderer().Render(columns, values).Value!;
    }
}
