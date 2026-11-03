namespace Full.NET.Modules.Ai.Persistence;

/// <summary>组织信息和发布状态的固定查询投影。</summary>
internal sealed class AiKnowledgeDirectoryRecord
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public Guid? FolderId { get; init; }
    public string TagsJson { get; init; } = "[]";
    public Guid? ResponsibleUserId { get; init; }
    public int Version { get; init; }
    public Guid? LastRequestKey { get; init; }
    public string? RequestHash { get; init; }
    public int DocumentVersion { get; init; }
    public string PublicationState { get; init; } = "draft";
    public Guid? PublicationId { get; init; }
    public int HeadVersion { get; init; }
    public Guid? CandidateVersionId { get; init; }
    public Guid? PendingPublicationId { get; init; }
}
