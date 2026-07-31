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
    string ProviderKey,
    string StorageKey,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);

internal sealed record DeletedHostFileBlobRecord(
    Guid Id,
    string ProviderKey,
    string StorageKey,
    DateTimeOffset DeletedAtUtc);

internal sealed record PendingHostFileRecord(
    Guid Id,
    string ProviderKey,
    string StorageKey,
    DateTimeOffset CreatedAtUtc,
    string StorageState = "pending");
