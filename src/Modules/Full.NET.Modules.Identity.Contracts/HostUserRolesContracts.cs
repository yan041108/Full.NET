namespace Full.NET.Modules.Identity.Contracts;

/// <summary>Host 用户可分配角色集合响应。</summary>
/// <param name="UserId">角色集合所属的 Host 用户标识。</param>
/// <param name="RoleIds">当前整量生效的角色标识集合；调用方应按服务端返回结果覆盖本地选择状态。</param>
/// <param name="Version">角色关系快照的并发版本。</param>
public sealed record HostUserRolesResponse(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds,
    int Version);

/// <summary>替换 Host 用户可分配角色集合请求。</summary>
/// <param name="RoleIds">提交后应完整生效的角色标识集合，而不是增量差异。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record ReplaceHostUserRolesRequest(
    IReadOnlyList<Guid> RoleIds,
    int Version);
