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

internal static class IdentityOidcClientOfflineAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await VerifySingleSessionRevokeWhileOfflineAsync(provider, connectionString, cancellationToken);
        await VerifyRevokeAllWhileOfflineAsync(provider, connectionString, cancellationToken);
    }

    private static async Task VerifySingleSessionRevokeWhileOfflineAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var publisher = new RecordingRealtimePublisher();
        using var factory = CreateFactory(provider, connectionString, publisher);
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
            publisher.SessionRevokedPublishCount >= 1,
            "Server should still emit session-revoked notifications for offline clients.");

        await AssertAuthoritativeRevokeWhileOfflineAsync(client, flow, cancellationToken);
    }

    private static async Task VerifyRevokeAllWhileOfflineAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var publisher = new RecordingRealtimePublisher();
        using var factory = CreateFactory(provider, connectionString, publisher);
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

        using var revokeAllRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/users/{adminUserId:D}/revoke-all")
        {
            Content = JsonContent.Create(new { }),
        };
        revokeAllRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var revokeAllResponse = await client.SendAsync(revokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeAllResponse.StatusCode);
        var payload = await revokeAllResponse.Content
            .ReadFromJsonAsync<RevokeAllHostUserSessionsResponse>(cancellationToken);
        Assert.IsNotNull(payload);
        Assert.IsTrue(
            payload.RevokedSessionCount >= 1,
            "Revoke-all should report at least one revoked session for the OIDC client.");
        Assert.IsTrue(
            publisher.SessionRevokedPublishCount >= 1,
            "Server should still emit session-revoked notifications for offline clients.");

        await AssertAuthoritativeRevokeWhileOfflineAsync(client, flow, cancellationToken);
    }

    private static FullNetApiFactory CreateFactory(
        DatabaseProvider provider,
        string connectionString,
        RecordingRealtimePublisher publisher) =>
        new(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings,
            configureTestServices: services =>
            {
                services.RemoveAll<IRealtimePublisher>();
                services.AddSingleton<IRealtimePublisher>(publisher);
            });

    private static async Task AssertAuthoritativeRevokeWhileOfflineAsync(
        HttpClient client,
        IdentityOidcAuthorizationResult flow,
        CancellationToken cancellationToken)
    {
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            meResponse.StatusCode,
            "Offline clients must fail closed on the next authoritative /me check.");

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(
            refreshResult.IsSuccessStatusCode,
            "Offline clients must not refresh after authoritative revoke.");
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

    private sealed class RecordingRealtimePublisher : IRealtimePublisher
    {
        public int SessionRevokedPublishCount { get; private set; }

        public Task PublishToUserAsync(
            Guid userId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message.Code == RealtimeMessageCodes.SessionRevoked)
            {
                SessionRevokedPublishCount++;
            }

            return Task.CompletedTask;
        }

        public Task PublishToTenantAsync(
            Guid tenantId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishToHostBroadcastAsync(
            RealtimeMessage message,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
