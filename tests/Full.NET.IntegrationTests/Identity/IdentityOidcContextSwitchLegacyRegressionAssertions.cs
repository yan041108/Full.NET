using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchLegacyRegressionAssertions
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
        await VerifyMixedSessionContextSwitchIndependenceAsync(factory, cancellationToken);
    }

    private static async Task VerifyMixedSessionContextSwitchIndependenceAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        var legacyHostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            "admin",
            cancellationToken: cancellationToken);
        var oidcFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            legacyHostToken,
            "legacy host token with parallel OIDC session",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            oidcFlow.AccessToken,
            "OIDC host token with parallel legacy session",
            cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(hostClient, oidcFlow.AccessToken, cancellationToken);
        using var oidcSwitchRequest = CreateContextSwitchRequest(acmeTenant.Id, oidcFlow.AccessToken);
        using var oidcSwitchResponse = await hostClient.SendAsync(oidcSwitchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, oidcSwitchResponse.StatusCode);
        var oidcTenantToken = await oidcSwitchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(oidcTenantToken);

        await AssertMeRejectsTokenAsync(
            hostClient,
            oidcFlow.AccessToken,
            "stale OIDC host token after OIDC switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            legacyHostToken,
            "legacy host token after OIDC-only switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            oidcTenantToken.AccessToken,
            "OIDC tenant token after OIDC switch",
            cancellationToken);

        using var legacySwitchRequest = CreateContextSwitchRequest(acmeTenant.Id, legacyHostToken);
        using var legacySwitchResponse = await hostClient.SendAsync(legacySwitchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, legacySwitchResponse.StatusCode);
        var legacyTenantToken = await legacySwitchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(legacyTenantToken);

        await AssertMeRejectsTokenAsync(
            hostClient,
            legacyHostToken,
            "stale legacy host token after legacy switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            legacyTenantToken.AccessToken,
            "legacy tenant token after legacy switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            oidcTenantToken.AccessToken,
            "OIDC tenant token unchanged after legacy switch",
            cancellationToken);
    }

    private static async Task AssertMeAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"Access token must remain valid for {scenario}.");
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
            $"Stale access token must be rejected for {scenario}.");
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

    private static HttpRequestMessage CreateContextSwitchRequest(Guid tenantId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}