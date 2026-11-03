namespace Full.NET.Modules.Identity.Configuration;

/// <summary>
/// OIDC 认证中心运行时配置。默认未启用；显式开启后才装配协议处理器与端点。
/// 安全边界：Production 启用时必须配置明确 Issuer 与持久化签名密钥。
/// </summary>
internal sealed class IdentityOidcOptions
{
    public const string SectionName = "Identity:Oidc";

    /// <summary>是否启用 OIDC 认证中心协议能力；默认关闭，不影响现有登录入口。</summary>
    public bool Enable { get; set; }

    /// <summary>每个可信来源地址的协议入口每分钟预算；在协议校验和拒绝审计前执行，默认 120。</summary>
    public int ProtocolRateLimitPermitLimitPerMinute { get; set; } = 120;

    /// <summary>OIDC Issuer（iss）；Discovery 与令牌校验的权威标识。</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// 允许开发态自动生成临时 RSA 签名密钥；Production 必须为 false 以确保跨实例 JWKS 一致。
    /// </summary>
    public bool AllowDevelopmentEphemeralSigningKey { get; set; }

    /// <summary>当前激活用于签发 OIDC 令牌的签名密钥 KeyId。</summary>
    public string ActiveSigningKeyId { get; set; } = string.Empty;

    /// <summary>OIDC 签名密钥环；激活 Key 用于签发，其余仅用于验签以支持平滑轮转。</summary>
    public Dictionary<string, IdentityOidcSigningKeyOptions> SigningKeys { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Base64 编码的 256 位对称密钥，用于 OpenIddict 刷新令牌等不透明令牌加密。
    /// 多实例部署必须共享同一密钥，否则跨节点 refresh 会返回 invalid_grant。
    /// </summary>
    public string EncryptionKeyBase64 { get; set; } = string.Empty;

    /// <summary>Migrator 首次初始化的客户端；已有数据库记录由管理 API 维护，启动不覆盖。</summary>
    public IdentityOidcClientOptions[] Clients { get; set; } = [];
    /// <summary>部署方精确授权的 HTTPS 退出通知来源；默认无外发目的地，可显式登记内网 RP。</summary>
    public string[] BackchannelAllowedOrigins { get; set; } = [];
    /// <summary>激活候选密钥所需的最少 API 就绪租约数；部署应对应最小 API 副本数。</summary>
    public int MinimumReadyApiNodes { get; set; } = 1;
    /// <summary>公钥登记到可激活之间的预发布窗口；生产至少一分钟，默认五分钟。</summary>
    public int SigningPrepublicationSeconds { get; set; } = 300;
}

/// <summary>OIDC 签名密钥配置项；职责与 Identity JWT 签名环分离，便于协议密钥独立轮转。</summary>
internal sealed class IdentityOidcSigningKeyOptions
{
    /// <summary>PEM 格式 RSA 公钥；JWKS 暴露与验签使用。</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>PEM 格式 RSA 私钥；仅 ActiveSigningKeyId 对应条目需要配置。</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;
}

/// <summary>固定 OIDC 客户端注册项；P0 仅支持显式 client_id 与精确 redirect_uri。</summary>
internal sealed class IdentityOidcClientOptions
{
    /// <summary>OAuth/OIDC client_id；全局唯一且稳定。</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>机密客户端密钥；公开客户端留空。</summary>
    public string? ClientSecret { get; set; }

    /// <summary>授权码回调地址白名单；必须完整绝对 URI，禁止通配符。</summary>
    public string[] RedirectUris { get; set; } = [];

    /// <summary>退出后回跳地址白名单；同样要求精确匹配。</summary>
    public string[] PostLogoutRedirectUris { get; set; } = [];

    /// <summary>默认请求 scope；未配置时使用 openid 与 profile。</summary>
    public string[] Scopes { get; set; } = ["openid", "profile"];

    /// <summary>内部 Full.NET 客户端可在令牌中包含安全戳；外部客户端必须为 false。</summary>
    public bool IsFirstParty { get; set; }
    /// <summary>该客户端必须使用已验证的第二因子登录；密码 Cookie 不满足此要求。</summary>
    public bool RequireMfa { get; set; }
    /// <summary>精确的标准退出接收端；必须属于部署允许的来源。</summary>
    public string? BackchannelLogoutUri { get; set; }

    /// <summary>资源 API aud 覆盖；默认使用 <see cref="IdentityOptions.Audience"/>。</summary>
    public string ResourceAudience { get; set; } = string.Empty;
}
