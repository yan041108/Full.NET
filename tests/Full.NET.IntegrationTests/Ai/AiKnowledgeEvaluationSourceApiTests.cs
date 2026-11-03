using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只接续真实来源准备后的新评估场景，不重跑已验收的 K04 功能断言。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeEvaluationSourceApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Retained_evaluation_feedback_requires_current_private_answer_and_complete_sources(DatabaseProvider provider)
    {
        try { await NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqLifecycle:true, verifyFaqSearch:true, verifyEvaluationSources:true); }
        finally { FaqLifecycleQueryTrace.Save(); }
    }
}
