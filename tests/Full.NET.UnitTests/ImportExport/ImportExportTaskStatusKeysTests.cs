using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.UnitTests.ImportExport;

[TestClass]
public sealed class ImportExportTaskStatusKeysTests
{
    [TestMethod]
    public void All_contains_expected_status_keys()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                ImportExportTaskStatusKeys.Uploaded,
                ImportExportTaskStatusKeys.PreviewSucceeded,
                ImportExportTaskStatusKeys.PreviewFailed,
            },
            ImportExportTaskStatusKeys.All.ToArray());
    }
}
