namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 当前认证用户可安全公开给客户端的身份摘要。
/// </summary>
/// <param name="Id">当前演员的用户标识。</param>
/// <param name="Username">当前演员的登录名。</param>
/// <param name="DisplayName">当前演员的显示名称。</param>
/// <param name="TenantId">当前有效租户；Host 上下文为空。</param>
/// <param name="ActorScope">演员账号所属的原始作用域。</param>
/// <param name="Scope">当前请求使用的有效作用域。</param>
/// <param name="IsSuperAdministrator">当前演员是否来自受保护超级管理员角色。</param>
/// <param name="Permissions">服务端签发的稳定权限码集合。</param>
/// <param name="SessionId">当前刷新会话标识。</param>
/// <param name="PreferredLocale">账号已保存的规范语言偏好。</param>
/// <param name="ProfileVersion">只保护展示资料更新的乐观并发版本。</param>
public sealed record CurrentUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    Guid? TenantId,
    string ActorScope,
    string Scope,
    bool IsSuperAdministrator,
    IReadOnlyCollection<string> Permissions,
    Guid SessionId,
    string PreferredLocale,
    int ProfileVersion);
