namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OAuth 提供程序管理权限码。</summary>
public static class IdentityOAuthProviderPermissions
{
    /// <summary>分页查询 OAuth 提供程序。</summary>
    public const string Read = "identity.oauth_providers.read";

    /// <summary>创建 OAuth 提供程序。</summary>
    public const string Create = "identity.oauth_providers.create";

    /// <summary>更新 OAuth 提供程序。</summary>
    public const string Update = "identity.oauth_providers.update";

    /// <summary>删除 OAuth 提供程序。</summary>
    public const string Delete = "identity.oauth_providers.delete";
}

/// <summary>OAuth 授权模式。</summary>
public static class OAuthAuthorizationModes
{
    /// <summary>匿名登录；仅当外部主体已绑定本地用户时签发会话。</summary>
    public const string Login = "login";

    /// <summary>已认证用户绑定外部身份。</summary>
    public const string Bind = "bind";
}

/// <summary>OAuth 回调稳定错误码（查询参数 oauth_error）。</summary>
public static class OAuthCallbackErrorCodes
{
    /// <summary>外部主体尚未绑定本地用户。</summary>
    public const string AccountNotLinked = "oauth_account_not_linked";

    /// <summary>外部主体已绑定其他用户。</summary>
    public const string AccountConflict = "oauth_account_conflict";

    /// <summary>授权状态无效或已过期。</summary>
    public const string InvalidState = "oauth_invalid_state";

    /// <summary>IdP 令牌交换或校验失败。</summary>
    public const string TokenExchangeFailed = "oauth_token_exchange_failed";

    /// <summary>提供程序不存在或未启用。</summary>
    public const string ProviderUnavailable = "oauth_provider_unavailable";
}

/// <summary>OAuth 提供程序响应；不包含客户端密钥。</summary>
/// <param name="Id">提供程序稳定标识。</param>
/// <param name="ProviderKey">稳定机器码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Authority">OIDC Issuer URL。</param>
/// <param name="ClientId">客户端标识。</param>
/// <param name="Scopes">授权范围。</param>
/// <param name="RedirectPath">回调路径。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record OAuthProviderResponse(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    string Authority,
    string ClientId,
    string Scopes,
    string RedirectPath,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>公开可见的已启用 OAuth 提供程序摘要。</summary>
/// <param name="ProviderKey">稳定机器码。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record PublicOAuthProviderResponse(
    string ProviderKey,
    string DisplayName);

/// <summary>创建 OAuth 提供程序请求。</summary>
/// <param name="ProviderKey">稳定机器码。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Authority">OIDC Issuer URL。</param>
/// <param name="ClientId">客户端标识。</param>
/// <param name="ClientSecret">客户端密钥；仅写入时接受，响应不回显。</param>
/// <param name="Scopes">授权范围；为空时使用默认 openid profile email。</param>
/// <param name="RedirectPath">回调路径；为空时使用默认 /api/v1/identity/oauth/callback。</param>
/// <param name="IsEnabled">是否启用。</param>
public sealed record CreateOAuthProviderRequest(
    string ProviderKey,
    string DisplayName,
    string Authority,
    string ClientId,
    string ClientSecret,
    string? Scopes,
    string? RedirectPath,
    bool IsEnabled);

/// <summary>更新 OAuth 提供程序请求。</summary>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Authority">OIDC Issuer URL。</param>
/// <param name="ClientId">客户端标识。</param>
/// <param name="ClientSecret">可选的新客户端密钥；为空表示保留现有密钥。</param>
/// <param name="Scopes">授权范围。</param>
/// <param name="RedirectPath">回调路径。</param>
/// <param name="IsEnabled">是否启用。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateOAuthProviderRequest(
    string DisplayName,
    string Authority,
    string ClientId,
    string? ClientSecret,
    string Scopes,
    string RedirectPath,
    bool IsEnabled,
    int Version);

/// <summary>当前用户的 OAuth 外部身份绑定。</summary>
/// <param name="Id">绑定标识。</param>
/// <param name="ProviderKey">提供程序机器码。</param>
/// <param name="ProviderDisplayName">提供程序显示名称。</param>
/// <param name="Subject">外部主体标识。</param>
/// <param name="Email">外部邮箱元数据。</param>
/// <param name="EmailVerified">邮箱是否已验证。</param>
/// <param name="DisplayName">外部显示名称。</param>
/// <param name="LinkedAtUtc">绑定时间（UTC）。</param>
/// <param name="LastUsedAtUtc">最近使用时间（UTC）。</param>
public sealed record OAuthUserLinkResponse(
    Guid Id,
    string ProviderKey,
    string ProviderDisplayName,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName,
    DateTimeOffset LinkedAtUtc,
    DateTimeOffset? LastUsedAtUtc);
