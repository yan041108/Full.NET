using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using System.Security.Cryptography;

namespace Full.NET.IntegrationTests.Identity;

internal sealed record IdentityOidcAuthorizationResult(
    string Code,
    string State,
    string? IdToken,
    string AccessToken,
    string? RefreshToken,
    string RawTokenResponse);

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
        CancellationToken cancellationToken = default)
    {
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var scopes = requestOfflineAccess ? "openid profile offline_access" : "openid profile";
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

        string? code = null;
        if (authorizeGet.Headers.Location is not null)
        {
            code = ExtractQuery(authorizeGet.Headers.Location, "code");
        }
        else
        {
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
        var tokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = redirectUri,
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
                code!,
                state,
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
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            var payloadNonce = ReadJwtPayloadValue(idToken, "nonce");
            Assert.IsTrue(
                IdentityOidcRelyingPartyFixture.ValidateNonce(nonce, payloadNonce),
                "ID token nonce must match the authorize request.");
        }

        return new IdentityOidcAuthorizationResult(
            code!,
            state,
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
}