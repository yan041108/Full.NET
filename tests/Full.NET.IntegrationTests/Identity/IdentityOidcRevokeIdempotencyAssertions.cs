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

internal static class IdentityOidcRevokeIdempotencyAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var publisher = new CountingRealtimePublisher();
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
        await VerifyDuplicateSingleRevokeIsIdempotentAsync(client, publisher, cancellationToken);
        await VerifyDuplicateRevokeAllIsIdempotentAsync(factory, publisher, cancellationToken);
        await VerifyDuplicateApplicationLogoutIsIdempotentAsync(client, publisher, cancellationToken);
        await VerifyDuplicateCenterLogoutIsIdempotentAsync(client, publisher, cancellationToken);
    }

    private static async Task<HostUserResponse> CreateHostUserAsync(
        HttpClient client,
        string adminToken,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users")
        {
            Content = JsonContent.Create(new CreateHostUserRequest(
                username,
                "OIDC revoke idempotency test user",
                password)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task VerifyDuplicateSingleRevokeIsIdempotentAsync(
        HttpClient client,
        CountingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
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

        publisher.Reset();
        using var firstRevokeRequest = CreateRevokeRequest(sessionId, adminToken);
        using var firstRevokeResponse = await client.SendAsync(firstRevokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstRevokeResponse.StatusCode);

        using var secondRevokeRequest = CreateRevokeRequest(sessionId, adminToken);
        using var secondRevokeResponse = await client.SendAsync(secondRevokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondRevokeResponse.StatusCode);
        Assert.AreEqual(
            1,
            publisher.PublishAttempts,
            "Duplicate revoke must not re-emit realtime session notifications.");

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

    private static async Task VerifyDuplicateRevokeAllIsIdempotentAsync(
        FullNetApiFactory factory,
        CountingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
        using var adminClient = factory.CreateClientForHost("localhost");
        using var userClient = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            adminClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-revoke-all-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        var testUser = await CreateHostUserAsync(
            adminClient,
            adminToken,
            username,
            password,
            cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            userClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            password,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        publisher.Reset();
        using var firstRevokeAllRequest = CreateRevokeAllRequest(testUser.Id, adminToken);
        using var firstRevokeAllResponse = await adminClient.SendAsync(firstRevokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstRevokeAllResponse.StatusCode);
        var firstPayload = await firstRevokeAllResponse.Content
            .ReadFromJsonAsync<RevokeAllHostUserSessionsResponse>(cancellationToken);
        Assert.IsNotNull(firstPayload);
        Assert.IsTrue(firstPayload.RevokedSessionCount > 0);

        using var secondRevokeAllRequest = CreateRevokeAllRequest(testUser.Id, adminToken);
        using var secondRevokeAllResponse = await adminClient.SendAsync(secondRevokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondRevokeAllResponse.StatusCode);
        var secondPayload = await secondRevokeAllResponse.Content
            .ReadFromJsonAsync<RevokeAllHostUserSessionsResponse>(cancellationToken);
        Assert.IsNotNull(secondPayload);
        Assert.AreEqual(0, secondPayload.RevokedSessionCount);
        Assert.AreEqual(
            firstPayload.RevokedSessionCount,
            publisher.PublishAttempts,
            "Revoke-all must publish one realtime notification per revoked session only once.");
    }

    private static async Task VerifyDuplicateApplicationLogoutIsIdempotentAsync(
        HttpClient client,
        CountingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
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

        publisher.Reset();
        using var firstLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout/application",
            new
            {
                clientId = IdentityOidcRelyingPartyFixture.PublicClientId,
            });
        using var firstLogoutResponse = await client.SendAsync(firstLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, firstLogoutResponse.StatusCode);

        using var secondLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout/application",
            new
            {
                clientId = IdentityOidcRelyingPartyFixture.PublicClientId,
            });
        using var secondLogoutResponse = await client.SendAsync(secondLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, secondLogoutResponse.StatusCode);
        Assert.AreEqual(
            1,
            publisher.PublishAttempts,
            "Duplicate application logout must not re-emit realtime session notifications.");

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
    }

    private static async Task VerifyDuplicateCenterLogoutIsIdempotentAsync(
        HttpClient client,
        CountingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(publicFlow.RefreshToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(confidentialFlow.RefreshToken));

        publisher.Reset();
        using var firstLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout");
        using var firstLogoutResponse = await client.SendAsync(firstLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, firstLogoutResponse.StatusCode);
        Assert.AreEqual(
            2,
            publisher.PublishAttempts,
            "Center logout must publish one realtime notification per active application session.");

        using var secondLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout");
        using var secondLogoutResponse = await client.SendAsync(secondLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, secondLogoutResponse.StatusCode);
        Assert.AreEqual(
            2,
            publisher.PublishAttempts,
            "Duplicate center logout must not re-emit realtime session notifications.");

        var publicRefreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            publicFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(publicRefreshResult.IsSuccessStatusCode);
        var confidentialRefreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            confidentialFlow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken);
        Assert.IsFalse(confidentialRefreshResult.IsSuccessStatusCode);
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

    private static HttpRequestMessage CreateRevokeAllRequest(Guid userId, string adminToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/users/{userId:D}/revoke-all")
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

    private sealed class CountingRealtimePublisher : IRealtimePublisher
    {
        public int PublishAttempts { get; private set; }

        public void Reset() => PublishAttempts = 0;

        public Task PublishToUserAsync(
            Guid userId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message.Code == RealtimeMessageCodes.SessionRevoked)
            {
                PublishAttempts++;
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