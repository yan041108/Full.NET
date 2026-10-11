using Full.NET.Modules.Reporting.Features.ManageExportTasks;

namespace Full.NET.UnitTests.Reporting;

/// <summary>损坏 JSON 不应逃逸 Worker 批循环，也不能被当成空权限授权。</summary>
[TestClass]
public sealed class ReportingExportAuthorizationSnapshotTests
{
    [TestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[null]")]
    [DataRow("[1]")]
    [DataRow("[\" \"]")]
    [DataRow("{}")]
    [DataRow("{\"permissionCodes\":null}")]
    public void Invalid_snapshot_is_denied(string json) =>
        Assert.IsNull(ReportingExportTaskMapper.DeserializeAuthorization(json));

    [TestMethod]
    public void Legacy_array_retains_columns_but_cannot_delegate_background_execution()
    {
        var snapshot = ReportingExportTaskMapper.DeserializeAuthorization("[\"protected.column\"]");
        Assert.IsNotNull(snapshot); Assert.IsNull(snapshot.Binding);
        CollectionAssert.AreEqual(new[] { "protected.column" }, snapshot.PermissionCodes);
    }
}
