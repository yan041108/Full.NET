using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 MySQL 验证删除清理与文件补偿恢复。</summary>
[TestClass]
public sealed class AiKnowledgeIndexCleanupApiMySqlTests
{
    [TestMethod]
    public async Task Http_cleanup_preserves_audit_and_recovers_async()
    {
        var faults = new AiKnowledgeIndexCleanupFaults();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), configureTestServices: faults.Configure);
        await AiKnowledgeIndexCleanupApiAssertions.VerifyAsync(factory, faults);
    }
}
