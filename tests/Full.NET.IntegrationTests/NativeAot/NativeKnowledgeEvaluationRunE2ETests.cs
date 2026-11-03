using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>新ELF实际执行比较/改写/账本公开接口，准备Worker仍是受控JIT。</summary>
[TestClass][DoNotParallelize]
public sealed class NativeKnowledgeEvaluationRunE2ETests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public Task Native_frozen_strategy_runs_actual_models_under_original_identity(DatabaseProvider provider)=>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider,true,verifyEvaluationRuns:true);
}
