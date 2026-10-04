namespace Full.NET.Modules.Ai.Persistence;

/// <summary>一次派发判定点的来源及模型绑定；不包含正文或凭据，也不替代预算和数据去向策略。</summary>
internal sealed class AiKnowledgeIndexDispatchRecord
{
    public Guid TaskId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public Guid LeaseToken { get; init; }
    public DateTimeOffset LeaseExpiresAtUtc { get; init; }
    public Guid FileId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
}
