using System.Net;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcEndpointErrorBoundaryAssertions
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
        using var client = factory.CreateClientForHost("localhost");

        // UserInfo challenge responses return an empty body in TestHost; token endpoint errors carry JSON payloads.
        await VerifyAuthorizeRejectsUnknownClientAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsInvalidCodeAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsWrongVerifierAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsInvalidClientSecretAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsUnsupportedGrantTypeAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsInvalidRefreshTokenAsync(client, cancellationToken);
    }

    private static async Task VerifyAuthorizeRejectsUnknownClientAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var url = "/connect/authorize"
            + "?client_id=fixture-oidc-unknown-client"
            + $"&redirect_uri={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)}"
            + "&response_type=code&scope=openid%20profile"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var response = await client.GetAsync(url, cancellationToken);
        await AssertAuthorizeErrorResponseAsync(response, "Authorize unknown client_id", cancellationToken);
    }

    private static async Task VerifyTokenEndpointRejectsInvalidCodeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var (verifier, _) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var result = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            "invalid-authorization-code",
            verifier,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            cancellationToken: cancellationToken);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.AccessToken));
        AssertProtocolErrorResponse(result.RawTokenResponse, "Token endpoint invalid authorization code");
    }

    private static async Task VerifyTokenEndpointRejectsWrongVerifierAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var (_, wrongVerifier) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var result = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            null,
            wrongCodeVerifier: wrongVerifier,
            expectedNonce: pending.Nonce,
            cancellationToken: cancellationToken);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.AccessToken));
        AssertProtocolErrorResponse(result.RawTokenResponse, "Token endpoint PKCE verifier mismatch");
    }

    private static async Task VerifyTokenEndpointRejectsInvalidClientSecretAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var result = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            client,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            "wrong-client-secret",
            expectedNonce: pending.Nonce,
            cancellationToken: cancellationToken);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.AccessToken));
        AssertProtocolErrorResponse(result.RawTokenResponse, "Token endpoint invalid client secret");
        Assert.IsTrue(
            result.RawTokenResponse.Contains("invalid_client", StringComparison.OrdinalIgnoreCase)
                || result.RawTokenResponse.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase),
            $"Token endpoint must reject invalid client credentials. Body: {result.RawTokenResponse}");
    }

    private static async Task VerifyTokenEndpointRejectsUnsupportedGrantTypeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
            }),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsFalse(response.IsSuccessStatusCode);
        AssertProtocolErrorResponse(body, "Token endpoint unsupported grant type");
        Assert.IsTrue(
            body.Contains("unsupported_grant_type", StringComparison.OrdinalIgnoreCase),
            $"Token endpoint must reject unsupported grant types. Body: {body}");
    }

    private static async Task VerifyTokenEndpointRejectsInvalidRefreshTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            "invalid-refresh-token",
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(result.IsSuccessStatusCode);
        AssertProtocolErrorResponse(result.RawBody, "Token endpoint invalid refresh token");
    }

    private static async Task AssertAuthorizeErrorResponseAsync(
        HttpResponseMessage response,
        string scenario,
        CancellationToken cancellationToken)
    {
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Redirect
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther,
            $"{scenario} must return an authorize error response, got {response.StatusCode}.");
        if (response.Headers.Location is not null)
        {
            var location = response.Headers.Location.ToString();
            Assert.IsTrue(
                location.Contains("error=", StringComparison.OrdinalIgnoreCase),
                $"{scenario} redirect must include an OAuth error. Location: {location}");
            IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(location, scenario);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            Assert.IsTrue(
                body.Contains("error", StringComparison.OrdinalIgnoreCase),
                $"{scenario} body must include an OAuth error. Body: {body}");
            IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(body, scenario);
        }
    }

    private static void AssertProtocolErrorResponse(string body, string scenario)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(body), $"{scenario} must return an error payload.");
        Assert.IsTrue(
            body.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"{scenario} must return a protocol error response. Body: {body}");
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(body, scenario);
    }
}