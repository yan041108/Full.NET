using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.ImportExport;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class ImportExportWorkerApiSqlServerTests
{
    [TestMethod]
    public async Task Official_position_schema_runs_in_worker() =>
        await ImportExportWorkerAssertions.VerifyPositionWorkerAsync(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Committed_sample_row_is_replayed_after_worker_restart() =>
        await ImportExportWorkerAssertions.VerifyCheckpointRestartAsync(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

    [TestMethod]
    public async Task Revoked_queued_session_requires_fresh_authorized_retry() =>
        await ImportExportWorkerAssertions.VerifyRevokedSessionRetryAsync(DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
}
