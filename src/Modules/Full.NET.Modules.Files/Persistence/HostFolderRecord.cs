namespace Full.NET.Modules.Files.Persistence;

/// <summary>Host 虚拟目录行，用于树构建与详情映射。</summary>
internal sealed record HostFolderRecord(
    Guid Id,
    Guid? ParentId,
    string Name,
    int DisplayOrder,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId);
