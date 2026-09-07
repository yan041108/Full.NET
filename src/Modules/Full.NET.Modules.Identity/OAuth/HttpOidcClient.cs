using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.OAuth;

/// <summary>基于 HttpClient 的 OIDC 客户端实现。</summary>
internal sealed partial class HttpOidcClient : IOidcClient
{
    private static readonly OidcJsonContext SerializerContext = new(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    private static readonly HttpClient HttpClient = CreateHttpClient();

    /// <summary>读取发现文档并验证提供程序地址边界。</summary>
    /// <param name="authority">受信任的身份提供程序基地址。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public async Task<OidcDiscoveryDocument> GetDiscoveryDocumentAsync(
        string authority,
        CancellationToken cancellationToken = default)
    {
        var normalizedAuthority = NormalizeAuthority(authority);
        var metadataAddress = $"{normalizedAuthority}/.well-known/openid-configuration";
        using var response = await HttpClient.GetAsync(metadataAddress, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await response.Content
            .ReadFromJsonAsync(SerializerContext.DiscoveryResponse, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC discovery document is empty.");
        if (string.IsNullOrWhiteSpace(document.Issuer)
            || string.IsNullOrWhiteSpace(document.AuthorizationEndpoint)
            || string.IsNullOrWhiteSpace(document.TokenEndpoint)
            || string.IsNullOrWhiteSpace(document.JwksUri))
        {
            throw new InvalidOperationException("OIDC discovery document is incomplete.");
        }

        return new OidcDiscoveryDocument(
            document.Issuer.Trim(),
            document.AuthorizationEndpoint.Trim(),
            document.TokenEndpoint.Trim(),
            document.JwksUri.Trim());
    }

    public string BuildAuthorizationUrl(
        OidcDiscoveryDocument discovery,
        string clientId,
        string redirectUri,
        string scopes,
        string state,
        string nonce,
        string codeChallenge)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = scopes,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return QueryHelpers.AddQueryString(discovery.AuthorizationEndpoint, query!);
    }

    /// <summary>使用 PKCE 授权码交换令牌并验证身份声明与 nonce。</summary>
    /// <param name="discovery">已经验证的发现文档。</param>
    /// <param name="clientId">客户端标识。</param>
    /// <param name="clientSecret">仅用于令牌交换的客户端密钥。</param>
    /// <param name="redirectUri">与授权请求一致的回调地址。</param>
    /// <param name="code">一次性授权码。</param>
    /// <param name="codeVerifier">与授权挑战对应的 PKCE 验证值。</param>
    /// <param name="expectedNonce">原始授权请求保存的 nonce。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public async Task<(OidcTokenExchangeResult Tokens, OidcExternalIdentityClaims Claims)>
        ExchangeCodeAsync(
            OidcDiscoveryDocument discovery,
            string clientId,
            string clientSecret,
            string redirectUri,
            string code,
            string codeVerifier,
            string expectedNonce,
            CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, discovery.TokenEndpoint);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        };
        request.Content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var tokenResponse = await response.Content
            .ReadFromJsonAsync(SerializerContext.TokenResponse, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC token response is empty.");
        if (string.IsNullOrWhiteSpace(tokenResponse.IdToken))
        {
            throw new InvalidOperationException("OIDC token response is missing id_token.");
        }

        var claims = await ValidateIdTokenAsync(
                tokenResponse.IdToken,
                discovery,
                clientId,
                expectedNonce,
                cancellationToken)
            .ConfigureAwait(false);
        return (
            new OidcTokenExchangeResult(tokenResponse.IdToken, tokenResponse.AccessToken),
            claims);
    }

    private static async Task<OidcExternalIdentityClaims> ValidateIdTokenAsync(
        string idToken,
        OidcDiscoveryDocument discovery,
        string clientId,
        string expectedNonce,
        CancellationToken cancellationToken)
    {
        var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{NormalizeAuthority(discovery.Issuer)}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            HttpClient);
        var configuration = await configurationManager
            .GetConfigurationAsync(cancellationToken)
            .ConfigureAwait(false);
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = discovery.Issuer,
            ValidAudience = clientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(idToken, validationParameters)
            .ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            throw new SecurityTokenException(result.Exception?.Message ?? "ID token validation failed.");
        }

        var subject = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? result.ClaimsIdentity.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new SecurityTokenException("ID token is missing sub claim.");
        }

        var nonce = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Nonce)?.Value
            ?? result.ClaimsIdentity.FindFirst("nonce")?.Value;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new SecurityTokenException("ID token nonce mismatch.");
        }

        var email = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? result.ClaimsIdentity.FindFirst("email")?.Value;
        var emailVerifiedRaw = result.ClaimsIdentity.FindFirst("email_verified")?.Value;
        var emailVerified = string.Equals(emailVerifiedRaw, "true", StringComparison.OrdinalIgnoreCase)
            || emailVerifiedRaw == "1";
        var displayName = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Name)?.Value
            ?? result.ClaimsIdentity.FindFirst("name")?.Value
            ?? result.ClaimsIdentity.FindFirst("preferred_username")?.Value;
        return new OidcExternalIdentityClaims(
            subject.Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            emailVerified,
            string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());
    }

    private static string NormalizeAuthority(string authority)
    {
        var trimmed = authority.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("OIDC authority must be an absolute http(s) URL.");
        }

        return trimmed;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        return client;
    }

    private sealed class DiscoveryResponse
    {
        [JsonPropertyName("issuer")]
        public string? Issuer { get; set; }

        [JsonPropertyName("authorization_endpoint")]
        public string? AuthorizationEndpoint { get; set; }

        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; set; }

        [JsonPropertyName("jwks_uri")]
        public string? JwksUri { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
    /// <summary>OIDC 元数据与令牌响应的静态 JSON 闭包。</summary>
    [JsonSerializable(typeof(DiscoveryResponse))]
    [JsonSerializable(typeof(TokenResponse))]
    private partial class OidcJsonContext : JsonSerializerContext;
}
