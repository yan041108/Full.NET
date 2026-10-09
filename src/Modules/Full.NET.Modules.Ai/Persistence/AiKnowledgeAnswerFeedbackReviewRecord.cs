namespace Full.NET.Modules.Ai.Persistence;

/// <summary>同模块 JOIN 的冻结审核候选；正文从不查询，所属人仅用于历史绑定解码。</summary>
internal sealed record AiKnowledgeAnswerFeedbackReviewRecord
{
    public Guid FeedbackId { get; init; }
    public Guid SessionId { get; init; }
    public Guid MessageId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string RatingKey { get; init; } = "";
    public string ReasonKey { get; init; } = "";
    public string CitationIdsJson { get; init; } = "[]";
    public string LineageHash { get; init; } = "";
    public int FeedbackVersion { get; init; }
    public string KnowledgeLineage { get; init; } = "";
    public int? ReviewFeedbackVersion { get; init; }
    public string? ReviewLineageHash { get; init; }
    public Guid? ReviewerUserId { get; init; }
    public string? DecisionKey { get; init; }
    public int? ReviewVersion { get; init; }
}
