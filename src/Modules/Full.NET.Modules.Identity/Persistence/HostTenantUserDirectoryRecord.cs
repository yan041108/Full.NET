namespace Full.NET.Modules.Identity.Persistence;

/// <summary>供 Host 侧租户成员/管理员目录查询使用的用户投影行。</summary>
internal sealed record HostTenantUserDirectoryRecord(
    Guid Id,
    string Username,
    string DisplayName,
    string AccountType,
    bool IsActive,
    string PreferredLocale);
