using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>固定审核标签与版本，不接受指令；导出不释放可关联个人的字段或少数群体统计。</summary>
[TestClass]
public sealed class AiKnowledgeAnswerFeedbackReviewPolicyTests
{
    [TestMethod] [DataRow("approved", 1, 0)] [DataRow("rejected", 10, 8)]
    public void Accepts_current_feedback_and_review_versions(string decision, int feedback, int review)
        => Assert.IsTrue(AiKnowledgeAnswerFeedbackReviewPolicy.IsValid(new(decision, feedback, review)));

    [TestMethod] [DataRow("APPROVED", 1, 0)] [DataRow("execute_tool", 1, 0)]
    [DataRow("approved", 0, 0)] [DataRow("approved", int.MaxValue, 0)] [DataRow("approved", 1, -1)] [DataRow("approved", 1, int.MaxValue)]
    public void Rejects_unknown_decisions_and_invalid_versions(string decision, int feedback, int review)
        => Assert.IsFalse(AiKnowledgeAnswerFeedbackReviewPolicy.IsValid(new(decision, feedback, review)));

    [TestMethod]
    public void Repeated_feedback_from_one_actor_does_not_satisfy_small_cell_threshold()
    {
        var actor = Guid.CreateVersion7();
        Assert.HasCount(0, AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(Enumerable.Range(0, 20).Select(_ => Value(actor)).ToArray()));
        Assert.HasCount(0, AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(Enumerable.Range(0, 4).Select(_ => Value()).ToArray()));
    }

    [TestMethod]
    public void Five_distinct_actors_release_only_closed_labels_and_count()
    {
        var values = Enumerable.Range(0, 5).Select(_ => Value()).ToArray();
        var cell = AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(values).Single();
        Assert.AreEqual(new AiKnowledgeAnswerFeedbackExportCell("helpful", "accurate", 1, 5), cell);
        var json = JsonSerializer.SerializeToElement(cell);
        CollectionAssert.AreEquivalent(new[] { "RatingKey", "ReasonKey", "CitationCount", "FeedbackCount" }, json.EnumerateObject().Select(x => x.Name).ToArray());
        foreach (var value in values) Assert.IsFalse(json.ToString().Contains(value.ActorUserId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Small_cells_are_suppressed_independently_and_output_order_is_stable()
    {
        var first = Enumerable.Range(0, 5).Select(_ => Value()).ToArray();
        var second = Enumerable.Range(0, 5).Select(_ => Value() with { RatingKey = "unhelpful", ReasonKey = "outdated", CitationCount = 0 });
        var small = Enumerable.Range(0, 4).Select(_ => Value() with { ReasonKey = "useful" });
        var values = first.Concat(second).Concat(small).ToArray();
        var forward = AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(values);
        var reverse = AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate(values.Reverse().ToArray());
        Assert.HasCount(2, forward);
        CollectionAssert.AreEqual(forward.ToArray(), reverse.ToArray());
    }

    [TestMethod] [DataRow("actor")] [DataRow("reason")] [DataRow("count")]
    public void Invalid_internal_projection_fails_closed(string fault)
    {
        var value = Value();
        value = fault switch { "actor" => value with { ActorUserId = Guid.Empty }, "reason" => value with { ReasonKey = "private instruction" }, _ => value with { CitationCount = 9 } };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerFeedbackReviewPolicy.Aggregate([value]));
    }

    private static AiKnowledgeAnswerFeedbackExportValue Value(Guid? actor = null) => new(actor ?? Guid.CreateVersion7(), "helpful", "accurate", 1);
}
