using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 墓碑恢复、分页、租约与实际 Worker 装配。</summary>
[TestClass]
public sealed class AiKnowledgeCleanupWorkerMySqlTests
{
    [TestMethod]
    public async Task Worker_recovers_durable_cleanup_without_http_retry() =>
        await AiKnowledgeCleanupWorkerAssertions.VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
}
