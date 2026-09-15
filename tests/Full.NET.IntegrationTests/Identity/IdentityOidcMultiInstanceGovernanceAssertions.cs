using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceGovernanceAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5013/signin-oidc-governance-multi";

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var primaryFactory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings);
        using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
        await primaryFactory.InitializeAsync(cancellationToken);
        await secondaryFactory.InitializeAsync(cancellationToken);

        using var primaryClient = primaryFactory.CreateClientForHost("localhost");
        using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
        await VerifyDisabledClientRejectedOnSecondaryInstanceAsync(
            primaryClient,
            secondaryClient,
            cancellationToken);
    }

    private static async Task VerifyDisabledClientRejectedOnSecondaryInstanceAsync(
        HttpClient primaryClient,
        HttpClient secondaryClient,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            primaryClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var clientId = $"gov-multi-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Governance multi-instance client",
                [ExternalRedirectUri],
                [],
                ["openid", "profile", "offline_access"],
                false,
                true,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await primaryClient.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var authorizeUrl = BuildAuthorizeUrl(clientId);
        using var warmAuthorizeResponse = await secondaryClient.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(
            warmAuthorizeResponse.StatusCode is HttpStatusCode.OK
                or HttpStatusCode.Redirect
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther,
            $"Secondary authorize warm-up returned {warmAuthorizeResponse.StatusCode}.");

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            clientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created!.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await primaryClient.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var authorizeResponse = await secondaryClient.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(authorizeResponse.Headers.Location is not null);
        StringAssert.Contains(
            authorizeResponse.Headers.Location!.ToString(),
            "error=unauthorized_client");

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            secondaryClient,
            flow.RefreshToken!,
            clientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase)
                || refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected disabled client refresh to fail on peer instance, got: {refreshResult.RawBody}");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await secondaryClient.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            meResponse.StatusCode,
            "Disabled clients must fail closed on peer instance resource APIs.");
    }

    private static string BuildAuthorizeUrl(string clientId)
    {
        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        return "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(ExternalRedirectUri)}"
            + "&response_type=code&scope=openid%20profile%20offline_access"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
    }
}
