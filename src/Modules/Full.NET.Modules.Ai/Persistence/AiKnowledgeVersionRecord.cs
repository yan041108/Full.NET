namespace Full.NET.Modules.Ai.Persistence;

/// <summary>不可变版本投影；FileId 仅在已授权的内部文件边界使用。</summary>
internal sealed class AiKnowledgeVersionRecord
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public Guid FileId { get; init; }
    public int VersionNumber { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string ParsingPolicyKey { get; init; } = "default";
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid UploadedByUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string ProcessingStatus { get; init; } = "pending_processing";
}
