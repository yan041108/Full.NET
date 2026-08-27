using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的 Notifications HTTP/JSON/SignalR 链路断言。
/// </summary>
internal static class NativeApiNotificationsE2EAssertions
{
    private static readonly TimeSpan RealtimeWaitTimeout = TimeSpan.FromSeconds(15);

    public static async Task VerifyNotificationsFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var redisConnectionString =
            await SharedDatabaseFixture.GetRedisConnectionStringAsync()
                .ConfigureAwait(false);
        var settings = new Dictionary<string, string?>
        {
            ["Realtime:RedisBackplaneConnectionString"] = redisConnectionString,
            ["ConnectionStrings:redis"] = redisConnectionString,
            ["Testing:OutboxCommandPath"] = "TypedPlan",
        };

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            settings,
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
                client,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        var receivedMessages = Channel.CreateUnbounded<RealtimeMessage>();
        var hubUrl = new Uri(host.BaseAddress, "hubs/notifications");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                hubUrl,
                options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    options.Transports = HttpTransportType.LongPolling;
                })
            .Build();
        connection.On<RealtimeMessage>(
            "ReceiveMessageAsync",
            message => receivedMessages.Writer.TryWrite(message));
        await connection.StartAsync(cancellationToken).ConfigureAwait(false);

        await VerifyAnnouncementLifecycleAsync(
                client,
                token,
                receivedMessages.Reader,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        await VerifyInboxLifecycleAsync(
                client,
                token,
                receivedMessages.Reader,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static async Task VerifyAnnouncementLifecycleAsync(
        HttpClient client,
        string token,
        ChannelReader<RealtimeMessage> receivedMessages,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        var title = $"native-aot-{Guid.NewGuid():N}"[..24];
        const string content = "Native AOT announcement body";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            token,
            new CreateHostAnnouncementRequest(title, content));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            createResponse,
            HttpStatusCode.Created,
            "Create host announcement",
            cancellationToken).ConfigureAwait(false);
        var created = await createResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(AnnouncementStatuses.Draft, created.Status);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{created.Id:D}",
            token,
            new UpdateHostAnnouncementRequest("Updated native title", "Updated body", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            updateResponse,
            HttpStatusCode.OK,
            "Update host announcement",
            cancellationToken).ConfigureAwait(false);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(updated);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var publishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            token,
            new PublishHostAnnouncementRequest(updated.Version));
        using var publishResponse = await client.SendAsync(publishRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            publishResponse,
            HttpStatusCode.OK,
            "Publish host announcement",
            cancellationToken).ConfigureAwait(false);
        var published = await publishResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(published);
        Assert.AreEqual(AnnouncementStatuses.Published, published.Status);

        var announcementRealtime = await WaitForMessageAsync(
                receivedMessages,
                RealtimeMessageCodes.AnnouncementPublished,
                message => message.Data is not null
                    && ReadGuid(message.Data, "announcementId") == created.Id,
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(announcementRealtime);
        Assert.IsNotNull(announcementRealtime.Data);
        Assert.AreEqual(published.Title, ReadString(announcementRealtime.Data, "title"));

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/host-announcements?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            listResponse,
            HttpStatusCode.OK,
            "List host announcements",
            cancellationToken).ConfigureAwait(false);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedHostAnnouncementResponses>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));
    }

    private static async Task VerifyInboxLifecycleAsync(
        HttpClient client,
        string token,
        ChannelReader<RealtimeMessage> receivedMessages,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        var title = $"inbox-{Guid.NewGuid():N}"[..20];
        const string content = "Native AOT inbox body";

        using var sendRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            token,
            new SendHostInboxMessageRequest(currentUser.Id, title, content));
        using var sendResponse = await client.SendAsync(sendRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            sendResponse,
            HttpStatusCode.Created,
            "Send host inbox message",
            cancellationToken).ConfigureAwait(false);
        var created = await sendResponse.Content.ReadFromJsonAsync<InboxMessageResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);

        var inboxRealtime = await WaitForMessageAsync(
                receivedMessages,
                RealtimeMessageCodes.InboxMessageReceived,
                message => message.Data is not null
                    && ReadGuid(message.Data, "messageId") == created.Id,
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(inboxRealtime);
        Assert.IsNotNull(inboxRealtime.Data);
        Assert.AreEqual(title, ReadString(inboxRealtime.Data, "title"));

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            listResponse,
            HttpStatusCode.OK,
            "List my inbox messages",
            cancellationToken).ConfigureAwait(false);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedInboxMessageResponses>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(page);
        var listed = page.Items.SingleOrDefault(item => item.Id == created.Id);
        Assert.IsNotNull(listed);
        Assert.AreEqual(InboxMessageStatuses.Unread, listed.Status);

        using var unreadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages/unread-count");
        unreadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var unreadResponse = await client.SendAsync(unreadRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            unreadResponse,
            HttpStatusCode.OK,
            "Get inbox unread count",
            cancellationToken).ConfigureAwait(false);
        var unreadBefore = await unreadResponse.Content.ReadFromJsonAsync<InboxUnreadCountResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(unreadBefore);
        Assert.IsTrue(unreadBefore.UnreadCount >= 1);

        using var readRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{created.Id:D}/read",
            token,
            new { });
        using var readResponse = await client.SendAsync(readRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            readResponse,
            HttpStatusCode.OK,
            "Mark inbox message read",
            cancellationToken).ConfigureAwait(false);

        using var unreadAfterRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages/unread-count");
        unreadAfterRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var unreadAfterResponse = await client.SendAsync(unreadAfterRequest, cancellationToken)
            .ConfigureAwait(false);
        await NativeApiE2EAssertions.AssertStatusAsync(
            unreadAfterResponse,
            HttpStatusCode.OK,
            "Get inbox unread count after read",
            cancellationToken).ConfigureAwait(false);
        var unreadAfter = await unreadAfterResponse.Content.ReadFromJsonAsync<InboxUnreadCountResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(unreadAfter);
        Assert.AreEqual(unreadBefore.UnreadCount - 1, unreadAfter.UnreadCount);

        var unreadRealtime = await WaitForMessageAsync(
                receivedMessages,
                RealtimeMessageCodes.InboxUnreadCountChanged,
                message => message.Data is not null
                    && ReadInt64(message.Data, "unreadCount") == unreadAfter.UnreadCount,
                logFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(unreadRealtime);
        Assert.IsNotNull(unreadRealtime.Data);
    }

    private static async Task<RealtimeMessage?> WaitForMessageAsync(
        ChannelReader<RealtimeMessage> reader,
        string code,
        Func<RealtimeMessage, bool> predicate,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RealtimeWaitTimeout);
        try
        {
            while (await reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var message))
                {
                    if (string.Equals(message.Code, code, StringComparison.Ordinal)
                        && predicate(message))
                    {
                        return message;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        var logTail = ReadNativeLogTail(logFilePath);
        Assert.Fail(
            $"Timed out waiting for realtime message '{code}'."
            + (string.IsNullOrEmpty(logTail) ? string.Empty : $"\nNative log tail:\n{logTail}"));
        return null;
    }

    private static async Task<CurrentUserResponse> GetCurrentUserAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(currentUser);
        return currentUser;
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Origin", "http://localhost");
        return request;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.String
                && Guid.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static long ReadInt64(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            long number => number,
            int intNumber => intNumber,
            JsonElement element when element.ValueKind == JsonValueKind.Number =>
                element.GetInt64(),
            _ => long.TryParse(value.ToString(), out var parsed) ? parsed : 0,
        };
    }

    private static string ReadNativeLogTail(string? logFilePath, int maxChars = 4_000)
    {
        if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
        {
            return string.Empty;
        }

        var content = File.ReadAllText(logFilePath);
        return content.Length <= maxChars ? content : content[^maxChars..];
    }

    private sealed record PagedHostAnnouncementResponses(
        HostAnnouncementResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private sealed record PagedInboxMessageResponses(
        InboxMessageResponse[] Items,
        int Page,
        int PageSize,
        long Total);
}
