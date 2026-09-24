using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.DataApproval;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DataApprovalRecoveryRestartApiMySqlTests
{
    [TestMethod]
    public async Task MySql_pending_request_is_linked_by_recovery_worker_after_host_restart()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await DataApprovalRecoveryRestartAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            connectionString);
    }
}
