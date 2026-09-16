using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchApplicationIsolationAssertions
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
        await VerifyOtherApplicationSessionUnchangedAfterSwitchAsync(factory, cancellationToken);
        await VerifyTenantRoundTripRestoresHostTokenAsync(factory, cancellationToken);
    }

    private static async Task VerifyOtherApplicationSessionUnchangedAfterSwitchAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        using var acmeClient = factory.CreateClientForHost("acme.localhost");
        var appAFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var appBFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            appBFlow.AccessToken,
            "application B host token before A switches tenant",
            cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(hostClient, appAFlow.AccessToken, cancellationToken);
        using var switchRequest = CreateContextSwitchRequest(acmeTenant.Id, appAFlow.AccessToken);
        using var switchResponse = await hostClient.SendAsync(switchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchResponse.StatusCode);
        var switched = await switchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(switched);
        Assert.AreEqual(acmeTenant.Id, switched.Context.TenantId);

        await AssertMeAcceptsTokenAsync(
            acmeClient,
            switched.AccessToken,
            "application A tenant token after switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            hostClient,
            appAFlow.AccessToken,
            "application A stale host token after switch",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            hostClient,
            appBFlow.AccessToken,
            "application B host token after A switched tenant",
            cancellationToken);
    }

    private static async Task VerifyTenantRoundTripRestoresHostTokenAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        using var acmeClient = factory.CreateClientForHost("acme.localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);

        using var enterTenantRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var enterTenantResponse = await hostClient.SendAsync(enterTenantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterTenantResponse.StatusCode);
        var tenantToken = await enterTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(tenantToken);
        Assert.AreEqual(acmeTenant.Id, tenantToken.Context.TenantId);

        using var returnHostRequest = CreateContextSwitchRequest(null, tenantToken.AccessToken);
        using var returnHostResponse = await hostClient.SendAsync(returnHostRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, returnHostResponse.StatusCode);
        var hostToken = await returnHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(hostToken);
        Assert.IsNull(hostToken.Context.TenantId);
        Assert.AreEqual("host", hostToken.Context.Scope);

        await AssertMeAcceptsTokenAsync(
            hostClient,
            hostToken.AccessToken,
            "OIDC token after tenant round-trip to host",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            acmeClient,
            tenantToken.AccessToken,
            "OIDC tenant token after returning to host",
            cancellationToken);
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
            $"Stale OIDC access token must be rejected for {scenario}.");
    }

    private static HttpRequestMessage CreateContextSwitchRequest(
        Guid? tenantId,
        string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}