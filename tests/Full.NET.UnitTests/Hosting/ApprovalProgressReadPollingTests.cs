using System.Net;
using Full.NET.Testing;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class ApprovalProgressReadPollingTests
{
    private const string Path = "/api/v1/enterprise_request/enterprise-requests/018f6817-a388-7000-8000-000000000001/approval-progress";
    private const string Conflict = "{\"code\":\"enterprise_request.enterprise_requests.version_conflict\"}";

    [TestMethod]
    public void Exact_progress_snapshot_conflict_can_be_polled_again() =>
        Assert.IsTrue(ApprovalProgressReadPolling.IsSnapshotConflict(Path, HttpStatusCode.Conflict, Conflict));

    [TestMethod]
    [DataRow(200)]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(404)]
    [DataRow(500)]
    public void Other_statuses_are_not_retryable(int status) =>
        Assert.IsFalse(ApprovalProgressReadPolling.IsSnapshotConflict(Path, (HttpStatusCode)status, Conflict));

    [TestMethod]
    [DataRow("{\"code\":\"other.version_conflict\"}")]
    [DataRow("{}")]
    [DataRow("{\"code\":409}")]
    [DataRow("{\"code\":\"enterprise_request.enterprise_requests.VERSION_CONFLICT\"}")]
    [DataRow("invalid json")]
    public void Other_or_malformed_errors_are_not_retryable(string body) =>
        Assert.IsFalse(ApprovalProgressReadPolling.IsSnapshotConflict(Path, HttpStatusCode.Conflict, body));

    [TestMethod]
    [DataRow("/api/v1/enterprise_request/enterprise-requests/018f6817-a388-7000-8000-000000000001")]
    [DataRow("/api/v1/enterprise_request/enterprise-requests/invalid/approval-progress")]
    [DataRow("/api/v1/reporting/018f6817-a388-7000-8000-000000000001/approval-progress")]
    public void Other_paths_are_not_retryable(string path) =>
        Assert.IsFalse(ApprovalProgressReadPolling.IsSnapshotConflict(path, HttpStatusCode.Conflict, Conflict));
}
