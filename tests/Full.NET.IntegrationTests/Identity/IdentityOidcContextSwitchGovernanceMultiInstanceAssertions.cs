using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchGovernanceMultiInstanceAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5013/signin-oidc-context-switch-governance";

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
        await VerifyDisabledClientRejectsContextSwitchedTokensOnPeerAsync(
            primaryClient,
            secondaryClient,
            cancellationToken);
    }

    private static async Task VerifyDisabledClientRejectsContextSwitchedTokensOnPeerAsync(
        HttpClient primaryClient,
        HttpClient secondaryClient,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            primaryClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var clientId = $"ctx-gov-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Context switch governance client",
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

        var acmeTenant = await GetAcmeTenantAsync(primaryClient, flow.AccessToken, cancellationToken);
        using var switchToTenantRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var switchToTenantResponse = await primaryClient.SendAsync(switchToTenantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchToTenantResponse.StatusCode);
        var tenantToken = await switchToTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(tenantToken);

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created!.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await primaryClient.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        await AssertDisabledClientRejectsTokensOnInstanceAsync(
            secondaryClient,
            authorizeUrl,
            flow,
            tenantToken.AccessToken,
            clientId,
            "peer",
            cancellationToken);
        await AssertDisabledClientRejectsTokensOnInstanceAsync(
            primaryClient,
            authorizeUrl,
            flow,
            tenantToken.AccessToken,
            clientId,
            "primary",
            cancellationToken);
    }

    private static async Task AssertDisabledClientRejectsTokensOnInstanceAsync(
        HttpClient client,
        string authorizeUrl,
        IdentityOidcAuthorizationResult flow,
        string tenantAccessToken,
        string clientId,
        string instanceLabel,
        CancellationToken cancellationToken)
    {
        using var authorizeResponse = await client.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(authorizeResponse.Headers.Location is not null);
        StringAssert.Contains(
            authorizeResponse.Headers.Location!.ToString(),
            "error=unauthorized_client");

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            clientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase)
                || refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected disabled client refresh to fail on {instanceLabel} instance, got: {refreshResult.RawBody}");

        await AssertMeRejectsTokenAsync(
            client,
            flow.AccessToken,
            $"stale OIDC host token after disable on {instanceLabel} instance",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            client,
            tenantAccessToken,
            $"OIDC tenant token after disable on {instanceLabel} instance",
            cancellationToken);
    }

    private static async Task AssertMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            $"Disabled clients must fail closed for {scenario}.");
    }

    private static async Task<TenantContextSummary> GetAcmeTenantAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var available = await response.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        return available.Single(tenant => tenant.Identifier == "acme");
    }

    private static HttpRequestMessage CreateContextSwitchRequest(Guid? tenantId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
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