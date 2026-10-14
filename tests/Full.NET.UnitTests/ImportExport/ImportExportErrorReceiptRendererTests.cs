using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using System.IO.Compression;
using System.Xml.Linq;
using System.Collections;

namespace Full.NET.UnitTests.ImportExport;

[TestClass]
public sealed class ImportExportErrorReceiptRendererTests
{
    [TestMethod]
    public void Receipt_preserves_legal_unicode_whitespace_formulas_and_cell_positions()
    {
        var bytes = ImportExportErrorReceiptRenderer.Render([new(2, false, null, "=错误😀", " 𠮷\0😀\uDC00 ")]);
        using var memory = new MemoryStream(bytes); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        CollectionAssert.AreEqual(new[] { "lineNumber", "errorCode", "message", "2", "'=错误😀", " 𠮷😀 " }, xml.Descendants(ns + "t").Select(t => t.Value).ToArray());
        Assert.AreEqual("C2", xml.Descendants(ns + "c").Last().Attribute("r")!.Value);
        Assert.IsTrue(xml.Descendants(ns + "c").All(c => c.Attribute("t")!.Value == "inlineStr"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Row_limit_is_checked_before_render_or_identity(bool identity)
    {
        var rows = Enumerable.Repeat(new StaticImportRowExecutionResult(1, false, null, "code", "message"), 1001).ToArray();
        Assert.ThrowsExactly<ImportExportErrorReceiptLimitException>(() => { if (identity) ImportExportErrorReceiptIdentity.BuildStorageFileName("source.xlsx", rows); else ImportExportErrorReceiptRenderer.Render(rows); });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Text_limit_includes_highly_compressible_messages_before_identity_or_render(bool identity)
    {
        var rows = new[] { new StaticImportRowExecutionResult(1, false, null, "", new string('x', (int)(ImportExportErrorReceiptBudget.MaxTextBytes / 2) + 1)) };
        Assert.ThrowsExactly<ImportExportErrorReceiptLimitException>(() => { if (identity) ImportExportErrorReceiptIdentity.BuildStorageFileName("source.xlsx", rows); else ImportExportErrorReceiptRenderer.Render(rows); });
    }

    [TestMethod]
    public void Boundary_row_count_can_still_produce_receipt()
    {
        var rows = Enumerable.Range(1, 1000).Select(i => new StaticImportRowExecutionResult(i, false, null, "code", "message")).ToArray();
        Assert.IsTrue(ImportExportErrorReceiptRenderer.Render(rows).Length > 0);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Precancelled_identity_or_render_does_not_read_rows(bool identity)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var rows = new CancelDuringRows(cancellation, throwWhenRead: true);
        Assert.ThrowsExactly<OperationCanceledException>(() => { if (identity) ImportExportErrorReceiptIdentity.BuildStorageFileName("source.xlsx", rows, cancellation.Token); else ImportExportErrorReceiptRenderer.Render(rows, cancellation.Token); });
    }

    [TestMethod]
    public void Cancellation_from_failed_row_collection_cannot_return_receipt()
    {
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => ImportExportErrorReceiptRenderer.Render(new CancelDuringRows(cancellation), cancellation.Token));
    }

    [TestMethod]
    public void Zip_output_budget_rejects_growth_before_allocation()
    {
        using var output = new ImportExportErrorReceiptOutputStream();
        output.Position = ImportExportErrorReceiptBudget.MaxOutputBytes;
        Assert.ThrowsExactly<ImportExportErrorReceiptLimitException>(() => output.WriteByte(1));
        Assert.AreEqual(0L, output.Length);
    }

    [TestMethod]
    public void Zip_output_cancellation_prevents_growth()
    {
        using var cancellation = new CancellationTokenSource(); using var output = new ImportExportErrorReceiptOutputStream(cancellation.Token);
        output.WriteByte(1); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => output.SetLength(100));
        Assert.AreEqual(1L, output.Length);
    }

    [TestMethod]
    public void Budget_check_keeps_existing_receipt_result_identity()
    {
        var rows = new[] { new StaticImportRowExecutionResult(2, false, null, "code", "😀𠮷") };
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ImportExportTaskMapper.SerializeExecutionRows(rows))));
        StringAssert.Contains(ImportExportErrorReceiptIdentity.BuildStorageFileName("source.xlsx", rows), "-errors-" + hash);
    }

    [TestMethod]
    public void Render_produces_non_empty_workbook_for_failed_rows()
    {
        var bytes = ImportExportErrorReceiptRenderer.Render(
        [
            new StaticImportRowExecutionResult(2, false, null, "validation.failed", "Invalid code"),
            new StaticImportRowExecutionResult(5, false, null, "organization.position.duplicate_code", "Duplicate"),
        ]);

        Assert.IsTrue(bytes.Length > 0);
        CollectionAssert.AreEqual(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            bytes.Take(4).ToArray());
    }

    private sealed class CancelDuringRows(CancellationTokenSource cancellation, bool throwWhenRead = false) : IReadOnlyList<StaticImportRowExecutionResult>
    {
        public int Count => throwWhenRead ? throw new InvalidOperationException("不应读取已取消的行。") : 1;
        public StaticImportRowExecutionResult this[int index] => throw new NotSupportedException();
        public IEnumerator<StaticImportRowExecutionResult> GetEnumerator()
        {
            cancellation.Cancel(); yield return new(1, false, null, "code", "message");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
