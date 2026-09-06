namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>Host 侧查询租户成员/管理员目录的响应契约。</summary>
public static class HostTenantDirectoryPermissions
{
    /// <summary>读取指定租户的成员与管理员目录。</summary>
    public const string ReadDirectory = "tenancy.tenants.read_directory";
}

/// <summary>租户成员列表项。</summary>
/// <param name="UserId">用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="AccountType">账号类型机器码。</param>
/// <param name="IsActive">账号是否活动。</param>
public sealed record HostTenantMemberResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string AccountType,
    bool IsActive);

/// <summary>租户成员分页结果。</summary>
/// <param name="TenantId">租户标识。</param>
/// <param name="Items">当前页成员。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">页大小。</param>
/// <param name="Total">总记录数。</param>
public sealed record HostTenantMembersPageResponse(
    Guid TenantId,
    IReadOnlyList<HostTenantMemberResponse> Items,
    int Page,
    int PageSize,
    long Total);

/// <summary>租户管理员分页结果。</summary>
/// <param name="TenantId">租户标识。</param>
/// <param name="Items">当前页管理员。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">页大小。</param>
/// <param name="Total">总记录数。</param>
public sealed record HostTenantAdministratorsPageResponse(
    Guid TenantId,
    IReadOnlyList<HostTenantMemberResponse> Items,
    int Page,
    int PageSize,
    long Total);
