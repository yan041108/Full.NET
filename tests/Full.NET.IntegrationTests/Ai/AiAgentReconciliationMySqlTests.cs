using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 人工对账与 SQL Server 保持相同的真实 HTTP 和原账本行为。</summary>
[TestClass]
public sealed class AiAgentReconciliationMySqlTests
{
    [TestMethod]
    public async Task Reconciliation_preserves_original_ledger_and_immutable_evidence_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            settingsOverrides: AiAgentRuntimeTestSettings.ForIntegrationTests());
        await AiAgentReconciliationAssertions.VerifyAsync(factory);
    }
}
