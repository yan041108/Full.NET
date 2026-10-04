namespace Full.NET.Modules.Ai.Persistence;

/// <summary>任务代次与租约令牌分离；失败或失效任务仍可定位，但不代表索引就绪。</summary>
internal sealed class AiKnowledgeIndexTaskRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid ModelConfigId { get; init; }
    public int ModelConfigVersion { get; init; }
    public string StateKey { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public string? FailureCode { get; init; }
    public Guid? LeaseToken { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
