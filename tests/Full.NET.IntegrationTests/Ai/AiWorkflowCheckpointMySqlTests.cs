using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>集成测试：AiWorkflowCheckpointMySql。</summary>
[TestClass]
public sealed class AiWorkflowCheckpointMySqlTests
{
    [TestMethod]
    public async Task Workflow_checkpoint_roundtrip_mysql_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiWorkflowCheckpointAssertions.VerifyAsync(factory).ConfigureAwait(false);
    }
}
