namespace Full.NET.Modules.Ai.Persistence;

/// <summary>导入日志在外部 I/O 前提交；租约令牌隔离过期执行者，文件状态支持确认和补偿重试。</summary>
internal sealed class AiKnowledgeImportRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid VersionId { get; init; }
    public Guid RequestKey { get; init; }
    public int ExpectedDocumentVersion { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string ParsingPolicyKey { get; init; } = "default";
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid? FileId { get; init; }
    public string StateKey { get; init; } = string.Empty;
    public string? FailureCode { get; init; }
    public Guid? LeaseToken { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
