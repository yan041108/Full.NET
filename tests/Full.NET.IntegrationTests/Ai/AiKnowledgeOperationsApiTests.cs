using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>任务运营的双库元数据、维护范围和精确权限；不代替 Worker 或来源载荷验收。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeOperationsApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Owned_operations_metadata_preserves_scope_paging_and_publication_separation(DatabaseProvider provider) => KnowledgeOperationsScenario.VerifyAsync(provider,false);
}
