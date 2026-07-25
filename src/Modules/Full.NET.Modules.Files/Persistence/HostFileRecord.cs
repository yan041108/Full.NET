namespace Full.NET.Modules.Files.Persistence;

internal sealed record HostFileListRecord(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);

internal sealed record HostFileDetailRecord(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string StorageKey,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);
