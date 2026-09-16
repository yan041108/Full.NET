using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcApplicationRevokeCenterSsoAssertions
{
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
        await VerifySingleSessionRevokePreservesCenterSsoForOtherAppsAsync(factory, cancellationToken);
        await VerifyApplicationLogoutPreservesCenterSsoForOtherAppsAsync(factory, cancellationToken);
    }

    private static async Task VerifyApplicationLogoutPreservesCenterSsoForOtherAppsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        using var logoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout/application",
            new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId });
        using var logoutResponse = await client.SendAsync(logoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        await AssertRevokedApplicationTokensRejectedAsync(client, publicFlow, cancellationToken);
        await AssertCenterCookieAuthorizeAndExchangeAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        await AssertRefreshStillValidAsync(
            client,
            confidentialFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        await AssertCenterCookieAuthorizeAndExchangeAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            cancellationToken);
    }

    private static async Task VerifySingleSessionRevokePreservesCenterSsoForOtherAppsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(client, adminToken, cancellationToken);
        var publicSessionId = await ResolveOidcSessionIdAsync(
            client,
            adminToken,
            adminUserId,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            cancellationToken);

        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{publicSessionId:D}/revoke")
        {
            Content = JsonContent.Create(new { }),
        };
        revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        await AssertRevokedApplicationTokensRejectedAsync(client, publicFlow, cancellationToken);
        await AssertCenterCookieAuthorizeAndExchangeAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        await AssertRefreshStillValidAsync(
            client,
            confidentialFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
    }

    private static async Task AssertCenterCookieAuthorizeAndExchangeAsync(
        HttpClient client,
        string clientId,
        string redirectUri,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        var pending = await BeginCenterCookieAuthorizeAsync(
            client,
            clientId,
            redirectUri,
            cancellationToken);
        var exchangeResult = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            clientSecret,
            null,
            pending.Nonce,
            null,
            cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(exchangeResult.AccessToken));
        await AssertUserInfoAcceptsTokenAsync(client, exchangeResult.AccessToken, cancellationToken);
    }

    private static async Task AssertRefreshStillValidAsync(
        HttpClient client,
        string refreshToken,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            refreshToken,
            clientId,
            clientSecret,
            cancellationToken);
        Assert.IsTrue(
            refreshResult.IsSuccessStatusCode,
            $"Unrelated application sessions must remain refreshable while center SSO stays active, got: {refreshResult.RawBody}");
    }

    private static async Task<IdentityOidcAuthorizationCodePending> BeginCenterCookieAuthorizeAsync(
        HttpClient client,
        string clientId,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        var (verifier, challenge) = CreatePkcePair();
        var authorizeUrl = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + "&scope=openid%20profile%20offline_access"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&nonce={Uri.EscapeDataString(nonce)}"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var authorizeResponse = await client.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(
            authorizeResponse.Headers.Location is not null,
            "Center SSO must issue authorization codes without showing the login page.");
        var code = ExtractQuery(authorizeResponse.Headers.Location!, "code");
        Assert.IsFalse(string.IsNullOrWhiteSpace(code));
        Assert.AreEqual(state, ExtractQuery(authorizeResponse.Headers.Location!, "state"));
        return new IdentityOidcAuthorizationCodePending(
            code!,
            verifier,
            clientId,
            redirectUri,
            nonce);
    }

    private static async Task AssertRevokedApplicationTokensRejectedAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        CancellationToken cancellationToken)
    {
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
    }

    private static async Task AssertUserInfoAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> ResolveOidcSessionIdAsync(
        HttpClient client,
        string adminToken,
        Guid userId,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&userId={userId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page.Items
            .Where(item => item.ClientId == clientId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .First()
            .Id;
    }

    private static async Task<Guid> ResolveAdminUserIdAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page.Items.Single(item => item.Username == "admin").Id;
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