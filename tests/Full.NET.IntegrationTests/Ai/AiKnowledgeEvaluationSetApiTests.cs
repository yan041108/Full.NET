using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>独立评估集的双库持久化与授权；私人反馈来源另需真实源专属场景。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeEvaluationSetApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Immutable_evaluation_sets_preserve_scope_idempotency_and_independent_approval(DatabaseProvider provider) => KnowledgeEvaluationSetScenario.VerifyAsync(provider,false);
}
