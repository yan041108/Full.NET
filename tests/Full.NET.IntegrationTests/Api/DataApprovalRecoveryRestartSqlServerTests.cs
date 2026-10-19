using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.DataApproval;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：DataApprovalRecoveryRestartApiSqlServer。</summary>
[TestClass]
public sealed class DataApprovalRecoveryRestartApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_pending_request_is_linked_after_api_and_recovery_worker_restart()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await DataApprovalRecoveryRestartAssertions.VerifyAsync(
            DatabaseProvider.SqlServer,
            connectionString);
    }
}
