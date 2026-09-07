using Full.NET.Modules.ImportExport.Features.ManageImportTasks;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>恢复旧任务时先识别快照形状，不能把旧行数组当成状态对象反序列化。</summary>
[TestClass]
public sealed class ImportExportExecutionStateTests
{
    /// <summary>旧数组快照在升级后仍可恢复行结果。</summary>
    [TestMethod]
    public void Legacy_array_remains_readable()
    {
        var state = ImportExportTaskMapper.DeserializeExecutionState("[]");
        Assert.IsEmpty(state.Rows);
        Assert.IsNull(state.CapabilityFlags);
    }
}
