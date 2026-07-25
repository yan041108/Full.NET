using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>Host 公告纵向切片验收夹具。</summary>
internal static class NotificationsHostAnnouncementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyDraftUpdatePublishLifecycleAsync(client, cancellationToken);
        await OpenApiNotificationsHostAnnouncementsContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/host-announcements?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDraftUpdatePublishLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var title = $"集成公告-{Guid.NewGuid():N}"[..24];
        var content = "集成测试公告正文";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            adminToken,
            new CreateHostAnnouncementRequest(title, content));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(AnnouncementStatuses.Draft, created.Status);
        Assert.AreEqual(title, created.Title);
        Assert.AreEqual(content, created.Content);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{created.Id:D}",
            adminToken,
            new UpdateHostAnnouncementRequest("更新后标题", "更新后正文", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后标题", updated.Title);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{created.Id:D}",
            adminToken,
            new UpdateHostAnnouncementRequest("陈旧版本", "陈旧正文", created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var staleProblem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            NotificationsErrorCodes.AnnouncementConcurrencyConflict,
            staleProblem.RootElement.GetProperty("code").GetString());

        using var publishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            adminToken,
            new PublishHostAnnouncementRequest(updated.Version));
        using var publishResponse = await client.SendAsync(publishRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(published);
        Assert.AreEqual(AnnouncementStatuses.Published, published.Status);
        Assert.IsNotNull(published.PublishedAtUtc);

        using var republishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            adminToken,
            new PublishHostAnnouncementRequest(published.Version));
        using var republishResponse = await client.SendAsync(republishRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, republishResponse.StatusCode);
        using var invalidStatusProblem = JsonDocument.Parse(
            await republishResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            NotificationsErrorCodes.AnnouncementInvalidStatus,
            invalidStatusProblem.RootElement.GetProperty("code").GetString());

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/host-announcements?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedHostAnnouncementResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));
    }

    private sealed record PagedHostAnnouncementResponses(
        HostAnnouncementResponse[] Items,
        int Page,
        int PageSize,
        long Total);

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
