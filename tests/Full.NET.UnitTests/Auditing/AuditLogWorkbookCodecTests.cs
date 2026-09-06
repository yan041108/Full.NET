using Full.NET.Modules.Auditing.Features.ExportHostAuditLogs;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditLogWorkbookCodecTests
{
    [TestMethod]
    public void EscapeFormulaText_prefixes_formula_like_values()
    {
        Assert.AreEqual("'=1+1", AuditLogWorkbookCodec.EscapeFormulaText("=1+1"));
    }

    [TestMethod]
    public void Export_returns_non_empty_workbook_bytes()
    {
        var bytes = AuditLogWorkbookCodec.Export(
            "AccessLogs",
            ["value"],
            [["sample"]]);

        Assert.IsTrue(bytes.Length > 0);
    }
}
