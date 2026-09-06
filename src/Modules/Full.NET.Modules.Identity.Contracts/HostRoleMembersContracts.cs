namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host 角色成员管理权限。</summary>
public static class IdentityRoleMemberManagementPermissions
{
    /// <summary>替换 Host 角色成员集合。</summary>
    public const string ReplaceMembers = "identity.roles.replace_members";
}

/// <summary>Host 角色成员列表项。</summary>
/// <param name="UserId">成员用户标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="IsActive">成员账号是否处于活动状态。</param>
public sealed record HostRoleMemberResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsActive);

/// <summary>Host 角色成员分页列表响应。</summary>
/// <param name="RoleId">目标角色标识。</param>
/// <param name="Items">当前页成员集合。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">页大小。</param>
/// <param name="Total">总成员数。</param>
/// <param name="Version">角色乐观并发版本；成员替换时须回传。</param>
public sealed record HostRoleMembersPageResponse(
    Guid RoleId,
    IReadOnlyList<HostRoleMemberResponse> Items,
    int Page,
    int PageSize,
    long Total,
    int Version);

/// <summary>替换 Host 角色成员集合请求。</summary>
/// <param name="UserIds">提交后应完整生效的成员用户标识集合。</param>
/// <param name="Version">调用方看到的当前角色版本。</param>
public sealed record ReplaceHostRoleMembersRequest(
    IReadOnlyList<Guid> UserIds,
    int Version);

/// <summary>Host 角色成员替换结果。</summary>
/// <param name="RoleId">目标角色标识。</param>
/// <param name="UserIds">替换后生效的成员用户标识集合。</param>
/// <param name="Version">替换后的角色版本。</param>
public sealed record HostRoleMembersAssignmentResponse(
    Guid RoleId,
    IReadOnlyList<Guid> UserIds,
    int Version);
