namespace Full.NET.Modules.Ai.Persistence;

/// <summary>维护者的无正文投影；不读取来源 JSON、文件名、问题、答案、模型或异常载荷。</summary>
internal sealed record AiKnowledgeOperationRecord
{
    public Guid KnowledgeBaseId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public int VersionNumber { get; init; }
    public Guid? FaqId { get; init; }
    public string SourceKind { get; init; } = "";
    public string? PreparationState { get; init; }
    public Guid? TaskId { get; init; }
    public string? TaskState { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? TaskUpdatedAtUtc { get; init; }
    public DateTimeOffset? TaskLeaseExpiresAtUtc { get; init; }
    public int HasActiveGeneration { get; init; }
    public string? ReviewState { get; init; }
    public Guid? PublicationId { get; init; }
    public Guid? PublicationVersionId { get; init; }
    public string? PublicationState { get; init; }
    public DateTimeOffset? EffectiveFromUtc { get; init; }
    public DateTimeOffset? EffectiveToUtc { get; init; }
}
