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

    /// <summary>固定注册的交互式客户端；回调地址必须精确匹配，不允许通配来源。</summary>
    public IdentityOidcClientOptions[] Clients { get; set; } = [];
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

    /// <summary>授权码回调地址白名单；必须完整绝对 URI，禁止通配符。</summary>
    public string[] RedirectUris { get; set; } = [];

    /// <summary>退出后回跳地址白名单；同样要求精确匹配。</summary>
    public string[] PostLogoutRedirectUris { get; set; } = [];
}