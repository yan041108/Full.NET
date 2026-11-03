namespace Full.NET.Modules.Ai.Persistence;

/// <summary>专用窄用途权限后才读取的批准反馈；不查询用户问题、标题或其他消息。</summary>
internal sealed record AiKnowledgeFaqFeedbackRecord
{
    public Guid FeedbackId { get; init; }
    public int FeedbackVersion { get; init; }
    public Guid ReviewId { get; init; }
    public int ReviewVersion { get; init; }
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public Guid OwnerUserId { get; init; }
    public Guid? TenantId { get; init; }
    public string LineageHash { get; init; } = "";
    public string KnowledgeLineage { get; init; } = "";
    public string Answer { get; init; } = "";
}
