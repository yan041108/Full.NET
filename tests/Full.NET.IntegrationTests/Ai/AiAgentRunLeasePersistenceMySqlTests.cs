using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySql 上的 Agent 运行租约 fencing。</summary>
[TestClass]
public sealed class AiAgentRunLeasePersistenceMySqlTests
{
    [TestMethod]
    public async Task Agent_run_lease_preserves_single_owner_and_fencing_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiAgentRunLeasePersistenceAssertions.VerifyAsync(factory);
    }
}
