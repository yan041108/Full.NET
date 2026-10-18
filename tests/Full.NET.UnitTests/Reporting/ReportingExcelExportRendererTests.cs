using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using System.IO.Compression;
using System.Xml.Linq;
using System.Collections;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExcelExportRendererTests
{
    [TestMethod]
    public void Workbook_preserves_supplementary_characters_in_headers_and_cells()
    {
        var bytes = ReportingExcelExportRenderer.Render([new("value", "标题😀𠮷")],
            [new(new Dictionary<string, string?> { ["value"] = "中文😀\0𠮷\uD800" })]);
        using var memory = new MemoryStream(bytes); using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        CollectionAssert.AreEqual(new[] { "标题😀𠮷", "中文😀𠮷" }, xml.Descendants(ns + "t").Select(t => t.Value).ToArray());
    }

    [TestMethod]
    public void Precancelled_render_does_not_enumerate_source_rows()
    {
        using var cancellation = new CancellationTokenSource();
        var rows = new CancelDuringRows(cancellation, throwWhenRead: true);
        Assert.ThrowsExactly<OperationCanceledException>(() => ReportingExcelExportRenderer.Render([new("value", "Value")], rows, new(true)));
    }

    [TestMethod]
    public void Cancellation_from_row_collection_cannot_return_workbook()
    {
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => ReportingExcelExportRenderer.Render([new("value", "Value")], new CancelDuringRows(cancellation), cancellation.Token));
    }

    [TestMethod]
    public void Zip_output_write_obeys_cancellation_before_buffer_growth()
    {
        using var cancellation = new CancellationTokenSource(); using var output = new ReportingExportOutputStream(cancellation.Token);
        output.WriteByte(1); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => output.WriteByte(2));
        Assert.AreEqual(1L, output.Length);
    }

    [TestMethod]
    public void EscapeFormulaText_prefixes_formula_like_values()
    {
        Assert.AreEqual("'=1+1", ReportingExcelExportRenderer.EscapeFormulaText("=1+1"));
        Assert.AreEqual("'+SUM(A1)", ReportingExcelExportRenderer.EscapeFormulaText("+SUM(A1)"));
        Assert.AreEqual("'-value", ReportingExcelExportRenderer.EscapeFormulaText("-value"));
        Assert.AreEqual("'@cmd", ReportingExcelExportRenderer.EscapeFormulaText("@cmd"));
        Assert.AreEqual("plain", ReportingExcelExportRenderer.EscapeFormulaText("plain"));
    }

    [TestMethod]
    public void Render_produces_non_empty_xlsx_with_headers_and_rows()
    {
        var columns = new[]
        {
            new ReportingExecutionColumnDefinition("SchemaName", "Schema"),
            new ReportingExecutionColumnDefinition("EngineVersion", "Version"),
        };
        var rows = new[]
        {
            new ReportingExecutionRow(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["SchemaName"] = "dbo",
                ["EngineVersion"] = "=evil()",
            }),
        };

        var bytes = ReportingExcelExportRenderer.Render(columns, rows);
        Assert.IsTrue(bytes.Length > 0);
        CollectionAssert.AreEqual(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            bytes.Take(4).ToArray());
    }
    /// <summary>超出行数时必须拒绝生成，不能返回被截断的成功文件。</summary>
    [TestMethod]
    public void Render_rejects_excess_rows()
    {
        var row = new ReportingExecutionRow(new Dictionary<string, string?> { ["value"] = "x" });
        Assert.ThrowsExactly<InvalidDataException>(() => ReportingExcelExportRenderer.Render(
            [new("value", "Value")], Enumerable.Repeat(row, 5001).ToArray()));
    }

    /// <summary>高度可压缩文本也必须受输入内存预算约束。</summary>
    [TestMethod]
    public void Render_rejects_large_uncompressed_input()
    {
        var row = new ReportingExecutionRow(new Dictionary<string, string?> { ["value"] = new string('x', 4096) });
        Assert.ThrowsExactly<InvalidDataException>(() => ReportingExcelExportRenderer.Render(
            [new("value", "Value")], Enumerable.Repeat(row, 5000).ToArray()));
    }

    private sealed class CancelDuringRows(CancellationTokenSource cancellation, bool throwWhenRead = false) : IReadOnlyList<ReportingExecutionRow>
    {
        public int Count => 1;
        public ReportingExecutionRow this[int index] => throw new NotSupportedException();
        public IEnumerator<ReportingExecutionRow> GetEnumerator()
        {
            if (throwWhenRead) throw new InvalidOperationException("不应读取已取消的行。");
            cancellation.Cancel();
            yield return new(new Dictionary<string, string?> { ["value"] = "value" });
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
