using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySql 上的 Agent 运行 HTTP 契约。</summary>
[TestClass]
public sealed class AiAgentRunApiMySqlTests
{
    [TestMethod]
    public async Task Agent_run_api_preserves_readiness_idempotency_and_ownership_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentRunApiAssertions.VerifyAsync(factory);
    }
}
