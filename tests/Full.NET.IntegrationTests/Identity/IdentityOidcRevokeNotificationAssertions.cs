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

internal static class IdentityOidcRevokeNotificationAssertions
{
    private static readonly string[] ForbiddenNotificationDataKeys =
    [
        "accessToken",
        "refreshToken",
        "idToken",
        "securityStamp",
        "password",
        "clientSecret",
        "privateKey",
    ];

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var publisher = new RecordingRealtimePublisher();
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
        await VerifySingleRevokeNotificationAsync(client, publisher, cancellationToken);
        await VerifyIdempotentRevokeNotificationAsync(client, publisher, cancellationToken);
        await VerifyRevokeAllNotificationsAsync(factory, publisher, cancellationToken);
    }

    private static async Task VerifySingleRevokeNotificationAsync(
        HttpClient client,
        RecordingRealtimePublisher publisher,
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
        using var revokeRequest = CreateRevokeRequest(sessionId, adminToken);
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        var notification = publisher.SessionRevokedNotifications.Single();
        Assert.AreEqual(adminUserId, notification.UserId);
        AssertSessionRevokedNotification(notification.Message, sessionId, "Single session revoke");
    }

    private static async Task VerifyIdempotentRevokeNotificationAsync(
        HttpClient client,
        RecordingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
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
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            cancellationToken);

        publisher.Reset();
        using var firstRevokeRequest = CreateRevokeRequest(sessionId, adminToken);
        using var firstRevokeResponse = await client.SendAsync(firstRevokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstRevokeResponse.StatusCode);
        Assert.AreEqual(1, publisher.SessionRevokedNotifications.Count);

        publisher.Reset();
        using var secondRevokeRequest = CreateRevokeRequest(sessionId, adminToken);
        using var secondRevokeResponse = await client.SendAsync(secondRevokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondRevokeResponse.StatusCode);
        Assert.AreEqual(
            0,
            publisher.SessionRevokedNotifications.Count,
            "Idempotent revoke must not republish session notifications.");
    }

    private static async Task VerifyRevokeAllNotificationsAsync(
        FullNetApiFactory factory,
        RecordingRealtimePublisher publisher,
        CancellationToken cancellationToken)
    {
        using var adminClient = factory.CreateClientForHost("localhost");
        using var userClient = factory.CreateClientForHost("localhost");
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            adminClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-revoke-notify-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        var testUser = await CreateHostUserAsync(
            adminClient,
            adminToken,
            username,
            password,
            cancellationToken);
        using var passwordClient = factory.CreateClientForHost("localhost");
        await IntegrationTestAuthHelper.ClearInitialPasswordChangeRequirementAsync(
            passwordClient,
            username,
            password,
            cancellationToken);
        var oidcPassword = IntegrationTestAuthHelper.ClearedPassword;
        var publicFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            userClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            oidcPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        var confidentialFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            userClient,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            username,
            oidcPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(publicFlow.RefreshToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(confidentialFlow.RefreshToken));

        var publicSessionId = await ResolveOidcSessionIdAsync(
            adminClient,
            adminToken,
            testUser.Id,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            cancellationToken);
        var confidentialSessionId = await ResolveOidcSessionIdAsync(
            adminClient,
            adminToken,
            testUser.Id,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            cancellationToken);

        publisher.Reset();
        using var revokeAllRequest = CreateRevokeAllRequest(testUser.Id, adminToken);
        using var revokeAllResponse = await adminClient.SendAsync(revokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeAllResponse.StatusCode);
        var payload = await revokeAllResponse.Content
            .ReadFromJsonAsync<RevokeAllHostUserSessionsResponse>(cancellationToken);
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload.RevokedSessionCount >= 2);

        Assert.AreEqual(
            payload.RevokedSessionCount,
            publisher.SessionRevokedNotifications.Count,
            "Revoke-all must publish one notification per revoked session.");
        Assert.IsTrue(
            publisher.SessionRevokedNotifications.All(item => item.UserId == testUser.Id),
            "Revoke-all notifications must target the revoked user only.");

        var notifiedSessionIds = publisher.SessionRevokedNotifications
            .Select(item => ReadSessionId(item.Message))
            .OrderBy(id => id)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[] { publicSessionId, confidentialSessionId },
            notifiedSessionIds,
            "Revoke-all notifications must carry authoritative session identifiers.");
        foreach (var notification in publisher.SessionRevokedNotifications)
        {
            AssertSessionRevokedNotification(
                notification.Message,
                ReadSessionId(notification.Message),
                "Revoke-all session notification");
        }
    }

    private static void AssertSessionRevokedNotification(
        RealtimeMessage message,
        Guid expectedSessionId,
        string scenario)
    {
        Assert.AreEqual(RealtimeMessageCodes.SessionRevoked, message.Code);
        Assert.IsNotNull(message.Data);
        Assert.AreEqual(1, message.Data!.Count, $"{scenario} payload must only expose sessionId.");
        Assert.IsTrue(
            message.Data.ContainsKey("sessionId"),
            $"{scenario} payload must include sessionId.");
        foreach (var key in ForbiddenNotificationDataKeys)
        {
            Assert.IsFalse(
                message.Data.ContainsKey(key),
                $"{scenario} payload must not expose '{key}'.");
        }

        Assert.AreEqual(expectedSessionId, ReadSessionId(message));
    }

    private static Guid ReadSessionId(RealtimeMessage message)
    {
        Assert.IsNotNull(message.Data);
        Assert.IsTrue(
            message.Data!.TryGetValue("sessionId", out var rawSessionId),
            "Session revoked notification must include sessionId.");
        return rawSessionId switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => throw new AssertFailedException(
                $"Session revoked notification sessionId must be a GUID, got '{rawSessionId}'."),
        };
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
                "OIDC revoke notification test user",
                password)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
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

    private sealed class RecordingRealtimePublisher : IRealtimePublisher
    {
        public IReadOnlyList<(Guid UserId, RealtimeMessage Message)> SessionRevokedNotifications
            => _sessionRevokedNotifications;

        private readonly List<(Guid UserId, RealtimeMessage Message)> _sessionRevokedNotifications = [];

        public void Reset() => _sessionRevokedNotifications.Clear();

        public Task PublishToUserAsync(
            Guid userId,
            RealtimeMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message.Code == RealtimeMessageCodes.SessionRevoked)
            {
                _sessionRevokedNotifications.Add((userId, message));
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