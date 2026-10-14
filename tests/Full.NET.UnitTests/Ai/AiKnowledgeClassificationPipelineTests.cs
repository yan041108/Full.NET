using Full.NET.Abstractions.Ids;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Knowledge;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>在实际检索和上下文入口验证分类拒绝，不以纯策略单测替代接线。</summary>
public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod]
    public async Task Data_policy_denies_rerank_before_returning_retrieved_chunks()
    {
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["rerank"] = ["public"] });
        var fixture = await RetrievalFixture.Create(false, policy);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Read());

        Assert.IsTrue(fixture.Query.SourceReads > 0);
        Assert.IsFalse(fixture.F.Transactions.HasTransaction);
    }

    [TestMethod]
    public async Task Data_policy_denies_context_before_allocating_citation_identifiers()
    {
        var fixture = await RetrievalFixture.Create(false);
        var ids = Substitute.For<IIdGenerator>();
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["context"] = ["public"] });
        var pipeline = new AiKnowledgeAnswerPipeline(fixture.Service, ids, policy);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pipeline.PrepareAsync(
            [fixture.F.Binding.KnowledgeBaseId], fixture.Actor, fixture.Model,
            AiKnowledgeRetrievalRuleTests.Vector(1, 0), "question", default));

        ids.DidNotReceive().NewId();
        Assert.IsFalse(fixture.F.Transactions.HasTransaction);
    }
}
