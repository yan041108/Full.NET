using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchBoundaryAssertions
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
        await VerifyOidcContextSwitchRejectedAsync(factory, cancellationToken);
        await VerifyLegacyContextSwitchStillWorksAsync(factory, cancellationToken);
    }

    private static async Task VerifyOidcContextSwitchRejectedAsync(
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
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(client, publicFlow.AccessToken, cancellationToken);
        await AssertContextSwitchRejectedAsync(
            client,
            publicFlow.AccessToken,
            acmeTenant.Id,
            "public OIDC client",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            client,
            publicFlow.AccessToken,
            "public OIDC client after rejected switch",
            cancellationToken);

        await AssertContextSwitchRejectedAsync(
            client,
            confidentialFlow.AccessToken,
            acmeTenant.Id,
            "confidential OIDC client",
            cancellationToken);
        await AssertMeAcceptsTokenAsync(
            client,
            confidentialFlow.AccessToken,
            "confidential OIDC client after rejected switch",
            cancellationToken);
    }

    private static async Task VerifyLegacyContextSwitchStillWorksAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var hostToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            cancellationToken: cancellationToken);
        var acmeTenant = await GetAcmeTenantAsync(client, hostToken, cancellationToken);

        using var switchRequest = CreateContextSwitchRequest(acmeTenant.Id, hostToken);
        using var switchResponse = await client.SendAsync(switchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchResponse.StatusCode);
        var switched = await switchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(switched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(switched.AccessToken));
        Assert.AreEqual(acmeTenant.Id, switched.Context.TenantId);

        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            switched.AccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
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

    private static async Task AssertContextSwitchRejectedAsync(
        HttpClient client,
        string accessToken,
        Guid tenantId,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = CreateContextSwitchRequest(tenantId, accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            response.StatusCode,
            $"OIDC access token must not switch tenant context for {scenario}.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual(
            IdentityErrorCodes.OidcContextSwitchNotSupported,
            problem.RootElement.GetProperty("code").GetString());
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
            body,
            $"OIDC context switch rejection for {scenario}");
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
            $"OIDC access token must remain valid for {scenario}.");
    }

    private static HttpRequestMessage CreateContextSwitchRequest(
        Guid tenantId,
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