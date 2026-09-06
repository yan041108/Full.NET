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
}
