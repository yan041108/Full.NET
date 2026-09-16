using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcCenterLogoutProtocolAssertions
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
        await VerifyCenterLogoutInvalidatesProtocolGrantsAsync(factory, cancellationToken);
    }

    private static async Task VerifyCenterLogoutInvalidatesProtocolGrantsAsync(
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
        await AssertUserInfoAcceptsTokenAsync(client, publicFlow.AccessToken, cancellationToken);
        await AssertUserInfoAcceptsTokenAsync(client, confidentialFlow.AccessToken, cancellationToken);
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);

        using var logoutResponse = await client.SendAsync(
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/logout"),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        await AssertProtocolRejectsAfterCenterLogoutAsync(
            client,
            publicFlow,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        await AssertProtocolRejectsAfterCenterLogoutAsync(
            client,
            confidentialFlow,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        await AssertAuthorizationCodeExchangeRejectedAsync(client, pending, cancellationToken);
        await AssertAuthorizeWithStaleCenterCookieRejectsAsync(client, cancellationToken);
    }

    private static async Task AssertProtocolRejectsAfterCenterLogoutAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        await AssertMeRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertUserInfoRejectsTokenAsync(client, flow.AccessToken, cancellationToken);
        await AssertRefreshRejectedAsync(client, flow, clientId, clientSecret, cancellationToken);
    }

    private static async Task AssertAuthorizeWithStaleCenterCookieRejectsAsync(
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
                "Center logout must clear center cookies so authorize cannot silently issue codes.");
        }
        else
        {
            Assert.IsTrue(
                response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                $"Authorize must fail closed after center logout, got {(int)response.StatusCode}.");
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
            $"Expected pending authorization code exchange to fail after center logout, got: {exchangeResult.RawTokenResponse}");
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
            $"UserInfo must reject tokens after center logout, got {(int)response.StatusCode}.");
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
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            clientId,
            clientSecret,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected refresh grant to fail after center logout, got: {refreshResult.RawBody}");
    }

    private static HttpRequestMessage CreateUserInfoRequest(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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