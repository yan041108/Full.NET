using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.ImportExport;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ImportExportWorkerApiMySqlTests
{
    /// <summary>真实岗位失败行的回执存储与检查点故障均由新 Worker 补齐。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_row_receipt_recovers_after_worker_restart(bool progressFailure) =>
        await ImportExportWorkerAssertions.VerifyReceiptRecoveryAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), progressFailure);

    [TestMethod]
    public async Task Official_position_schema_runs_in_worker() =>
        await ImportExportWorkerAssertions.VerifyPositionWorkerAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    [TestMethod]
    public async Task Committed_sample_row_is_replayed_after_worker_restart() =>
        await ImportExportWorkerAssertions.VerifyCheckpointRestartAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    [TestMethod]
    public async Task Revoked_queued_session_requires_fresh_authorized_retry() =>
        await ImportExportWorkerAssertions.VerifyRevokedSessionRetryAsync(DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
}
