using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>同一正式 Native API 完成 FAQ 物化准入、审核发布及正文交付；索引 Worker 仍为真实 JIT 宿主。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqLifecycleE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_revision_index_review_publish_and_revoke_preserve_provenance(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqLifecycle: true);
}
