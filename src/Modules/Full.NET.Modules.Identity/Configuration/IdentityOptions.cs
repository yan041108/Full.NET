namespace Full.NET.Modules.Identity.Configuration;

/// <summary>
/// Identity 模块运行时配置选项。包含 JWT 签发参数、密码策略相关的账户锁定阈值、
/// Refresh Token 会话过期、安全戳刷新凭据以及多密钥 RSA 签名环等配置。
/// 安全边界：Production 必须禁用 AllowDevelopmentEphemeralSigningKey。
/// </summary>
internal sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>JWT Access Token 签发方 (iss)。</summary>
    public string Issuer { get; set; } = "Full.NET";

    /// <summary>JWT Access Token 受众 (aud)。</summary>
    public string Audience { get; set; } = "Full.NET.Api";

    /// <summary>会话与签名认证中的 client_id 标识。</summary>
    public string ClientId { get; set; } = "fullnet-admin";

    /// <summary>JWT Access Token 有效时长（分钟）；到期后需用 Refresh Token 轮换。</summary>
    public int AccessTokenMinutes { get; set; } = 10;

    /// <summary>Refresh Token 与 Refresh Session 的生命周期（天）。</summary>
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>登录失败触发账户锁定的累计阈值；超过后触发 LockoutMinutes 冷却。</summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>达到锁定阈值后账户被拒绝登录的时长（分钟）。</summary>
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>
    /// 是否对 Session Cookie/CSRF Cookie 强制 Secure 标志；非 TLS 开发环境可临时关闭。
    /// </summary>
    public bool RequireSecureCookies { get; set; } = true;

    /// <summary>
    /// 允许开发态自动生成临时 RSA 签名密钥；Production 必须为 false 以确保令牌可跨实例验证。
    /// </summary>
    public bool AllowDevelopmentEphemeralSigningKey { get; set; }

    /// <summary>是否启用 Login/Refresh/Logout 等 Token 端点；纯资源服务可关闭。</summary>
    public bool EnableTokenEndpoints { get; set; } = true;

    /// <summary>
    /// 是否允许远程（非 Migrator Seed）执行超管授予/撤销操作；开启时应同时启用 TOTP 强认证。
    /// </summary>
    public bool EnableRemoteSuperAdministratorManagement { get; set; }

    /// <summary>
    /// 启用 TOTP 强认证 Provider（ADR-0004）。Production 开启远程超管写操作时必须为 true。
    /// </summary>
    public bool EnableTotpStrongReauthentication { get; set; }

    /// <summary>
    /// 当前激活用于签发 JWT 的签名密钥 KeyId；必须在 SigningKeys 中且配有私钥。
    /// </summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>
    /// RSA 签名密钥环字典。KeyId → 密钥选项；激活 Key 用于签发，其余仅用于验签以支持平滑轮转。
    /// </summary>
    public Dictionary<string, IdentitySigningKeyOptions> SigningKeys { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 浏览器管理端 CORS 白名单；包含端口的完整源，必须精确匹配。
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>首次启动时宿主管理员播种凭据（Baseline Profile 使用）。</summary>
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

/// <summary>
/// RSA 签名密钥配置项。同一 KeyId 的公钥可部署于全部实例，私钥仅保留在签发节点。
/// </summary>
internal sealed class IdentitySigningKeyOptions
{
    /// <summary>PEM 格式 RSA 公钥；验证 Access Token 时使用。</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>PEM 格式 RSA 私钥；仅 ActiveKeyId 对应条目需要配置。</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;
}

/// <summary>
/// 首次启动宿主管理员种子选项；仅 Baseline Profile 在无现存超管时生效。
/// </summary>
internal sealed class IdentityBootstrapOptions
{
    /// <summary>宿主管理员登录用户名；仅在账号不存在时生效。</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>宿主管理员初始密码；必须满足 IdentityPasswordPolicy 强密码要求。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>宿主管理员显示名称。</summary>
    public string DisplayName { get; set; } = "系统管理员";
}
