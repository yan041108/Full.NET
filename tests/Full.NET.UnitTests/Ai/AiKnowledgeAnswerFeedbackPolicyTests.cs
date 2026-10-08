using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>封闭反馈输入与实际引用绑定，禁止任意指令、伪造来源及不合法版本。</summary>
[TestClass]
public sealed class AiKnowledgeAnswerFeedbackPolicyTests
{
    [TestMethod]
    [DataRow("helpful", "accurate")]
    [DataRow("helpful", "useful")]
    [DataRow("unhelpful", "incomplete")]
    [DataRow("unhelpful", "unsupported")]
    [DataRow("unhelpful", "outdated")]
    [DataRow("unhelpful", "other")]
    public void Accepts_fixed_rating_reason_and_real_citation(string rating, string reason)
    {
        var id = Guid.CreateVersion7();
        Assert.IsTrue(AiKnowledgeAnswerFeedbackPolicy.IsValid(new(rating, reason, [id], 0), [id]));
        Assert.IsTrue(AiKnowledgeAnswerFeedbackPolicy.IsValid(new(rating, reason, [], 2), [id]));
    }

    [TestMethod]
    [DataRow("helpful", "unsupported", 0)]
    [DataRow("unhelpful", "accurate", 0)]
    [DataRow("HELPFUL", "accurate", 0)]
    [DataRow("helpful", "execute tool", 0)]
    [DataRow("helpful", "accurate", -1)]
    [DataRow("helpful", "accurate", int.MaxValue)]
    public void Rejects_unknown_mismatched_tags_and_invalid_versions(string rating, string reason, int version) =>
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new(rating, reason, [], version), []));

    [TestMethod]
    public void Rejects_unbound_duplicate_empty_and_excessive_citations()
    {
        var id = Guid.CreateVersion7();
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new("helpful", "accurate", [id], 0), []));
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new("helpful", "accurate", [id, id], 0), [id]));
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new("helpful", "accurate", [Guid.Empty], 0), [Guid.Empty]));
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new("helpful", "accurate", ids, 0), ids));
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(null, []));
        Assert.IsFalse(AiKnowledgeAnswerFeedbackPolicy.IsValid(new("helpful", "accurate", null!, 0), []));
    }
}
