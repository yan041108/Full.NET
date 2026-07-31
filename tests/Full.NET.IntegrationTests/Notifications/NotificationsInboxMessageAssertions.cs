using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>站内信收件箱纵向切片验收夹具。</summary>
internal static class NotificationsInboxMessageAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifySendRequiresWritePermissionAsync(factory, client, cancellationToken);
        await VerifySendListReadAndMarkReadLifecycleAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiNotificationsInboxMessagesContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifySendRequiresWritePermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            await factory.CreateHostAccessTokenAsync(
                ["notifications.inbox.read"],
                cancellationToken),
            new SendHostInboxMessageRequest(
                Guid.NewGuid(),
                "无权限",
                "正文"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task VerifySendListReadAndMarkReadLifecycleAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var currentUser = await GetCurrentUserAsync(client, adminToken, cancellationToken);
        var title = $"站内信-{Guid.NewGuid():N}"[..20];
        const string content = "集成测试站内信正文";

        using var sendRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            adminToken,
            new SendHostInboxMessageRequest(currentUser.Id, title, content));
        using var sendResponse = await client.SendAsync(sendRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, sendResponse.StatusCode);
        var created = await sendResponse.Content.ReadFromJsonAsync<InboxMessageResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(InboxMessageStatuses.Unread, created.Status);
        Assert.AreEqual(title, created.Title);

        using var unreadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages/unread-count");
        unreadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var unreadResponse = await client.SendAsync(unreadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, unreadResponse.StatusCode);
        var unread = await unreadResponse.Content.ReadFromJsonAsync<InboxUnreadCountResponse>(
            cancellationToken);
        Assert.IsNotNull(unread);
        Assert.IsTrue(unread.UnreadCount >= 1);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedInboxMessageResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var readRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{created.Id:D}/read",
            adminToken,
            new { });
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readResponse.StatusCode);
        var read = await readResponse.Content.ReadFromJsonAsync<InboxMessageResponse>(
            cancellationToken);
        Assert.IsNotNull(read);
        Assert.AreEqual(InboxMessageStatuses.Read, read.Status);
        Assert.IsNotNull(read.ReadAtUtc);

        using var invalidRecipientRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            adminToken,
            new SendHostInboxMessageRequest(Guid.NewGuid(), "无效收件人", "正文"));
        using var invalidRecipientResponse = await client.SendAsync(
            invalidRecipientRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, invalidRecipientResponse.StatusCode);
        using var recipientProblem = JsonDocument.Parse(
            await invalidRecipientResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            NotificationsErrorCodes.InboxRecipientNotFound,
            recipientProblem.RootElement.GetProperty("code").GetString());

        using var readAllRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/my-inbox-messages/read-all",
            adminToken,
            new { });
        using var readAllResponse = await client.SendAsync(readAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readAllResponse.StatusCode);
        var readAll = await readAllResponse.Content.ReadFromJsonAsync<InboxUnreadCountResponse>(
            cancellationToken);
        Assert.IsNotNull(readAll);
        Assert.AreEqual(0, readAll.UnreadCount);

        await VerifyInboxOutboxAsync(
            factory,
            currentUser.Id,
            created,
            cancellationToken);
    }

    private sealed record PagedInboxMessageResponses(
        InboxMessageResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task VerifyInboxOutboxAsync(
        FullNetApiFactory factory,
        Guid recipientUserId,
        InboxMessageResponse created,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var serializer = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventSerializer>();
            var receivedRows = await ReadOutboxAsync(
                query,
                NotificationRealtimeEventTypes.InboxMessageReceived,
                cancellationToken);
            Assert.HasCount(1, receivedRows);
            Assert.AreEqual(1, receivedRows[0].SchemaVersion);
            var received = serializer
                .Deserialize<InboxMessageReceivedIntegrationEvent>(
                    receivedRows[0].Payload);
            Assert.AreEqual(recipientUserId, received.RecipientUserId);
            Assert.AreEqual(created.Id, received.MessageId);
            Assert.AreEqual(created.Title, received.Title);

            var readStateRows = await ReadOutboxAsync(
                query,
                NotificationRealtimeEventTypes.InboxReadStateChanged,
                cancellationToken);
            Assert.HasCount(1, readStateRows);
            Assert.AreEqual(1, readStateRows[0].SchemaVersion);
            var readState = serializer
                .Deserialize<InboxReadStateChangedIntegrationEvent>(
                    readStateRows[0].Payload);
            Assert.AreEqual(recipientUserId, readState.RecipientUserId);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static Task<IReadOnlyList<NotificationOutboxRecord>> ReadOutboxAsync(
        IQueryExecutor query,
        string messageType,
        CancellationToken cancellationToken) =>
        query.QueryAsync<NotificationOutboxRecord>(
            new SqlStatement(
                "test.notifications.outbox_by_message_type",
                """
                SELECT MessageType, SchemaVersion, Payload
                FROM fn_outbox_message
                WHERE MessageType = @MessageType
                ORDER BY OccurredAtUtc DESC, Id
                """,
                SqlDataScope.Global),
            new { MessageType = messageType },
            cancellationToken);

    private sealed class NotificationOutboxRecord
    {
        public string MessageType { get; set; } = string.Empty;

        public int SchemaVersion { get; set; }

        public byte[] Payload { get; set; } = [];
    }

    private static async Task<CurrentUserResponse> GetCurrentUserAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(
            cancellationToken);
        Assert.IsNotNull(currentUser);
        return currentUser;
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
