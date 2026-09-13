using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SqlServer 上的 Agent 运行 HTTP 契约。</summary>
[TestClass]
public sealed class AiAgentRunApiSqlServerTests
{
    [TestMethod]
    public async Task Agent_run_api_preserves_readiness_idempotency_and_ownership_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentRunApiAssertions.VerifyAsync(factory);
    }
}
