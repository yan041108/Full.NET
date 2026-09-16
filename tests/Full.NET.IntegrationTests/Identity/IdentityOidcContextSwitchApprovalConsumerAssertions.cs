using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchApprovalConsumerAssertions
{
    private static readonly Guid MissingApprovalId = Guid.Parse("01981f2a-1200-7000-8000-000000000099");

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
        await VerifyApprovalConsumerPathTracksContextSwitchAsync(factory, cancellationToken);
    }

    private static async Task VerifyApprovalConsumerPathTracksContextSwitchAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        await AssertApprovalReadReachesServiceAsync(
            hostClient,
            flow.AccessToken,
            "OIDC host token before context switch",
            cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);
        using var enterTenantRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var enterTenantResponse = await hostClient.SendAsync(enterTenantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterTenantResponse.StatusCode);
        var tenantToken = await enterTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(tenantToken);

        await AssertMeAcceptsTokenAsync(
            hostClient,
            tenantToken.AccessToken,
            "OIDC tenant token after context switch",
            cancellationToken);
        await AssertApprovalReadRequiresHostScopeAsync(
            hostClient,
            tenantToken.AccessToken,
            "OIDC tenant token on host-only approval API",
            cancellationToken);
        await AssertApprovalReadRejectsTokenAsync(
            hostClient,
            flow.AccessToken,
            "stale OIDC host token after context switch",
            cancellationToken);

        using var returnHostRequest = CreateContextSwitchRequest(null, tenantToken.AccessToken);
        using var returnHostResponse = await hostClient.SendAsync(returnHostRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, returnHostResponse.StatusCode);
        var hostToken = await returnHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(hostToken);

        await AssertApprovalReadReachesServiceAsync(
            hostClient,
            hostToken.AccessToken,
            "OIDC host token after tenant round-trip",
            cancellationToken);
        await AssertApprovalReadRejectsTokenAsync(
            hostClient,
            tenantToken.AccessToken,
            "stale OIDC tenant token after host round-trip",
            cancellationToken);
    }

    private static async Task AssertApprovalReadReachesServiceAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/ai/agent/approvals/{MissingApprovalId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.NotFound,
            response.StatusCode,
            $"Host-scoped approval read must authenticate and return not-found for {scenario}.");
    }

    private static async Task AssertApprovalReadRequiresHostScopeAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/ai/agent/approvals/{MissingApprovalId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            response.StatusCode,
            $"Tenant-scoped OIDC token must fail closed on host-only approval API for {scenario}.");
    }

    private static async Task AssertApprovalReadRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/ai/agent/approvals/{MissingApprovalId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            $"Stale OIDC access token must be rejected for {scenario}.");
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