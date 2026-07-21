namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host 用户可分配角色集合响应。</summary>
public sealed record HostUserRolesResponse(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds,
    int Version);

/// <summary>替换 Host 用户可分配角色集合请求。</summary>
public sealed record ReplaceHostUserRolesRequest(
    IReadOnlyList<Guid> RoleIds,
    int Version);
