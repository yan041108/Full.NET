using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>准确私人评估集、真实FAQ候选和父叶贯通实际比较，撤回及原答案改变后拒绝运行历史。</summary>
[TestClass][DoNotParallelize]
public sealed class AiKnowledgeEvaluationPrivateRunApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Private_dataset_run_keeps_current_complete_answer_and_faq_provenance(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,false,verifyFaqLifecycle:true,verifyFaqSearch:true,verifyEvaluationSources:true,verifyEvaluationPrivateRuns:true);
}
