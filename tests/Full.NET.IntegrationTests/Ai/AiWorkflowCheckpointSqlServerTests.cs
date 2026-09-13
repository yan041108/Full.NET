using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

[TestClass]
public sealed class AiWorkflowCheckpointSqlServerTests
{
    [TestMethod]
    public async Task Workflow_checkpoint_roundtrip_sqlserver_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiWorkflowCheckpointAssertions.VerifyAsync(factory).ConfigureAwait(false);
    }
}
