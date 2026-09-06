namespace Full.NET.Modules.Identity.OAuth;

/// <summary>OIDC Discovery 文档最小子集。</summary>
/// <param name="Issuer">Issuer 标识。</param>
/// <param name="AuthorizationEndpoint">授权端点 URL。</param>
/// <param name="TokenEndpoint">令牌端点 URL。</param>
/// <param name="JwksUri">JWKS 端点 URL。</param>
internal sealed record OidcDiscoveryDocument(
    string Issuer,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string JwksUri);

/// <summary>OIDC 授权码交换结果。</summary>
/// <param name="IdToken">已签名的 ID Token。</param>
/// <param name="AccessToken">可选的 Access Token。</param>
internal sealed record OidcTokenExchangeResult(
    string IdToken,
    string? AccessToken);

/// <summary>从 ID Token 提取的外部身份声明。</summary>
/// <param name="Subject">外部主体标识（sub）。</param>
/// <param name="Email">邮箱声明。</param>
/// <param name="EmailVerified">邮箱是否已验证。</param>
/// <param name="DisplayName">显示名称。</param>
internal sealed record OidcExternalIdentityClaims(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? DisplayName);
