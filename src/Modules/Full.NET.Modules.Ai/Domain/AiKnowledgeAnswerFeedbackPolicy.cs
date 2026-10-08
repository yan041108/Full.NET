using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>反馈只接受封闭标签和实际引用集合，不接受用户编写的模型指令。</summary>
internal static class AiKnowledgeAnswerFeedbackPolicy
{
    /// <summary>同时验证固定评价、原因、版本和本回答的实际引用。</summary>
    internal static bool IsValid(SetAiKnowledgeAnswerFeedbackRequest? request, IReadOnlyList<Guid> references)
    {
        if (request is null || request.Version is < 0 or int.MaxValue || request.CitationIds is null
            || request.CitationIds.Count > 8 || request.CitationIds.Any(id => id == Guid.Empty)
            || request.CitationIds.Distinct().Count() != request.CitationIds.Count
            || request.CitationIds.Any(id => !references.Contains(id))) return false;
        return request.RatingKey switch
        {
            "helpful" => request.ReasonKey is "accurate" or "useful",
            "unhelpful" => request.ReasonKey is "incomplete" or "unsupported" or "outdated" or "other",
            _ => false,
        };
    }
}
