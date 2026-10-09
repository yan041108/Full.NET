using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>通过证明的反馈投影，仅在应用内用于聚合，不进入 HTTP。</summary>
internal sealed record AiKnowledgeAnswerFeedbackExportValue(Guid ActorUserId, string RatingKey, string ReasonKey, int CitationCount);

/// <summary>审核双版本与标签统计的封闭规则，不允许反馈作为模型指令。</summary>
internal static class AiKnowledgeAnswerFeedbackReviewPolicy
{
    internal const string ExportPolicyVersion = "feedback-labels-v1";
    internal static bool IsValid(ReviewAiKnowledgeAnswerFeedbackRequest? request) => request is not null
        && request.DecisionKey is "approved" or "rejected" && request.FeedbackVersion is > 0 and < int.MaxValue
        && request.ReviewVersion is >= 0 and < int.MaxValue;

    internal static IReadOnlyList<AiKnowledgeAnswerFeedbackExportCell> Aggregate(IReadOnlyList<AiKnowledgeAnswerFeedbackExportValue> values)
    {
        if (values.Count > 64 || values.Any(value => value.ActorUserId == Guid.Empty || value.CitationCount is < 0 or > 8
            || !AiKnowledgeAnswerFeedbackPolicy.IsValid(new(value.RatingKey, value.ReasonKey, [], 0), [])))
            throw new InvalidDataException("反馈统计投影无效。");
        // 阈值抑制少量提交者的格子；实时计数仍存在差分风险，不承诺匿名化。
        return values.GroupBy(value => (value.RatingKey, value.ReasonKey, value.CitationCount))
            .Where(group => group.Select(value => value.ActorUserId).Distinct().Count() >= 5)
            .OrderBy(group => group.Key.RatingKey, StringComparer.Ordinal).ThenBy(group => group.Key.ReasonKey, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CitationCount)
            .Select(group => new AiKnowledgeAnswerFeedbackExportCell(group.Key.RatingKey, group.Key.ReasonKey, group.Key.CitationCount, group.Count())).ToArray();
    }
}
