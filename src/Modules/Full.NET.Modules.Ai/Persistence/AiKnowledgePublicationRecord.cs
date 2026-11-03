using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>提案正文不可变，仅审核与正式发布状态按版本推进。</summary>
internal sealed class AiKnowledgePublicationRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid GenerationId { get; init; }
    public Guid VersionId { get; init; }
    public Guid RequestKey { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid SessionId { get; init; }
    public string SessionKind { get; init; } = "";
    public string SecurityStamp { get; init; } = "";
    public string ActorScope { get; init; } = "";
    public string EffectiveScope { get; init; } = "";
    public string SnapshotJson { get; init; } = "";
    public string SnapshotHash { get; init; } = "";
    public string StateKey { get; init; } = "";
    public DateTimeOffset EffectiveFromUtc { get; init; }
    public DateTimeOffset EffectiveToUtc { get; init; }
    public int ExpectedHeadVersion { get; init; }
    public int DocumentVersion { get; init; }
    public int KnowledgeBaseVersion { get; init; }
    public Guid? ReviewerUserId { get; init; }
    public DateTimeOffset? ReviewedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public int Version { get; init; }
}
/// <summary>发布头不删除以避免 ABA；下架同样递增版本。</summary>
internal sealed class AiKnowledgePublicationHeadRecord
{
    public Guid Id { get; init; }
    public Guid? PublicationId { get; init; }
    public int Version { get; init; }
}
/// <summary>冻结候选和审核策略，旧授权与外部凭据不进入快照。</summary>
internal sealed record AiKnowledgePublicationSnapshot(int PolicyVersion, AiKnowledgeIndexStatusRecord Source);
