using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>新发布 ELF 的私人评估来源场景；仅使用受控内部模型目的地。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeEvaluationSourceE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Native_retained_evaluation_feedback_requires_current_private_answer_and_complete_sources(DatabaseProvider provider)
    {
        try { await NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, true, verifyFaqLifecycle:true, verifyFaqSearch:true, verifyEvaluationSources:true); }
        finally { FaqLifecycleQueryTrace.Save(); }
    }
}
