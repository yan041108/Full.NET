namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档预览转换任务持久化记录。</summary>
internal sealed class DocumentPreviewTaskRecord
{
    public Guid Id { get; init; }

    public Guid DocumentItemId { get; init; }

    public Guid? VersionId { get; init; }

    public string DocumentTitle { get; init; } = string.Empty;

    public Guid SourceFileId { get; init; }

    public string? SourceFileName { get; init; }

    public string? SourceMimeType { get; init; }

    public Guid? OutputFileId { get; init; }

    public string StatusKey { get; init; } = string.Empty;

    public string ProviderKey { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public Guid RequestedByUserId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public long Version { get; init; }
}
