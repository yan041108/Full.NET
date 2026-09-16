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
        await VerifyTokenEndpointRejectsInvalidCodeAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsWrongVerifierAsync(client, cancellationToken);
        await VerifyTokenEndpointRejectsInvalidRefreshTokenAsync(client, cancellationToken);
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

    private static void AssertProtocolErrorResponse(string body, string scenario)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(body), $"{scenario} must return an error payload.");
        Assert.IsTrue(
            body.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"{scenario} must return a protocol error response. Body: {body}");
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(body, scenario);
    }
}