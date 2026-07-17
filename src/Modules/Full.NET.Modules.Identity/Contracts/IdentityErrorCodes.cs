namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// 定义 Identity 模块对外返回的稳定错误码。
/// </summary>
public static class IdentityErrorCodes
{
    /// <summary>
    /// Identity 错误码前缀。
    /// </summary>
    public const string Prefix = "identity.";

    /// <summary>初始化管理员密码不符合安全策略。</summary>
    public const string BootstrapInvalidPassword = "identity.bootstrap.invalid-password";

    /// <summary>初始化管理员资料无效。</summary>
    public const string BootstrapInvalidProfile = "identity.bootstrap.invalid-profile";

    /// <summary>会话请求的 CSRF 校验失败。</summary>
    public const string CsrfValidationFailed = "identity.csrf_validation_failed";

    /// <summary>当前身份的参与者范围不允许切换上下文。</summary>
    public const string InvalidActorScope = "identity.invalid_actor_scope";

    /// <summary>登录凭据无效。</summary>
    public const string InvalidCredentials = "identity.invalid_credentials";

    /// <summary>刷新令牌无效或已过期。</summary>
    public const string InvalidRefreshToken = "identity.invalid_refresh_token";

    /// <summary>浏览器请求来源不在允许列表中。</summary>
    public const string OriginNotAllowed = "identity.origin_not_allowed";

    /// <summary>检测到刷新令牌重复使用并撤销会话族。</summary>
    public const string RefreshTokenReuseDetected = "identity.refresh_token_reuse_detected";

    /// <summary>会话上下文发生并发冲突。</summary>
    public const string SessionContextConflict = "identity.session_context_conflict";

    /// <summary>当前会话已失效。</summary>
    public const string SessionNotActive = "identity.session_not_active";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        BootstrapInvalidPassword,
        BootstrapInvalidProfile,
        CsrfValidationFailed,
        InvalidActorScope,
        InvalidCredentials,
        InvalidRefreshToken,
        OriginNotAllowed,
        RefreshTokenReuseDetected,
        SessionContextConflict,
        SessionNotActive,
    ]);
}
