namespace Full.NET.Modules.Identity.Configuration;

internal sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public string Issuer { get; set; } = "Full.NET";

    public string Audience { get; set; } = "Full.NET.Api";

    public string ClientId { get; set; } = "fullnet-admin";

    public int AccessTokenMinutes { get; set; } = 10;

    public int RefreshTokenDays { get; set; } = 7;

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public bool RequireSecureCookies { get; set; } = true;

    public bool AllowDevelopmentEphemeralSigningKey { get; set; }

    public bool EnableTokenEndpoints { get; set; } = true;

    public bool EnableRemoteSuperAdministratorManagement { get; set; }

    public string ActiveKeyId { get; set; } = string.Empty;

    public Dictionary<string, IdentitySigningKeyOptions> SigningKeys { get; set; } =
        new(StringComparer.Ordinal);

    public string[] AllowedOrigins { get; set; } = [];

    public IdentityBootstrapOptions Bootstrap { get; set; } = new();

    /// <summary>
    /// 登录端点每分钟允许的请求数；真实栈 E2E 等本地自动化场景可提高以避免误触限流。
    /// </summary>
    public int LoginRateLimitPermitLimitPerMinute { get; set; } = 10;

    /// <summary>
    /// Refresh/Logout 等会话变更端点每分钟允许的请求数。
    /// </summary>
    public int SessionMutationRateLimitPermitLimitPerMinute { get; set; } = 30;
}

internal sealed class IdentitySigningKeyOptions
{
    public string PublicKeyPem { get; set; } = string.Empty;

    public string PrivateKeyPem { get; set; } = string.Empty;
}

internal sealed class IdentityBootstrapOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "系统管理员";
}
