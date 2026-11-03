using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生公开比较实际消费受控 TLS 模型、账本和双库来源，可连接正式 Vue。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeSearchComparisonE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_comparison_bills_once_preserves_scope_and_separate_scores(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyComparison: true);
}
