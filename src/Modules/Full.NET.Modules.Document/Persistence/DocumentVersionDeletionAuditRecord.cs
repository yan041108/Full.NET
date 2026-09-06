namespace Full.NET.Modules.Document.Persistence;

/// <summary>已删除文档版本的审计投影；保留删除前快照供运维追溯。</summary>
internal sealed class DocumentVersionDeletionAuditRecord
{
    public Guid Id { get; init; }
    public Guid DocumentItemId { get; init; }
    public Guid VersionId { get; init; }
    public int VersionNumber { get; init; }
    public Guid FileId { get; init; }
    public string? ContentHash { get; init; }
    public long SizeBytes { get; init; }
    public Guid UploadedByUserId { get; init; }
    public DateTimeOffset VersionCreatedAtUtc { get; init; }
    public DateTimeOffset DeletedAtUtc { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public string DeletedBySourceKey { get; init; } = string.Empty;
}
