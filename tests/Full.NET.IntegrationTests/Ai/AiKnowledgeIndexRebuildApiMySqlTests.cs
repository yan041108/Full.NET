using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 MySQL 验证显式重建、收费隔离、撤权与活动代次切换。</summary>
[TestClass]
public sealed class AiKnowledgeIndexRebuildApiMySqlTests
{
    [TestMethod]
    public Task Rebuild_preserves_old_charges_and_active_generation_until_atomic_switch() =>
        AiKnowledgeIndexRebuildApiAssertions.VerifyAsync(DatabaseProvider.MySql);
}
