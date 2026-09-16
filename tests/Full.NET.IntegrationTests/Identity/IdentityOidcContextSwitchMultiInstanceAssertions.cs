using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchMultiInstanceAssertions
{
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
        await VerifyPeerInstanceHonorsContextSwitchAsync(
            primaryFactory,
            secondaryFactory,
            cancellationToken);
    }

    private static async Task VerifyPeerInstanceHonorsContextSwitchAsync(
        FullNetApiFactory primaryFactory,
        FullNetApiFactory secondaryFactory,
        CancellationToken cancellationToken)
    {
        using var primaryClient = primaryFactory.CreateClientForHost("localhost");
        using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(primaryClient, flow.AccessToken, cancellationToken);
        using var switchToTenantRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var switchToTenantResponse = await primaryClient.SendAsync(switchToTenantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchToTenantResponse.StatusCode);
        var tenantToken = await switchToTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(tenantToken);

        await AssertMeAcceptsTokenAsync(
            secondaryClient,
            tenantToken.AccessToken,
            "peer instance after OIDC tenant context switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            secondaryClient,
            flow.AccessToken,
            "peer instance with stale OIDC host token after context switch",
            cancellationToken);

        using var switchToHostRequest = CreateContextSwitchRequest(null, tenantToken.AccessToken);
        using var switchToHostResponse = await primaryClient.SendAsync(switchToHostRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchToHostResponse.StatusCode);
        var hostToken = await switchToHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(hostToken);

        await AssertMeAcceptsTokenAsync(
            secondaryClient,
            hostToken.AccessToken,
            "peer instance after OIDC host round-trip",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            secondaryClient,
            tenantToken.AccessToken,
            "peer instance with stale OIDC tenant token after host round-trip",
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
            $"Access token must remain valid on peer instance for {scenario}.");
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
            $"Stale OIDC access token must be rejected on peer instance for {scenario}.");
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
}