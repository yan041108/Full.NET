using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;

namespace Full.NET.UnitTests.ImportExport;

[TestClass]
public sealed class ImportExportErrorReceiptRendererTests
{
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
}
