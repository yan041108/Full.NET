using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcRevokeRealtimeFaultAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var publisher = new CountingFailingRealtimePublisher();
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings,
            configureTestServices: services =>
            {
                services.RemoveAll<IRealtimePublisher>();
                services.AddSingleton<IRealtimePublisher>(publisher);
            });
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(client, adminToken, cancellationToken);
        var sessionId = await ResolveOidcSessionIdAsync(
            client,
            adminToken,
            adminUserId,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            cancellationToken);

        using var revokeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{sessionId:D}/revoke")
        {
            Content = JsonContent.Create(new { }),
        };
        revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);
        Assert.IsTrue(
            publisher.PublishAttempts >= 3,
            "Expected bounded realtime retry attempts after authoritative revoke.");

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
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
        return page.Items.Single(item => item.ClientId == clientId).Id;
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

    private sealed class CountingFailingRealtimePublisher : IRealtimePublisher
    {
        public int PublishAttempts { get; private set; }

        public Task PublishToUserAsync(
            Guid userId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default)
        {
            PublishAttempts++;
            throw new InvalidOperationException("Simulated realtime outage.");
        }

        public Task PublishToTenantAsync(
            Guid tenantId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Simulated realtime outage."));

        public Task PublishToHostBroadcastAsync(
            RealtimeMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Simulated realtime outage."));
    }
}