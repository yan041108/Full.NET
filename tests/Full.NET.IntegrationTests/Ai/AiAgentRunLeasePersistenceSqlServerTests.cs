using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SqlServer 上的 Agent 运行租约 fencing。</summary>
[TestClass]
public sealed class AiAgentRunLeasePersistenceSqlServerTests
{
    [TestMethod]
    public async Task Agent_run_lease_preserves_single_owner_and_fencing_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiAgentRunLeasePersistenceAssertions.VerifyAsync(factory);
    }
}
