namespace Full.NET.Modules.Ai.Persistence;

/// <summary>全局轮询仅返回路由元数据，不提取会话安全戳或正文。</summary>
internal sealed record AiKnowledgeCorrectionPreparationCandidate(Guid Id, Guid VersionId, Guid KnowledgeBaseId, Guid DocumentId,
    Guid OwnerUserId, Guid? TenantId, int DocumentVersion, string ContentType);

/// <summary>准备任务及当前资源事实；原会话仅供 Worker 复核，不进入 HTTP 响应或日志。</summary>
internal sealed class AiKnowledgeCorrectionPreparationRecord
{
    public Guid Id { get; init; }
    public Guid VersionId { get; init; }
    public int DocumentVersion { get; init; }
    public Guid RequestKey { get; init; }
    public string RequestHash { get; init; } = "";
    public Guid ActorUserId { get; init; }
    public Guid SessionId { get; init; }
    public string SessionKind { get; init; } = "";
    public string SecurityStamp { get; init; } = "";
    public string ActorScope { get; init; } = "";
    public string EffectiveScope { get; init; } = "";
    public string StateKey { get; init; } = "";
    public int AttemptCount { get; init; }
    public Guid? LeaseToken { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
    public string? ParserRuntimeId { get; init; }
    public Guid? SourceId { get; init; }
    public string? FailureCode { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string ContentType { get; init; } = "";
    public int IsCurrent { get; init; }
}
