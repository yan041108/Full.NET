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
public sealed record HostOnlineSessionResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    string ClientId,
    Guid? ActiveTenantId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
