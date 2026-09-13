using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

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
