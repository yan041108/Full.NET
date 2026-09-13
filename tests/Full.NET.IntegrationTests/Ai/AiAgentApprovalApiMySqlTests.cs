using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

[TestClass]
public sealed class AiAgentApprovalApiMySqlTests
{
    [TestMethod]
    public async Task Approval_api_preserves_idempotency_decision_and_single_execution_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentApprovalApiAssertions.VerifyAsync(factory);
    }
}
