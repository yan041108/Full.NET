using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

[TestClass]
public sealed class AiAgentDelegationApiSqlServerTests
{
    [TestMethod]
    public async Task Delegation_api_preserves_scope_revocation_and_delegated_decide_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentDelegationApiAssertions.VerifyAsync(factory);
    }
}
