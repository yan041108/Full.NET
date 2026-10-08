namespace Full.NET.Modules.Ai.Contracts;

/// <summary>本人回答反馈；版本零仅用于首次提交，引用标识必须来自该回答。</summary>
/// <param name="RatingKey">helpful 或 unhelpful。</param>
/// <param name="ReasonKey">受控原因键，不能作为模型指令。</param>
/// <param name="CitationIds">最多八个真实服务器引用标识。</param>
/// <param name="Version">当前反馈版本，首次提交为零。</param>
public sealed record SetAiKnowledgeAnswerFeedbackRequest(string RatingKey, string ReasonKey, IReadOnlyList<Guid> CitationIds, int Version);

/// <summary>未审核或已撤回的本人反馈；不包含回答正文或来源元数据。</summary>
/// <param name="Id">稳定反馈标识。</param>
/// <param name="MessageId">实际回答消息标识。</param>
/// <param name="RatingKey">未撤回的固定评价。</param>
/// <param name="ReasonKey">未撤回的固定原因。</param>
/// <param name="CitationIds">服务器确认的引用标识。</param>
/// <param name="StatusKey">pending_review 或 withdrawn，不表示已审核。</param>
/// <param name="Version">并发控制版本。</param>
/// <param name="UpdatedAtUtc">最后更新 UTC 时间。</param>
public sealed record AiKnowledgeAnswerFeedbackResponse(Guid Id, Guid MessageId, string? RatingKey, string? ReasonKey,
    IReadOnlyList<Guid> CitationIds, string StatusKey, int Version, DateTimeOffset UpdatedAtUtc);

/// <summary>本人清除反馈所需的版本状态，不包含评价、原因或来源引用；来源撤权后仍可读取。</summary>
/// <param name="MessageId">本人实际知识答案消息。</param>
/// <param name="StatusKey">待审核或已撤回状态。</param>
/// <param name="Version">撤回必须使用的当前版本。</param>
public sealed record AiKnowledgeAnswerFeedbackStateResponse(Guid MessageId, string StatusKey, int Version);

/// <summary>回答反馈的精确权限；不授予其他用户会话或来源读取权。</summary>
public static class AiKnowledgeAnswerFeedbackPermissions
{
    /// <summary>读取本人反馈。</summary>
    public const string Read = "ai.knowledge_answer_feedback.read";
    /// <summary>提交或修改本人反馈。</summary>
    public const string Submit = "ai.knowledge_answer_feedback.submit";
    /// <summary>读取清除版本并撤回本人反馈。</summary>
    public const string Withdraw = "ai.knowledge_answer_feedback.withdraw";
}
