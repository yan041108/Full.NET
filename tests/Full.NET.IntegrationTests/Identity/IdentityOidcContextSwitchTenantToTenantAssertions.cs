using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchTenantToTenantAssertions
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
        await VerifyTenantToTenantSwitchAsync(factory, cancellationToken);
    }

    private static async Task VerifyTenantToTenantSwitchAsync(
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
        var acmeTenant = await GetTenantByIdentifierAsync(hostClient, flow.AccessToken, "acme", cancellationToken);

        using var enterAcmeRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var enterAcmeResponse = await hostClient.SendAsync(enterAcmeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterAcmeResponse.StatusCode);
        var acmeToken = await enterAcmeResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(acmeToken);

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var secondTenant = await CreateTenantAsync(hostClient, adminToken, cancellationToken);

        using var switchTenantRequest = CreateContextSwitchRequest(secondTenant.Id, acmeToken.AccessToken);
        using var switchTenantResponse = await hostClient.SendAsync(switchTenantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchTenantResponse.StatusCode);
        var secondTenantToken = await switchTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(secondTenantToken);
        Assert.AreEqual(secondTenant.Id, secondTenantToken.Context.TenantId);

        using var secondTenantClient = factory.CreateClientForHost($"{secondTenant.Identifier}.localhost");
        await AssertMeAcceptsTokenAsync(
            secondTenantClient,
            secondTenantToken.AccessToken,
            "OIDC token after tenant-to-tenant switch",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            acmeClient,
            acmeToken.AccessToken,
            "stale OIDC acme token after tenant-to-tenant switch",
            cancellationToken);
        await AssertUserInfoRejectsTokenAsync(hostClient, flow.AccessToken, cancellationToken);
        await AssertUserInfoAcceptsTokenAsync(secondTenantClient, secondTenantToken.AccessToken, cancellationToken);
    }

    private static async Task<TenantContextSummary> CreateTenantAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var identifier = $"oidc-sw-{Guid.NewGuid():N}"[..12];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenancy/tenants")
        {
            Content = JsonContent.Create(new ProvisionTenantRequest(
                identifier,
                $"OIDC Switch {identifier}",
                $"{identifier}.localhost")),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(created);
        return new TenantContextSummary(created.Id, created.Identifier, created.Name, created.Domain);
    }

    private static async Task<TenantContextSummary> GetTenantByIdentifierAsync(
        HttpClient client,
        string accessToken,
        string identifier,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var available = await response.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        return available.Single(tenant => tenant.Identifier == identifier);
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
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, scenario);
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
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, scenario);
    }

    private static async Task AssertUserInfoAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertUserInfoRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"UserInfo must reject stale OIDC token, got {(int)response.StatusCode}.");
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