using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcProtocolAssertions
{
    public static readonly IReadOnlyDictionary<string, string?> Settings = new Dictionary<string, string?>
    {
        ["Identity:Oidc:Enable"] = "true",
        ["Identity:Oidc:Issuer"] = "https://localhost/identity",
        ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "true",
        ["Identity:Oidc:Clients:0:ClientId"] = IdentityOidcRelyingPartyFixture.PublicClientId,
        ["Identity:Oidc:Clients:0:RedirectUris:0"] = IdentityOidcRelyingPartyFixture.PublicRedirectUri,
        ["Identity:Oidc:Clients:0:Scopes:0"] = "openid",
        ["Identity:Oidc:Clients:0:Scopes:1"] = "profile",
        ["Identity:Oidc:Clients:0:Scopes:2"] = "offline_access",
        ["Identity:Oidc:Clients:0:IsFirstParty"] = "true",
        ["Identity:Oidc:Clients:1:ClientId"] = IdentityOidcRelyingPartyFixture.ConfidentialClientId,
        ["Identity:Oidc:Clients:1:ClientSecret"] = IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
        ["Identity:Oidc:Clients:1:RedirectUris:0"] = IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
        ["Identity:Oidc:Clients:1:Scopes:0"] = "openid",
        ["Identity:Oidc:Clients:1:Scopes:1"] = "profile",
        ["Identity:Oidc:Clients:1:Scopes:2"] = "offline_access",
        ["Identity:Oidc:Clients:1:IsFirstParty"] = "true",
    };

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyAsync(client, cancellationToken);
    }

    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        await VerifyDiscoveryAsync(client, cancellationToken);
        await VerifySuccessfulPublicClientFlowAsync(client, cancellationToken);
        await VerifyWrongVerifierRejectedAsync(client, cancellationToken);
        await VerifyWithoutOfflineAccessAsync(client, cancellationToken);
        await VerifyIdTokenRejectedByResourceApiAsync(client, cancellationToken);
        await VerifyInvalidRedirectUriRejectedAsync(client, cancellationToken);
        await VerifyBusinessApiStillReturnsProblemDetailsAsync(client, cancellationToken);
        await VerifyConfidentialClientFlowAsync(client, cancellationToken);
    }

    private static async Task VerifyDiscoveryAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/.well-known/openid-configuration", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsTrue(document.RootElement.TryGetProperty("issuer", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("jwks_uri", out _));
    }

    private static async Task VerifySuccessfulPublicClientFlowAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.IdToken));
        Assert.AreEqual(
            IdentityOidcPrincipalFactory.TokenUseAccess,
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                result.AccessToken,
                FullNetIdentityClaimTypes.TokenUse));
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                result.AccessToken,
                FullNetIdentityClaimTypes.ApplicationSessionId)));
        Assert.AreEqual(
            "host",
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                result.AccessToken,
                FullNetIdentityClaimTypes.ActorScope));
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        using var userInfoResponse = await client.SendAsync(userInfoRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, userInfoResponse.StatusCode);
    }

    private static async Task VerifyWrongVerifierRejectedAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            wrongCodeVerifier: "invalid-verifier",
            cancellationToken: cancellationToken);
        Assert.IsTrue(result.RawTokenResponse.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.AccessToken));
    }

    private static async Task VerifyWithoutOfflineAccessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.IsNull(result.RefreshToken);
    }

    private static async Task VerifyIdTokenRejectedByResourceApiAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.IdToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.IdToken!);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task VerifyInvalidRedirectUriRejectedAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var url = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)}"
            + "&redirect_uri=https%3A%2F%2Fevil.example%2Fcallback"
            + "&response_type=code&scope=openid%20profile"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var response = await client.GetAsync(url, cancellationToken);
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect,
            $"Unexpected status {response.StatusCode}.");
        if (response.Headers.Location is not null)
        {
            StringAssert.Contains(response.Headers.Location.ToString(), "error");
        }
    }

    private static async Task VerifyBusinessApiStillReturnsProblemDetailsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/v1/me", cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsTrue(body.Contains("type", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("\"error\":\"invalid_token\"", StringComparison.Ordinal));
    }

    private static async Task VerifyConfidentialClientFlowAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var result = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AccessToken));
        var tokenUse = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            result.AccessToken,
            FullNetIdentityClaimTypes.TokenUse);
        Assert.AreEqual(IdentityOidcPrincipalFactory.TokenUseAccess, tokenUse);
    }
}