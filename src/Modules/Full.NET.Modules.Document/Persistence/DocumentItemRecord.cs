namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentItemRecord
{
    public Guid Id { get; init; }

    public string DocumentNo { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public string? CategoryColor { get; init; }

    public int DocumentType { get; init; }

    public long SizeKb { get; init; }

    public string? Thumbnail { get; init; }

    public int Status { get; init; }

    public int AccessCount { get; init; }

    public int Sort { get; init; }

    public DateTimeOffset? LastAccessTime { get; init; }

    public Guid? CurrentVersionId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public Guid? UpdatedByUserId { get; init; }

    public long Version { get; init; }

    public DateTimeOffset? DeletedAtUtc { get; init; }

    public Guid? DeletedByUserId { get; init; }
}

internal sealed class DocumentItemDetailRecord
{
    public Guid Id { get; init; }

    public string DocumentNo { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public string? CategoryColor { get; init; }

    public int DocumentType { get; init; }

    public long SizeKb { get; init; }

    public string? Thumbnail { get; init; }

    public int Status { get; init; }

    public int AccessCount { get; init; }

    public int Sort { get; init; }

    public DateTimeOffset? LastAccessTime { get; init; }

    public Guid? CurrentVersionId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public Guid? UpdatedByUserId { get; init; }

    public long Version { get; init; }

    public Guid? VersionId { get; init; }

    public int? VersionNumber { get; init; }

    public Guid? FileId { get; init; }

    public string? ContentHash { get; init; }

    public long? SizeBytes { get; init; }

    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    public string? Extension { get; init; }

    public long? FileSizeBytes { get; init; }

    public string? ChangeDescription { get; init; }

    public DateTimeOffset? VersionCreatedAtUtc { get; init; }

    public Guid? UploadedByUserId { get; init; }

    public DateTimeOffset? DeletedAtUtc { get; init; }

    public Guid? DeletedByUserId { get; init; }
}

internal sealed class DocumentVersionRecord
{
    public Guid Id { get; init; }

    public Guid DocumentItemId { get; init; }

    public Guid FileId { get; init; }

    public int VersionNumber { get; init; }

    public string? ContentHash { get; init; }

    public long SizeBytes { get; init; }

    public string? ChangeDescription { get; init; }

    public Guid UploadedByUserId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class DocumentPermissionRecord
{
    public Guid Id { get; init; }

    public Guid DocumentId { get; init; }

    public Guid UserId { get; init; }

    public string PermissionLevel { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class DocumentShareRecord
{
    public Guid Id { get; init; }

    public Guid DocumentId { get; init; }

    public string ShareCode { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpireTime { get; init; }

    /// <summary>
    /// 不可逆口令哈希；使用 ASP.NET Core PasswordHasher 生成（PBKDF2 + 随机盐）。
    /// 明文口令永不落库；若分享未设置口令则为 null。
    /// </summary>
    public string? PasswordHash { get; init; }

    public int? MaxAccessCount { get; init; }

    public int AccessCount { get; init; }

    public bool IsEnabled { get; init; }

    public long Version { get; init; }
}

internal sealed class DocumentStatisticsSummaryRecord
{
    public long TotalItems { get; init; }

    public long TotalVersions { get; init; }

    public long TotalSizeKb { get; init; }
}

internal sealed class DocumentStatisticsByTypeRecord
{
    public string? Extension { get; init; }

    public long Count { get; init; }

    public long TotalSizeKb { get; init; }
}

internal sealed class DocumentStatisticsByCategoryRecord
{
    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public long Count { get; init; }
}

internal sealed class DocumentStatisticsShareCountRecord
{
    public long ShareCount { get; init; }

    public long TodayAccessCount { get; init; }

    public long TodayDownloadCount { get; init; }

    public long TodayCreatedCount { get; init; }

    public long RecycleBinCount { get; init; }
}
