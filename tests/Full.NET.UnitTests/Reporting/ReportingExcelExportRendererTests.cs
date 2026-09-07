using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;

namespace Full.NET.UnitTests.Reporting;

[TestClass]
public sealed class ReportingExcelExportRendererTests
{
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
}
