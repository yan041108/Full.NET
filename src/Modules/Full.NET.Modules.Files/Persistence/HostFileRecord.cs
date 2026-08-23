namespace Full.NET.Modules.Files.Persistence;

internal sealed record HostFileListRecord(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId);

/// <summary>Host 文件详情行，含 ProviderKey/StorageKey 用于内容读取与 Blob 回收定位。</summary>
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

/// <summary>已软删除待 Blob 回收的 Host 文件行，由清理 Runner 按 keyset 游标消费。</summary>
internal sealed record DeletedHostFileBlobRecord(
    Guid Id,
    string ProviderKey,
    string StorageKey,
    DateTimeOffset DeletedAtUtc);

/// <summary>处于 <c>pending</c>/<c>publishing</c> 状态的 Host 文件行，由对账 Runner 按陈旧阈值消费。</summary>
internal sealed record PendingHostFileRecord(
    Guid Id,
    string ProviderKey,
    string StorageKey,
    DateTimeOffset CreatedAtUtc,
    string StorageState = "pending");
