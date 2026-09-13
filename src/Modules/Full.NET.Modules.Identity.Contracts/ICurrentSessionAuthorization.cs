namespace Full.NET.Modules.Identity.Contracts;

/// <summary>权威确认当前交互会话与权限，不接受调用方提供的用户或租户覆盖。</summary>
public interface ICurrentSessionAuthorization
{
    /// <summary>账号、会话、租户或权限失效时返回空；不支持持久委托或 API Key 代替会话。</summary>
    Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default);
}

/// <summary>权威授权后可供业务模块使用的最小主体快照。</summary>
public sealed record AuthorizedSessionActor(Guid UserId, Guid? TenantId, Guid SessionId);
