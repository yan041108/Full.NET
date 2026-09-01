namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 在线会话（刷新令牌族活跃会话）查询与强制下线 API 契约。
/// </summary>
public static class IdentitySessionManagementPermissions
{
    /// <summary>分页查询 Host 在线会话列表。</summary>
    public const string Read = "identity.sessions.read";

    /// <summary>强制下线指定在线会话。</summary>
    public const string Revoke = "identity.sessions.revoke";

    /// <summary>迁移 058 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "identity.sessions.write";
}

/// <summary>Host 在线会话列表项。</summary>
/// <param name="Id">刷新会话稳定标识；对应 JWT sid Claim。</param>
/// <param name="UserId">会话所属的 Host 用户标识。</param>
/// <param name="Username">会话所属的 Host 用户登录名。</param>
/// <param name="DisplayName">会话所属的 Host 用户展示名称。</param>
/// <param name="ClientId">创建会话的客户端标识；用于区分浏览器、第三方或管理端。</param>
/// <param name="ActiveTenantId">会话最近一次切换到的租户标识；仍停留在 Host 时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">会话创建时间（UTC）。</param>
/// <param name="ExpiresAtUtc">会话到期时间（UTC）；到期后刷新令牌族整体失效。</param>
public sealed record HostOnlineSessionResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string ClientId,
    Guid? ActiveTenantId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
