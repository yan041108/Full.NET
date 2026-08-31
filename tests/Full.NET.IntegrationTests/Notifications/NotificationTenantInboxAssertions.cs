using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>Tenant Inbox 隔离、权威未读数与 Host 契约兼容验收。</summary>
internal static class NotificationTenantInboxAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var hostClient = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("localhost");
        using var otherTenantClient = factory.CreateClientForHost("localhost");

        var hostAdminToken = await LoginAsHostAdminAsync(hostClient, cancellationToken);
        var tenantSwitchToken = await LoginAsHostAdminAsync(tenantClient, cancellationToken);
        var otherSwitchToken = await LoginAsHostAdminAsync(otherTenantClient, cancellationToken);
        var hostUser = await GetCurrentUserAsync(hostClient, hostAdminToken, cancellationToken);
        var hostTitle = $"host-{Guid.NewGuid():N}"[..20];
        var hostMessage = await SendHostInboxAsync(
            hostClient,
            hostAdminToken,
            hostUser.Id,
            hostTitle,
            "host-body",
            cancellationToken);

        var tenantToken = await EnterAcmeTenantAsync(tenantClient, tenantSwitchToken, cancellationToken);
        using var hostSendInTenant = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            tenantToken,
            new SendHostInboxMessageRequest(hostUser.Id, "拒绝", "租户不得走 Host 发信"));
        using var hostSendInTenantResponse = await tenantClient.SendAsync(
            hostSendInTenant,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, hostSendInTenantResponse.StatusCode);

        var tenantTitle = $"tenant-{Guid.NewGuid():N}"[..20];
        using var tenantSendRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/tenant-inbox-messages",
            tenantToken,
            new
            {
                recipientUserId = hostUser.Id,
                title = tenantTitle,
                content = "tenant-body",
                tenantId = Guid.NewGuid(),
            });
        using var tenantSendResponse = await tenantClient.SendAsync(tenantSendRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            tenantSendResponse.StatusCode,
            await tenantSendResponse.Content.ReadAsStringAsync(cancellationToken));
        var tenantMessage = await tenantSendResponse.Content.ReadFromJsonAsync<InboxMessageResponse>(
            cancellationToken);
        Assert.IsNotNull(tenantMessage);
        Assert.AreEqual(InboxMessageStatuses.Unread, tenantMessage.Status);

        var tenantUnread = await GetUnreadCountAsync(tenantClient, tenantToken, cancellationToken);
        var tenantPage = await ListInboxAsync(tenantClient, tenantToken, cancellationToken);
        Assert.IsTrue(tenantPage.Items.Any(item => item.Id == tenantMessage.Id));
        Assert.IsFalse(tenantPage.Items.Any(item => item.Id == hostMessage.Id));
        Assert.AreEqual(
            tenantPage.Items.Count(item => item.Status == InboxMessageStatuses.Unread),
            tenantUnread.UnreadCount,
            "未读数必须以当前作用域数据库行为准，而不是 SignalR 累加。");

        var hostPage = await ListInboxAsync(hostClient, hostAdminToken, cancellationToken);
        Assert.IsTrue(hostPage.Items.Any(item => item.Id == hostMessage.Id));
        Assert.IsFalse(hostPage.Items.Any(item => item.Id == tenantMessage.Id));

        using var hostMarkTenant = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{tenantMessage.Id:D}/read",
            hostAdminToken,
            new { });
        using var hostMarkTenantResponse = await hostClient.SendAsync(hostMarkTenant, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, hostMarkTenantResponse.StatusCode);

        var otherTenant = await CreateTenantAsync(hostClient, hostAdminToken, cancellationToken);
        var otherToken = await EnterTenantAsync(
            otherTenantClient,
            otherSwitchToken,
            otherTenant.Id,
            cancellationToken);
        using var hostSendAsHost = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/tenant-inbox-messages",
            hostAdminToken,
            new SendTenantInboxMessageRequest(hostUser.Id, "拒绝", "Host 不得走租户发信"));
        using var hostSendAsHostResponse = await hostClient.SendAsync(hostSendAsHost, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, hostSendAsHostResponse.StatusCode);

        var otherPage = await ListInboxAsync(otherTenantClient, otherToken, cancellationToken);
        Assert.IsFalse(otherPage.Items.Any(item => item.Id == tenantMessage.Id));
        using var crossRead = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{tenantMessage.Id:D}/read",
            otherToken,
            new { });
        using var crossReadResponse = await otherTenantClient.SendAsync(crossRead, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, crossReadResponse.StatusCode);
        using var crossProblem = JsonDocument.Parse(
            await crossReadResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            NotificationsErrorCodes.InboxMessageNotFound,
            crossProblem.RootElement.GetProperty("code").GetString());

        using var markRead = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{tenantMessage.Id:D}/read",
            tenantToken,
            new { });
        using var markReadResponse = await tenantClient.SendAsync(markRead, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, markReadResponse.StatusCode);
        var afterRead = await GetUnreadCountAsync(tenantClient, tenantToken, cancellationToken);
        var afterReadPage = await ListInboxAsync(tenantClient, tenantToken, cancellationToken);
        Assert.AreEqual(
            afterReadPage.Items.Count(item => item.Status == InboxMessageStatuses.Unread),
            afterRead.UnreadCount);

        await OpenApiNotificationsInboxMessagesContractAssertions.VerifyAsync(
            tenantClient,
            cancellationToken);
    }

    private static async Task<InboxMessageResponse> SendHostInboxAsync(
        HttpClient client,
        string token,
        Guid recipientUserId,
        string title,
        string content,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-inbox-messages",
            token,
            new SendHostInboxMessageRequest(recipientUserId, title, content));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<InboxMessageResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<InboxUnreadCountResponse> GetUnreadCountAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages/unread-count");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var unread = await response.Content.ReadFromJsonAsync<InboxUnreadCountResponse>(cancellationToken);
        Assert.IsNotNull(unread);
        return unread;
    }

    private static async Task<PagedInboxMessageResponses> ListInboxAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?page=1&pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedInboxMessageResponses>(cancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private static async Task<string> EnterAcmeTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");
        return await EnterTenantAsync(client, hostAccessToken, acme.Id, cancellationToken);
    }

    private static async Task<string> EnterTenantAsync(
        HttpClient client,
        string hostAccessToken,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        using var enterRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            "/api/v1/tenancy/context",
            hostAccessToken,
            new ChangeTenantContextRequest(tenantId));
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }

    private static async Task<TenantSummary> CreateTenantAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var identifier = $"nbox-{Guid.NewGuid():N}"[..14];
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(identifier, "站内信隔离租户", $"{identifier}.localhost"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        var created = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
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
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        Assert.IsNotNull(currentUser);
        return currentUser;
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private sealed record PagedInboxMessageResponses(
        InboxMessageResponse[] Items,
        int Page,
        int PageSize,
        long Total);
}
