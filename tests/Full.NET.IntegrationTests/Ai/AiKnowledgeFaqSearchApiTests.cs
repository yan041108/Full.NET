using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只运行新 FAQ 候选与问答/历史消费者，不重复后续旧生命周期场景。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqSearchApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_search_comparison_refinement_answer_and_history_keep_full_nodes(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqLifecycle: true, verifyFaqSearch: true);
}
