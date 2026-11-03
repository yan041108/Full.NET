using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>FAQ 草稿使用真实双库、不可变原始来源及正式公开接口，不代替后续审核发布。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqDraftApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_drafts_preserve_source_replay_history_and_owner_boundaries(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqDraft: true);
}
