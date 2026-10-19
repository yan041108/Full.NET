using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.DataApproval;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：DataApprovalRecoveryRestartApiMySql。</summary>
[TestClass]
public sealed class DataApprovalRecoveryRestartApiMySqlTests
{
    [TestMethod]
    public async Task MySql_pending_request_is_linked_after_api_and_recovery_worker_restart()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await DataApprovalRecoveryRestartAssertions.VerifyAsync(
            DatabaseProvider.MySql,
            connectionString);
    }
}
