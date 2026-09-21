namespace Full.NET.Modules.Identity.Contracts;

/// <summary>账号操作挑战用途。</summary>
/// <remarks>枚举数值发布后不可改名或删除；新增成员只能追加到末尾，已发布数值不得调整顺序，以保证持久化与协议稳定。</remarks>
public enum IdentityAccountChallengePurpose : byte
{
    /// <summary>注册流程邮箱验证；挑战通过后允许继续注册。</summary>
    RegistrationEmailVerification = 1,
    /// <summary>密码找回流程邮箱验证；挑战通过后允许设置新密码。</summary>
    PasswordRecovery = 2,
    /// <summary>邀请接受流程邮箱验证；挑战通过后允许完成邀请接受。</summary>
    InvitationEmailVerification = 3,
}

/// <summary>注册策略三态模式。</summary>
/// <remarks>枚举数值发布后不可改名或删除；新增成员只能追加到末尾，已发布数值不得调整顺序。</remarks>
public enum IdentityRegistrationMode : byte
{
    /// <summary>禁用注册；任何注册请求都被拒绝。</summary>
    Disabled = 0,
    /// <summary>仅邀请注册；须凭有效邀请 Token 才能创建账号。</summary>
    InvitationOnly = 1,
    /// <summary>开放注册；任何符合挑战验证的邮箱都可创建账号。</summary>
    Open = 2,
}

/// <summary>注册邀请状态。</summary>
/// <remarks>枚举数值发布后不可改名或删除；新增成员只能追加到末尾，已发布数值不得调整顺序。</remarks>
public enum IdentityRegistrationInvitationStatus : byte
{
    /// <summary>待处理；邀请已发出但尚未完成账号创建。</summary>
    Pending = 0,
    /// <summary>账号已创建但尚未完成首次激活；等待用户完成首登或挑战。</summary>
    AccountCreatedPendingOnboarding = 1,
    /// <summary>已激活；用户已可正常登录。</summary>
    Activated = 2,
    /// <summary>已撤销；邀请失效，不可再据此创建账号。</summary>
    Revoked = 3,
}

/// <summary>账号挑战接受结果；调用方据此判断挑战窗口是否仍有效。</summary>
/// <remarks>挑战为一次性短期凭据，过期后必须重新发起；服务端应保证挑战状态迁移的原子性。</remarks>
/// <param name="ChallengeId">挑战稳定标识。</param>
/// <param name="ExpiresAtUtc">挑战过期时间（UTC）；过期后不可用于完成后续操作。</param>
public sealed record AccountChallengeAcceptedResponse(Guid ChallengeId, DateTimeOffset ExpiresAtUtc);

/// <summary>验证注册邀请 Token 请求；用于在提交注册前确认邀请仍然有效。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="InvitationId">邀请稳定标识。</param>
/// <param name="InvitationToken">邀请一次性 Token；服务端校验有效性与未过期。</param>
public sealed record VerifyRegistrationInvitationRequest(Guid InvitationId, string InvitationToken);

/// <summary>验证注册邀请 Token 响应；返回邀请上下文供调用方预填注册表单。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="InvitationId">邀请稳定标识。</param>
/// <param name="TenantId">邀请关联的租户标识。</param>
/// <param name="Email">邀请目标邮箱；调用方应禁止修改。</param>
/// <param name="RegistrationWayId">邀请指定的注册路径标识。</param>
/// <param name="ExpiresAtUtc">邀请过期时间（UTC）。</param>
public sealed record VerifyRegistrationInvitationResponse(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    Guid RegistrationWayId,
    DateTimeOffset ExpiresAtUtc);

/// <summary>发送注册邮箱挑战请求；服务端据此向邮箱投递验证码。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Email">挑战目标邮箱。</param>
/// <param name="Purpose">挑战用途；不同用途产生不同挑战，禁止复用。</param>
/// <param name="InvitationId">关联邀请标识；Purpose 为 InvitationEmailVerification 时必填。</param>
/// <param name="InvitationToken">关联邀请 Token；Purpose 为 InvitationEmailVerification 时必填。</param>
public sealed record SendRegistrationEmailChallengeRequest(
    string Email,
    IdentityAccountChallengePurpose Purpose,
    Guid? InvitationId = null,
    string? InvitationToken = null);

/// <summary>注册账号请求；挑战与邀请二选一或组合提供。</summary>
/// <remarks>服务端必须原子完成账号创建、挑战核销与邀请状态迁移；半提交会破坏可重放与撤销语义。</remarks>
/// <param name="Email">账号邮箱；必须与挑战邮箱一致。</param>
/// <param name="DisplayName">账号展示名。</param>
/// <param name="Password">明文密码；只允许存在于当前请求边界，禁止写日志或缓存。</param>
/// <param name="ChallengeId">已完成验证的挑战标识。</param>
/// <param name="ChallengeCode">挑战验证码；服务端校验一次性消费。</param>
/// <param name="RegistrationWayId">注册路径标识；省略时使用默认路径。</param>
/// <param name="InvitationId">关联邀请标识；邀请模式下必填。</param>
/// <param name="InvitationToken">关联邀请 Token；邀请模式下必填。</param>
public sealed record RegisterAccountRequest(
    string Email,
    string DisplayName,
    string Password,
    Guid ChallengeId,
    string ChallengeCode,
    Guid? RegistrationWayId = null,
    Guid? InvitationId = null,
    string? InvitationToken = null);

/// <summary>注册账号响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="UserId">新创建或匹配到的用户标识。</param>
/// <param name="Created">是否实际新建账号；<see langword="false"/> 表示命中幂等并返回既有账号。</param>
public sealed record RegisterAccountResponse(Guid UserId, bool Created);

/// <summary>发起密码找回请求；服务端据此向邮箱投递挑战。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Email">账号邮箱。</param>
public sealed record RequestPasswordRecoveryRequest(string Email);

/// <summary>确认密码找回请求；挑战核销通过后写入新密码。</summary>
/// <remarks>挑战为一次性凭据；服务端必须原子完成挑战核销与新密码写入，避免挑战被多次用于重置。</remarks>
/// <param name="ChallengeId">挑战稳定标识。</param>
/// <param name="ChallengeCode">挑战验证码；服务端校验一次性消费。</param>
/// <param name="NewPassword">新明文密码；只允许存在于当前请求边界。</param>
public sealed record ConfirmPasswordRecoveryRequest(
    Guid ChallengeId,
    string ChallengeCode,
    string NewPassword);