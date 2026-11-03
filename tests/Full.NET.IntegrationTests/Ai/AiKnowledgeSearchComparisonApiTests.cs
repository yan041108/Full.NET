using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>同输入公开比较的双库范围、真实账本、未知用量及失败关闭；不重跑旧搜索/问答矩阵。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeSearchComparisonApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Comparison_bills_once_preserves_complete_scope_and_separate_scores(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyComparison: true);
}
