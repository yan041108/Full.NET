namespace Full.NET.Modules.Identity.Contracts;

/// <summary>请求将现有 Host 账号授予超级管理员系统角色。</summary>
/// <param name="Username">目标 Host 用户名。</param>
/// <param name="CurrentPassword">操作者当前密码。</param>
/// <param name="TotpCode">Production 强认证路径下的 TOTP 验证码；Dev/Test 密码 Provider 可省略。</param>
public sealed record GrantSuperAdministratorRequest(
    string Username,
    string CurrentPassword,
    string? TotpCode = null);

/// <summary>请求撤销超级管理员系统角色，并携带当前操作者的重认证凭据。</summary>
/// <param name="CurrentPassword">操作者当前密码。</param>
/// <param name="TotpCode">Production 强认证路径下的 TOTP 验证码；Dev/Test 密码 Provider 可省略。</param>
public sealed record RevokeSuperAdministratorRequest(
    string CurrentPassword,
    string? TotpCode = null);

/// <summary>描述一个已分配超级管理员系统角色的 Host 账号。</summary>
public sealed record SuperAdministratorResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsActive);

/// <summary>描述一次可追责的超级管理员关系变更审计记录。</summary>
public sealed record SuperAdministratorAuditResponse(
    Guid Id,
    Guid TargetUserId,
    Guid? ActorUserId,
    string EventType,
    string ResultCode,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc);
