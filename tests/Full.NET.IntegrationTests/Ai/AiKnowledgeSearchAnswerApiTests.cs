using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>搜索冻结选择经真实问答、预算、持久化和历史复核；只验证新增贯通路径。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeSearchAnswerApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Search_answer_freezes_publication_before_billing_and_revalidates_history(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifySearchAnswer: true);
}
