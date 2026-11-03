using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>独立评估集公开路径的实际 Native 静态闭包；不声明实际模型评估或私人来源链通过。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeEvaluationSetE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_independent_evaluation_sets_persist_freeze_and_review(DatabaseProvider provider) => KnowledgeEvaluationSetScenario.VerifyAsync(provider,true);
}
