using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native 草稿协议、双库与正式 Vue 桥接；此切片不派发模型。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqDraftE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_drafts_preserve_source_replay_history_and_owner_boundaries(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqDraft: true);
}
