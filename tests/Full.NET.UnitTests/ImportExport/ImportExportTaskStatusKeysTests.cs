using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>单元测试（导入导出）：Import Export Task Status Keys。</summary>
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
                ImportExportTaskStatusKeys.Queued,
                ImportExportTaskStatusKeys.Executing,
                ImportExportTaskStatusKeys.ExecutionSucceeded,
                ImportExportTaskStatusKeys.ExecutionPartial,
                ImportExportTaskStatusKeys.ExecutionFailed,
            },
            ImportExportTaskStatusKeys.All.ToArray());
    }
}
