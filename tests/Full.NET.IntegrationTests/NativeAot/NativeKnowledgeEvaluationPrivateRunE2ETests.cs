using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>新ELF的私人完整来源实际评估；租户聊天历史仍是只读夹具，租户比较为真实模型派发。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeKnowledgeEvaluationPrivateRunE2ETests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Native_private_dataset_run_retains_answer_faq_and_leaf_authority(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,true,verifyFaqLifecycle:true,verifyFaqSearch:true,verifyEvaluationSources:true,verifyEvaluationPrivateRuns:true);
}
