using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 人工对账的真实 HTTP、条件写入及账本语义。</summary>
[TestClass]
public sealed class AiAgentReconciliationSqlServerTests
{
    [TestMethod]
    public async Task Reconciliation_preserves_original_ledger_and_immutable_evidence_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentReconciliationAssertions.VerifyAsync(factory);
    }
}
