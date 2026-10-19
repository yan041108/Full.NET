// 权限快照读取 Port；供授权中间件在请求初态加载精确权限集。
namespace Full.NET.Modules.Identity.Authorization;

/// <summary>读取用户在某作用域下的权限快照；供授权管线缓存与失效，不写入权限。</summary>
internal interface IPermissionSnapshotReader
{
    Task<PermissionSnapshot> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>权限快照只读投影；超级管理员标记与精确权限码列表。</summary>
internal sealed record PermissionSnapshot(
    IReadOnlyList<string> Permissions,
    bool IsSuperAdministrator);
