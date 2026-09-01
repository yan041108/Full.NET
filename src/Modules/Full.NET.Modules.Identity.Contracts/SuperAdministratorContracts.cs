namespace Full.NET.Modules.Identity.Contracts;

/// <summary>请求将现有 Host 账号授予超级管理员系统角色。</summary>
/// <param name="Username">目标 Host 用户名；必须为已存在且活动的普通账号。</param>
/// <param name="CurrentPassword">操作者（当前超级管理员）的明文密码；用于高风险操作重认证。</param>
/// <param name="TotpCode">Production 强认证路径下的 TOTP 验证码；Dev/Test 密码 Provider 可省略。</param>
public sealed record GrantSuperAdministratorRequest(
    string Username,
    string CurrentPassword,
    string? TotpCode = null);

/// <summary>
/// 请求撤销超级管理员系统角色，并携带当前操作者的重认证凭据。
/// </summary>
/// <remarks>
/// 服务端须执行最后一名保护：当目标为最后一名有效超级管理员时，
/// 无论操作者身份如何，均应拒绝并返回稳定失败码。
/// </remarks>
/// <param name="CurrentPassword">操作者（当前超级管理员）的明文密码；用于高风险操作重认证。</param>
/// <param name="TotpCode">Production 强认证路径下的 TOTP 验证码；Dev/Test 密码 Provider 可省略。</param>
public sealed record RevokeSuperAdministratorRequest(
    string CurrentPassword,
    string? TotpCode = null);

/// <summary>描述一个已分配超级管理员系统角色的 Host 账号。</summary>
/// <param name="UserId">Host 用户稳定标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="IsActive">账号是否处于活动状态；禁用账号不参与最后一名保护计数。</param>
public sealed record SuperAdministratorResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    bool IsActive);

/// <summary>描述一次可追责的超级管理员关系变更审计记录。</summary>
/// <param name="Id">审计记录稳定标识。</param>
/// <param name="TargetUserId">被授予或撤销角色的目标用户标识。</param>
/// <param name="ActorUserId">执行变更的操作者标识；自动化迁移场景可能为 <see langword="null"/>。</param>
/// <param name="EventType">变更事件种类机器码：grant 或 revoke。</param>
/// <param name="ResultCode">本次操作的稳定结果码；成功时为空，失败时对齐 Identity 错误码目录。</param>
/// <param name="Succeeded">本次是否实际提交成功。</param>
/// <param name="OccurredAtUtc">事件发生时间（UTC）。</param>
public sealed record SuperAdministratorAuditResponse(
    Guid Id,
    Guid TargetUserId,
    Guid? ActorUserId,
    string EventType,
    string ResultCode,
    bool Succeeded,
    DateTimeOffset OccurredAtUtc);
