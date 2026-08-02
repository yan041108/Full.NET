namespace Full.NET.Modules.Document.Persistence;

internal sealed class DocumentItemRecord
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? CurrentVersionId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public Guid? UpdatedByUserId { get; init; }

    public long Version { get; init; }
}

internal sealed class DocumentItemDetailRecord
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

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

    public DateTimeOffset? VersionCreatedAtUtc { get; init; }

    public Guid? UploadedByUserId { get; init; }
}

internal sealed class DocumentVersionRecord
{
    public Guid Id { get; init; }

    public Guid DocumentItemId { get; init; }

    public Guid FileId { get; init; }

    public int VersionNumber { get; init; }

    public string? ContentHash { get; init; }

    public long SizeBytes { get; init; }

    public Guid UploadedByUserId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
