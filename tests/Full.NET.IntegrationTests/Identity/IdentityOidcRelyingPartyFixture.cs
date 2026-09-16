using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using System.Security.Cryptography;

namespace Full.NET.IntegrationTests.Identity;


internal sealed record IdentityOidcTokenExchangeResult(
    HttpStatusCode StatusCode,
    bool IsSuccessStatusCode,
    string RawBody);
internal sealed record IdentityOidcAuthorizationResult(
    string Code,
    string State,
    string? IdToken,
    string AccessToken,
    string? RefreshToken,
    string RawTokenResponse);

internal sealed record IdentityOidcAuthorizationCodePending(
    string Code,
    string Verifier,
    string ClientId,
    string RedirectUri,
    string Nonce);

internal static class IdentityOidcRelyingPartyFixture
{
    public const string PublicClientId = "fixture-oidc-a-public";
    public const string ConfidentialClientId = "fixture-oidc-b-confidential";
    public const string ConfidentialClientSecret = "test-secret-b";
    public const string PublicRedirectUri = "https://localhost:5001/signin-oidc-a";
    public const string ConfidentialRedirectUri = "https://localhost:5002/signin-oidc-b";

    public static async Task<IdentityOidcAuthorizationResult> RunAuthorizationCodeFlowAsync(
        HttpClient client,
        string clientId,
        string redirectUri,
        string? clientSecret,
        string username,
        string password,
        bool requestOfflineAccess,
        string? wrongCodeVerifier = null,
        string? wrongRedirectUri = null,
        string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await BeginAuthorizationCodeFlowAsync(
            client,
            clientId,
            redirectUri,
            username,
            password,
            requestOfflineAccess,
            scopes,
            cancellationToken).ConfigureAwait(false);
        return await ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            clientId,
            redirectUri,
            clientSecret,
            wrongCodeVerifier,
            pending.Nonce,
            wrongRedirectUri,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IdentityOidcAuthorizationCodePending> BeginAuthorizationCodeFlowAsync(
        HttpClient client,
        string clientId,
        string redirectUri,
        string username,
        string password,
        bool requestOfflineAccess,
        string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = CreatePkcePair();
        scopes ??= requestOfflineAccess ? "openid profile offline_access" : "openid profile";
        var authorizeUrl = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&nonce={Uri.EscapeDataString(nonce)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var authorizeGet = await client.GetAsync(authorizeUrl, cancellationToken)
            .ConfigureAwait(false);
        Assert.IsTrue(
            authorizeGet.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect
                or HttpStatusCode.Found or HttpStatusCode.SeeOther,
            $"Authorize GET returned {authorizeGet.StatusCode}.");

        string? code;
        if (authorizeGet.Headers.Location is not null)
        {
            code = ExtractQuery(authorizeGet.Headers.Location, "code");
        }
        else
        {
            var loginPage = await authorizeGet.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            const string tokenPrefix = "name=\"__RequestVerificationToken\" value=\"";
            var tokenStart = loginPage.IndexOf(tokenPrefix, StringComparison.Ordinal);
            Assert.IsTrue(tokenStart >= 0, "中心登录页缺少防伪令牌。");
            tokenStart += tokenPrefix.Length;
            var tokenEnd = loginPage.IndexOf('"', tokenStart);
            var antiforgeryToken = WebUtility.HtmlDecode(loginPage[tokenStart..tokenEnd]);
            // 使用真实防伪服务证明跨站可构造的裸表单不能进入中心凭据处理。
            using (var forgedResponse = await client.PostAsync("/connect/authorize",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId, ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code", ["scope"] = scopes,
                    ["state"] = state, ["nonce"] = nonce,
                    ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
                    ["username"] = username, ["password"] = password,
                }), cancellationToken).ConfigureAwait(false))
            {
                Assert.AreEqual(HttpStatusCode.BadRequest, forgedResponse.StatusCode,
                    "缺少防伪令牌的凭据表单必须拒绝，不能颁发授权码。");
                Assert.IsNull(forgedResponse.Headers.Location);
            }
            using var authorizePost = new HttpRequestMessage(HttpMethod.Post, "/connect/authorize")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code",
                    ["scope"] = scopes,
                    ["state"] = state,
                    ["nonce"] = nonce,
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                    ["username"] = username,
                    ["password"] = password,
                    ["__RequestVerificationToken"] = antiforgeryToken,
                }),
            };
            using var authorizePostResponse = await client.SendAsync(
                    authorizePost,
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.Redirect, authorizePostResponse.StatusCode);
            code = ExtractQuery(authorizePostResponse.Headers.Location!, "code");
            Assert.AreEqual(state, ExtractQuery(authorizePostResponse.Headers.Location!, "state"));
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(code));
        return new IdentityOidcAuthorizationCodePending(
            code!,
            verifier,
            clientId,
            redirectUri,
            nonce);
    }

    public static async Task<IdentityOidcAuthorizationResult> ExchangeAuthorizationCodeAsync(
        HttpClient client,
        string code,
        string verifier,
        string clientId,
        string redirectUri,
        string? clientSecret,
        string? wrongCodeVerifier = null,
        string? expectedNonce = null,
        string? wrongRedirectUri = null,
        CancellationToken cancellationToken = default)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = wrongRedirectUri ?? redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = wrongCodeVerifier ?? verifier,
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            tokenRequest["client_secret"] = clientSecret!;
        }

        using var tokenResponse = await client.PostAsync(
                "/connect/token",
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken)
            .ConfigureAwait(false);
        var raw = await tokenResponse.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            return new IdentityOidcAuthorizationResult(
                code,
                string.Empty,
                null,
                string.Empty,
                null,
                raw);
        }

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        var idToken = root.TryGetProperty("id_token", out var idTokenElement)
            ? idTokenElement.GetString()
            : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString()
            : null;
        if (!string.IsNullOrWhiteSpace(idToken) && !string.IsNullOrWhiteSpace(expectedNonce))
        {
            var payloadNonce = ReadJwtPayloadValue(idToken, "nonce");
            Assert.IsTrue(
                ValidateNonce(expectedNonce, payloadNonce),
                "ID token nonce must match the authorize request.");
        }

        return new IdentityOidcAuthorizationResult(
            code,
            string.Empty,
            idToken,
            accessToken,
            refreshToken,
            raw);
    }

    public static string? ReadJwtPayloadValue(string jwt, string claimName)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(claimName, out var claim)
            ? claim.GetString()
            : null;
    }

    public static string? ReadJwtHeaderValue(string jwt, string propertyName)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 1)
        {
            return null;
        }

        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }

    private static string? ExtractQuery(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }


    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = CreateCodeVerifier();
        return (verifier, CreateCodeChallenge(verifier));
    }

    public static bool ValidateNonce(string expectedNonce, string? idTokenNonce)
        => string.Equals(expectedNonce, idTokenNonce, StringComparison.Ordinal);

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    public static async Task<IdentityOidcTokenExchangeResult> ExchangeRefreshTokenAsync(
        HttpClient client,
        string refreshToken,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            tokenRequest["client_secret"] = clientSecret!;
        }

        using var tokenResponse = await client.PostAsync(
                "/connect/token",
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken)
            .ConfigureAwait(false);
        var raw = await tokenResponse.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        return new IdentityOidcTokenExchangeResult(
            tokenResponse.StatusCode,
            tokenResponse.IsSuccessStatusCode,
            raw);
    }
}
