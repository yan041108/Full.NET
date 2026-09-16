using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchRevokeRaceAssertions
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
        await VerifyConcurrentRevokeAndContextSwitchAsync(factory, cancellationToken);
        await VerifyRevokeAfterTenantContextSwitchAsync(factory, cancellationToken);
    }

    private static async Task VerifyConcurrentRevokeAndContextSwitchAsync(
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
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            "admin",
            cancellationToken: cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(hostClient, adminToken, cancellationToken);
        var sessionId = await ResolveOidcSessionIdAsync(
            hostClient,
            adminToken,
            adminUserId,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            cancellationToken);
        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);

        var revokeTask = hostClient.SendAsync(
            CreateRevokeRequest(sessionId, adminToken),
            cancellationToken);
        var switchTask = hostClient.SendAsync(
            CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken),
            cancellationToken);
        await Task.WhenAll(revokeTask, switchTask);
        using var revokeResponse = await revokeTask;
        using var switchResponse = await switchTask;

        Assert.AreNotEqual(HttpStatusCode.InternalServerError, revokeResponse.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.InternalServerError, switchResponse.StatusCode);

        TenantContextTokenResponse? switched = null;
        if (switchResponse.StatusCode == HttpStatusCode.OK)
        {
            switched = await switchResponse.Content
                .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
            Assert.IsNotNull(switched);
        }

        if (revokeResponse.StatusCode == HttpStatusCode.OK)
        {
            await AssertMeRejectsTokenAsync(
                hostClient,
                flow.AccessToken,
                "host access token after concurrent revoke",
                cancellationToken);
            await AssertRefreshRejectedAsync(hostClient, flow.RefreshToken!, cancellationToken);
            if (switched is not null)
            {
                await AssertMeRejectsTokenAsync(
                    acmeClient,
                    switched.AccessToken,
                    "tenant access token after concurrent revoke",
                    cancellationToken);
            }

            return;
        }

        Assert.AreEqual(HttpStatusCode.OK, switchResponse.StatusCode);
        await AssertMeAcceptsTokenAsync(
            acmeClient,
            switched!.AccessToken,
            "tenant access token when revoke lost the race",
            cancellationToken);
        await AssertMeRejectsTokenAsync(
            hostClient,
            flow.AccessToken,
            "host access token after winning context switch race",
            cancellationToken);
    }

    private static async Task VerifyRevokeAfterTenantContextSwitchAsync(
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
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);
        using var switchRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var switchResponse = await hostClient.SendAsync(switchRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, switchResponse.StatusCode);
        var switched = await switchResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(switched);

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            hostClient,
            "admin",
            cancellationToken: cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(hostClient, adminToken, cancellationToken);
        var sessionId = await ResolveOidcSessionIdAsync(
            hostClient,
            adminToken,
            adminUserId,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            cancellationToken);
        using var revokeResponse = await hostClient.SendAsync(
            CreateRevokeRequest(sessionId, adminToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        await AssertMeRejectsTokenAsync(
            acmeClient,
            switched.AccessToken,
            "tenant access token after revoke following context switch",
            cancellationToken);
        await AssertRefreshRejectedAsync(hostClient, flow.RefreshToken!, cancellationToken);
    }

    private static HttpRequestMessage CreateRevokeRequest(Guid sessionId, string adminToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{sessionId:D}/revoke")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return request;
    }

    private static async Task<Guid> ResolveOidcSessionIdAsync(
        HttpClient client,
        string adminToken,
        Guid userId,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&userId={userId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page.Items
            .Where(item => item.ClientId == clientId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .First()
            .Id;
    }

    private static async Task<Guid> ResolveAdminUserIdAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page.Items.Single(item => item.Username == "admin").Id;
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

    private static async Task AssertRefreshRejectedAsync(
        HttpClient client,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            refreshToken,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
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
            $"Access token must be rejected for {scenario}.");
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