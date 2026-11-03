using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>使用实际发布/索引和受控模型贯通策略及原操作运行；不重复FAQ旧断言。</summary>
[TestClass][DoNotParallelize]
public sealed class AiKnowledgeEvaluationRunApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Frozen_strategy_runs_actual_models_under_original_operation_identity(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,false,verifyEvaluationRuns:true);
}
