using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcAccountLockoutAssertions
{
    private const int LockoutThreshold = 5;

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync(cancellationToken);
        await VerifyLoginLockoutInvalidatesOidcProtocolGrantsAsync(factory, cancellationToken);
    }

    private static async Task VerifyLoginLockoutInvalidatesOidcProtocolGrantsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-lockout-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        await CreateHostUserAsync(client, adminToken, username, password, cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            password,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        await AssertUserInfoAcceptsTokenAsync(client, flow.AccessToken, cancellationToken);
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            username,
            password,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        await LockUserViaFailedLoginsAsync(client, username, cancellationToken);

        await AssertProtocolRejectsAfterLockoutAsync(client, flow, pending, cancellationToken);
    }

    private static async Task LockUserViaFailedLoginsAsync(
        HttpClient client,
        string username,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < LockoutThreshold; attempt++)
        {
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(username, "wrong-password")),
            };
            loginRequest.Headers.Add("Origin", "http://localhost");
            using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
        }

        using var lockedLoginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        lockedLoginRequest.Headers.Add("Origin", "http://localhost");
        using var lockedLoginResponse = await client.SendAsync(lockedLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, lockedLoginResponse.StatusCode);
    }

    private static async Task AssertProtocolRejectsAfterLockoutAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        IdentityOidcAuthorizationCodePending pending,
        CancellationToken cancellationToken)
    {
        await AssertMeRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertUserInfoRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertRefreshRejectedAsync(client, flow, cancellationToken);
        await AssertAuthorizationCodeExchangeRejectedAsync(client, pending, cancellationToken);
        await AssertAuthorizeWithCenterCookieRejectsAsync(client, cancellationToken);
    }

    private static async Task AssertAuthorizeWithCenterCookieRejectsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = CreatePkcePair();
        var authorizeUrl = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)}"
            + "&response_type=code"
            + "&scope=openid%20profile%20offline_access"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&nonce={Uri.EscapeDataString(nonce)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var response = await client.GetAsync(authorizeUrl, cancellationToken);
        if (response.Headers.Location is Uri location)
        {
            Assert.IsTrue(
                string.IsNullOrWhiteSpace(ExtractQuery(location, "code")),
                "Locked users must not silently receive authorization codes via stale center cookies.");
        }
        else
        {
            Assert.IsTrue(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                $"Authorize must fail closed for locked users, got {(int)response.StatusCode}.");
        }
    }

    private static async Task AssertAuthorizationCodeExchangeRejectedAsync(
        HttpClient client,
        IdentityOidcAuthorizationCodePending pending,
        CancellationToken cancellationToken)
    {
        var exchangeResult = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            null,
            null,
            pending.Nonce,
            null,
            cancellationToken);
        Assert.IsTrue(string.IsNullOrWhiteSpace(exchangeResult.AccessToken));
        Assert.IsTrue(
            exchangeResult.RawTokenResponse.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected pending authorization code exchange to fail after lockout, got: {exchangeResult.RawTokenResponse}");
    }

    private static async Task CreateHostUserAsync(
        HttpClient client,
        string adminToken,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "OIDC lockout test user", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
    }

    private static async Task AssertUserInfoAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateUserInfoRequest(accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertUserInfoRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateUserInfoRequest(accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"UserInfo must reject tokens after account lockout, got {(int)response.StatusCode}.");
    }

    private static async Task AssertMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertRefreshRejectedAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected refresh grant to fail after lockout, got: {refreshResult.RawBody}");
    }

    private static HttpRequestMessage CreateUserInfoRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string bearerToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    private static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(verifierBytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return (verifier, Base64UrlEncode(challengeBytes));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? ExtractQuery(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
        {
            query = uri.AbsoluteUri.Contains('?', StringComparison.Ordinal)
                ? uri.AbsoluteUri[(uri.AbsoluteUri.IndexOf('?', StringComparison.Ordinal) + 1)..]
                : string.Empty;
        }
        else if (query.StartsWith('?'))
        {
            query = query[1..];
        }

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}