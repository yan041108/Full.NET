using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Full.NET.Modules.EnterpriseRequest.Features.ImportExport;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestImportTests
{
    private static readonly string[] Headers = ["requestNumber", "title", "totalAmount", "applicantUserId", "organizationUnitId"];
    private static readonly StaticImportPreviewContext Context = new(Guid.NewGuid(), new Dictionary<string, bool>());
    private readonly EnterpriseRequestStaticImportSchemaHandler handler = new(null!);

    [TestMethod]
    public void Template_is_a_real_excel_archive_and_rejects_unknown_worksheet()
    {
        var bytes = handler.CreateTemplate("requests");
        Assert.AreEqual((byte)'P', bytes[0]); Assert.AreEqual((byte)'K', bytes[1]);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        Assert.IsNotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.IsNotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.Throws<InvalidOperationException>(() => handler.CreateTemplate("unknown"));
    }

    [TestMethod]
    public async Task Preview_counts_real_rows_and_reports_invalid_values_without_writing()
    {
        var bytes = Workbook([
            ["REQ-1", "标题,含逗号", "12.5", Guid.NewGuid().ToString(), Guid.NewGuid().ToString()],
            ["REQ-2", "bad amount", "12,5", Guid.NewGuid().ToString(), Guid.NewGuid().ToString()],
            ["REQ-3", "bad identity", "1", "not-a-guid", Guid.NewGuid().ToString()]
        ]);
        using var stream = new MemoryStream(bytes);
        var result = await handler.PreviewAsync(stream, bytes.Length, Context);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(3, result.Value!.TotalRows); Assert.AreEqual(1, result.Value.ValidRowCount); Assert.AreEqual(2, result.Value.InvalidRowCount);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, result.Value.Rows.Select(row => row.LineNumber).ToArray());
        Assert.IsTrue(result.Value.Rows[0].IsValid); Assert.IsFalse(result.Value.Rows[1].IsValid);
        Assert.IsNotNull(result.Value.Rows[1].ErrorCode); Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task Csv_disguised_as_xlsx_is_rejected_instead_of_zero_row_success()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join(',', Headers) + "\nREQ-1,title,1,user,unit");
        using var stream = new MemoryStream(bytes);
        var result = await handler.PreviewAsync(stream, bytes.Length, Context);
        Assert.IsFalse(result.IsSuccess); Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task Preview_requires_exact_length_and_propagates_cancellation()
    {
        var bytes = Workbook([]); using var stream = new MemoryStream(bytes);
        Assert.IsFalse((await handler.PreviewAsync(stream, bytes.Length + 1, Context)).IsSuccess);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); stream.Position = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.PreviewAsync(stream, bytes.Length, Context, canceled.Token));
    }

    [TestMethod]
    public async Task Preview_rejects_formula_and_header_drift()
    {
        var bytes = Workbook([], sheet => sheet.Descendants().First(node => node.Name.LocalName == "c").Add(new XElement(sheet.Name.Namespace + "f", "1+1")));
        using var formula = new MemoryStream(bytes); Assert.IsFalse((await handler.PreviewAsync(formula, bytes.Length, Context)).IsSuccess);
        bytes = Workbook([], sheet => sheet.Descendants().First(node => node.Name.LocalName == "t").Value = "wrong");
        using var drift = new MemoryStream(bytes); Assert.IsFalse((await handler.PreviewAsync(drift, bytes.Length, Context)).IsSuccess);
    }

    [TestMethod]
    public async Task Preview_preserves_sparse_source_lines_and_skips_blank_rows()
    {
        var bytes = Workbook([["", "", "", "", ""], ["REQ", "title", "1", Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]],
            sheet => { var row = sheet.Descendants().Last(node => node.Name.LocalName == "row"); row.SetAttributeValue("r", 10);
                foreach (var cell in row.Elements()) cell.SetAttributeValue("r", ((string)cell.Attribute("r")!)[0] + "10"); });
        using var stream = new MemoryStream(bytes); var result = await handler.PreviewAsync(stream, bytes.Length, Context);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.TotalRows); Assert.AreEqual(10, result.Value.Rows[0].LineNumber);
    }

    [TestMethod]
    public async Task Preview_rejects_duplicate_cells_external_relationships_and_row_limit()
    {
        var bytes = Workbook([], sheet => { var row = sheet.Descendants().First(node => node.Name.LocalName == "row"); row.Add(new XElement(row.Elements().First())); });
        using var duplicate = new MemoryStream(bytes); Assert.IsFalse((await handler.PreviewAsync(duplicate, bytes.Length, Context)).IsSuccess);
        using var archiveBytes = new MemoryStream(); archiveBytes.Write(Workbook([]));
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Update, true))
        using (var writer = new StreamWriter(archive.CreateEntry("xl/_rels/workbook.xml.rels").Open()))
            writer.Write("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship TargetMode='External' Target='https://example.test'/></Relationships>");
        bytes = archiveBytes.ToArray(); using var external = new MemoryStream(bytes);
        Assert.IsFalse((await handler.PreviewAsync(external, bytes.Length, Context)).IsSuccess);
        bytes = Workbook(Enumerable.Range(0, 1001).Select(_ => new[] { "a", "b", "1", Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }).ToArray());
        using var oversized = new MemoryStream(bytes); Assert.IsFalse((await handler.PreviewAsync(oversized, bytes.Length, Context)).IsSuccess);
    }

    [TestMethod]
    public async Task Execution_requires_persisted_task_and_valid_batch_boundaries()
    {
        foreach (var item in new[] { (Context, 0, 1), (Context with { TaskId = Guid.NewGuid() }, -1, 1), (Context with { TaskId = Guid.NewGuid() }, 0, 0) })
        {
            using var content = new MemoryStream(Workbook([]));
            Assert.IsFalse((await handler.ExecuteBatchAsync(content, content.Length, item.Item2, item.Item3, item.Item1)).IsSuccess);
        }
    }

    [TestMethod]
    public async Task Preview_does_not_round_tiny_nonzero_amounts_to_zero()
    {
        var bytes = Workbook([["REQ", "title", "0.00000000000000000000000000000000000000001", Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]]);
        using var source = new MemoryStream(bytes); var result = await handler.PreviewAsync(source, bytes.Length, Context);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(0, result.Value!.ValidRowCount); Assert.AreEqual(1, result.Value.InvalidRowCount);
    }

    internal static byte[] Workbook(string[][] rows, Action<XElement>? edit = null)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var all = new[] { Headers }.Concat(rows);
        var sheet = new XElement(ns + "worksheet", new XElement(ns + "sheetData", all.Select((row, index) =>
            new XElement(ns + "row", new XAttribute("r", index + 1), row.Select((value, column) =>
                new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + column)}{index + 1}"), new XAttribute("t", "inlineStr"),
                    new XElement(ns + "is", new XElement(ns + "t", value))))))));
        edit?.Invoke(sheet);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(), new UTF8Encoding(false));
            writer.Write(sheet.ToString());
        }
        return output.ToArray();
    }
}
