namespace Full.NET.Modules.Identity.OAuth;

/// <summary>OIDC 客户端抽象；单元测试可替换为模拟实现。</summary>
internal interface IOidcClient
{
    /// <summary>从 Authority 拉取 OIDC Discovery 文档。</summary>
    /// <param name="authority">Issuer 根 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Discovery 文档。</returns>
    Task<OidcDiscoveryDocument> GetDiscoveryDocumentAsync(
        string authority,
        CancellationToken cancellationToken = default);

    /// <summary>构建带 PKCE、state 与 nonce 的授权 URL。</summary>
    /// <param name="discovery">Discovery 文档。</param>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="redirectUri">回调绝对 URL。</param>
    /// <param name="scopes">空格分隔的 scope 列表。</param>
    /// <param name="state">OAuth state。</param>
    /// <param name="nonce">OIDC nonce。</param>
    /// <param name="codeChallenge">PKCE code_challenge。</param>
    /// <returns>可重定向的授权 URL。</returns>
    string BuildAuthorizationUrl(
        OidcDiscoveryDocument discovery,
        string clientId,
        string redirectUri,
        string scopes,
        string state,
        string nonce,
        string codeChallenge);

    /// <summary>用授权码交换令牌并校验 ID Token。</summary>
    /// <param name="discovery">Discovery 文档。</param>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="clientSecret">客户端密钥。</param>
    /// <param name="redirectUri">回调绝对 URL。</param>
    /// <param name="code">授权码。</param>
    /// <param name="codeVerifier">PKCE code_verifier。</param>
    /// <param name="expectedNonce">期望的 nonce。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>令牌交换结果与外部身份声明。</returns>
    Task<(OidcTokenExchangeResult Tokens, OidcExternalIdentityClaims Claims)> ExchangeCodeAsync(
        OidcDiscoveryDocument discovery,
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken = default);
}
