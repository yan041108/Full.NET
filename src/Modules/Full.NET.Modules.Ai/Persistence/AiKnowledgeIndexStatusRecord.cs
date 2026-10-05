namespace Full.NET.Modules.Ai.Persistence;

/// <summary>无正文的权威状态快照；内部活动令牌仅用于核对完成证明，禁止序列化到 HTTP。</summary>
internal sealed record AiKnowledgeIndexStatusRecord
{
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid OwnerUserId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public int IsDeleted { get; init; }
    public int CleanupPending { get; init; }
    public int HasDerivedData { get; init; }
    public Guid? TaskId { get; init; }
    public string? StateKey { get; init; }
    public int IsCurrent { get; init; }
    public Guid? ActiveTaskId { get; init; }
    public Guid? ActiveLeaseToken { get; init; }
    public string? ActiveReceiptHash { get; init; }
    public string? ActiveManifestHash { get; init; }
    public string? ActivePlanHash { get; init; }
    public Guid FileId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid? ModelConfigId { get; init; }
    public int? ModelConfigVersion { get; init; }
    public string? ProviderKey { get; init; }
    public string? ModelId { get; init; }
}
