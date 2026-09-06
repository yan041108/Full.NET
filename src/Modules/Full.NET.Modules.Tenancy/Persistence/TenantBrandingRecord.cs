namespace Full.NET.Modules.Tenancy.Persistence;

/// <summary>租户品牌字段持久化投影。</summary>
internal sealed record TenantBrandingRecord(
    Guid TenantId,
    string? SystemTitle,
    Guid? LogoFileId,
    string? ContactPhone,
    string? ContactEmail,
    string? ContactAddress,
    string? Copyright,
    int Version);
