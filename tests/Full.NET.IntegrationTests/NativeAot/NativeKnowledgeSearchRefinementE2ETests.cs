using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生公开受控实验实际消费隔离 TLS 模型、双库账本并连接正式 Vue。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeSearchRefinementE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_refinement_retains_original_scope_and_independent_settled_stages(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyRefinement: true);
}
