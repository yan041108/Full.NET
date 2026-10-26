namespace Full.NET.Modules.Files.Persistence;

/// <summary>租户资源描述的固定 SQL 投影，列顺序同步静态 AOT 物化器。</summary>
internal sealed record TenantResourceFileDescriptorRecord(Guid Id, string OriginalFileName, string ContentType,
    long SizeBytes, string ContentHash, Guid CreatedByUserId);
