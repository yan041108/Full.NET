using System.Net;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>Host 公告纵向切片验收夹具。</summary>
internal static class NotificationsHostAnnouncementAssertions
{
    public static async Task VerifyArchiveAsync(FullNetApiFactory factory, CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var admin = await LoginAsHostAdminAsync(client, cancellationToken);
        var archiveToken = await factory.CreateHostAccessTokenAsync(
            [HostAnnouncementPermissions.Read, HostAnnouncementPermissions.Archive], cancellationToken);
        var restoreToken = await factory.CreateHostAccessTokenAsync(
            [HostAnnouncementPermissions.Read, HostAnnouncementPermissions.Restore], cancellationToken);
        const string collection = "/api/v1/notifications/host-announcements";
        var title = $"archive-{Guid.NewGuid():N}";
        var draft = await SendAsync<HostAnnouncementResponse>(HttpMethod.Post, collection, admin,
            new CreateHostAnnouncementRequest(title, "Archive test content"), HttpStatusCode.Created);
        var item = $"{collection}/{draft.Id:D}";
        var archive = $"{item}/archive";
        var restore = $"{item}/restore";
        await AssertAnnouncementPermissionDeniedAsync(client, restoreToken, HttpMethod.Post, archive,
            cancellationToken, new ChangeHostAnnouncementArchiveRequest(draft.Version));
        var archived = await ChangeAsync(archive, archiveToken, draft.Version);
        Assert.AreEqual(AnnouncementStatuses.Draft, archived.Status);
        Assert.IsNotNull(archived.ArchivedAtUtc);
        Assert.AreEqual(draft.Version + 1, archived.Version);
        var sqlServer = factory.Provider == DatabaseProvider.SqlServer;
        await using DbConnection database = sqlServer ? new SqlConnection(factory.ConnectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);
        await database.OpenAsync(cancellationToken);
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(database, "378_NotificationsAnnouncementArchive.sql", sqlServer);
        var migration = ReviewFixMigrationRecoverySupport.CreateRunner(factory.Provider, factory.ConnectionString);
        Assert.AreEqual(1, (await migration.MigrateAsync(cancellationToken)).ExecutedScriptCount);
        Assert.AreEqual(0, (await migration.MigrateAsync(cancellationToken)).ExecutedScriptCount);
        var repeated = await ChangeAsync(archive, archiveToken, archived.Version);
        Assert.AreEqual(archived.Version, repeated.Version);
        Assert.AreEqual(archived.ArchivedAtUtc, repeated.ArchivedAtUtc);
        await AssertAnnouncementPermissionDeniedAsync(client, archiveToken, HttpMethod.Post, restore,
            cancellationToken, new ChangeHostAnnouncementArchiveRequest(archived.Version));
        await AssertFailureAsync(restore, restoreToken, new ChangeHostAnnouncementArchiveRequest(draft.Version), HttpStatusCode.Conflict);
        await AssertFailureAsync($"{item}/publish", admin, new PublishHostAnnouncementRequest(archived.Version), HttpStatusCode.BadRequest);
        var activePage = await ListAsync(false);
        Assert.AreEqual(0L, activePage.Total);
        var archivePage = await ListAsync(true);
        Assert.AreEqual(1L, archivePage.Total);
        Assert.AreEqual(draft.Id, archivePage.Items.Single().Id);

        var restored = await ChangeAsync(restore, restoreToken, archived.Version);
        Assert.IsNull(restored.ArchivedAtUtc);
        Assert.AreEqual(draft.Content, restored.Content);
        var published = await SendAsync<HostAnnouncementResponse>(HttpMethod.Post, $"{item}/publish", admin,
            new PublishHostAnnouncementRequest(restored.Version));
        await AssertFailureAsync(archive, archiveToken, new ChangeHostAnnouncementArchiveRequest(published.Version), HttpStatusCode.BadRequest);
        await SendAsync<ReceivedHostAnnouncementDetailResponse>(HttpMethod.Post,
            $"/api/v1/notifications/my-host-announcements/{draft.Id:D}/read", admin, null);
        var retracted = await SendAsync<HostAnnouncementResponse>(HttpMethod.Post, $"{item}/retract", admin,
            new RetractHostAnnouncementRequest(published.Version));
        var archivedRetracted = await ChangeAsync(archive, archiveToken, retracted.Version);
        Assert.AreEqual(AnnouncementStatuses.Retracted, archivedRetracted.Status);
        Assert.AreEqual(retracted.RetractedAtUtc, archivedRetracted.RetractedAtUtc);
        var stats = await SendAsync<HostAnnouncementReadStatsResponse>(HttpMethod.Get, $"{item}/read-stats", admin, null);
        Assert.AreEqual(1, stats.ReadCount);
        var restoredRetracted = await ChangeAsync(restore, restoreToken, archivedRetracted.Version);
        Assert.AreEqual(AnnouncementStatuses.Retracted, restoredRetracted.Status);
        Assert.IsNull(restoredRetracted.ArchivedAtUtc);
        await OpenApiNotificationsHostAnnouncementsContractAssertions.VerifyAsync(client, cancellationToken);

        Task<HostAnnouncementResponse> ChangeAsync(string path, string token, int version) =>
            SendAsync<HostAnnouncementResponse>(HttpMethod.Post, path, token, new ChangeHostAnnouncementArchiveRequest(version));

        Task<PagedHostAnnouncementResponses> ListAsync(bool archivedOnly) =>
            SendAsync<PagedHostAnnouncementResponses>(HttpMethod.Get,
                $"{collection}?title={title}&archived={archivedOnly}", admin, null);

        async Task AssertFailureAsync(string path, string token, object body, HttpStatusCode expected)
        {
            using var request = CreateBearerJsonRequest(HttpMethod.Post, path, token, body);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        async Task<T> SendAsync<T>(HttpMethod method, string path, string token, object? body, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, cancellationToken);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new InvalidOperationException($"Missing response for {path}.");
        }
    }

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyDraftUpdatePublishLifecycleAsync(
            factory,
            client,
            cancellationToken);
        await VerifyExactAnnouncementActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await VerifyReceivedAnnouncementReadLifecycleAsync(client, cancellationToken);
        await OpenApiNotificationsHostAnnouncementsContractAssertions.VerifyAsync(client, cancellationToken);
        await OpenApiNotificationsHostAnnouncementReceiptsContractAssertions.VerifyAsync(client, cancellationToken);
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
        FullNetApiFactory factory,
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
        await VerifyPublishedOutboxAsync(
            factory,
            published,
            cancellationToken);

        using var republishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            adminToken,
            new PublishHostAnnouncementRequest(published.Version));
        using var republishResponse = await client.SendAsync(republishRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, republishResponse.StatusCode);
        var republished = await republishResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(republished);
        Assert.AreEqual(AnnouncementStatuses.Published, republished.Status);

        using var retractRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/retract",
            adminToken,
            new RetractHostAnnouncementRequest(published.Version));
        using var retractResponse = await client.SendAsync(retractRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, retractResponse.StatusCode);
        var retracted = await retractResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(retracted);
        Assert.AreEqual(AnnouncementStatuses.Retracted, retracted.Status);
        Assert.IsNotNull(retracted.RetractedAtUtc);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/host-announcements?page=1&pageSize=20&status=retracted");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedHostAnnouncementResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));
        Assert.IsTrue(page.Items.All(item => item.Status == AnnouncementStatuses.Retracted));
    }

    private static async Task VerifyReceivedAnnouncementReadLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var title = $"收件公告-{Guid.NewGuid():N}"[..24];
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            adminToken,
            new CreateHostAnnouncementRequest(title, "收件正文"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        using var publishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            adminToken,
            new PublishHostAnnouncementRequest(created.Version));
        using var publishResponse = await client.SendAsync(publishRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode);

        using var unreadRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-host-announcements/unread-count");
        unreadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var unreadResponse = await client.SendAsync(unreadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, unreadResponse.StatusCode);
        var unread = await unreadResponse.Content.ReadFromJsonAsync<HostAnnouncementUnreadCountResponse>(
            cancellationToken);
        Assert.IsNotNull(unread);
        Assert.IsTrue(unread.UnreadCount >= 1);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/my-host-announcements?page=1&pageSize=20&isRead=false");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedReceivedHostAnnouncementListItems>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id && !item.IsRead));

        using var readRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/notifications/my-host-announcements/{created.Id:D}/read");
        readRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readResponse.StatusCode);
        var readDetail = await readResponse.Content.ReadFromJsonAsync<ReceivedHostAnnouncementDetailResponse>(
            cancellationToken);
        Assert.IsNotNull(readDetail);
        Assert.IsTrue(readDetail.IsRead);
        Assert.IsNotNull(readDetail.ReadAtUtc);

        using var statsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/read-stats");
        statsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var statsResponse = await client.SendAsync(statsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, statsResponse.StatusCode);
        var stats = await statsResponse.Content.ReadFromJsonAsync<HostAnnouncementReadStatsResponse>(
            cancellationToken);
        Assert.IsNotNull(stats);
        Assert.IsTrue(stats.ReadCount >= 1);
        Assert.IsTrue(stats.EligibleRecipientCount >= stats.ReadCount);
    }

    private sealed record PagedReceivedHostAnnouncementListItems(
        ReceivedHostAnnouncementListItemResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task VerifyExactAnnouncementActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var title = $"边界公告-{Guid.NewGuid():N}"[..24];
        var content = "边界测试正文";

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

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostAnnouncementPermissions.Read],
            cancellationToken);
        using var readListRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/notifications/host-announcements?page=1&pageSize=20");
        readListRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var readListResponse = await client.SendAsync(readListRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, readListResponse.StatusCode);
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            cancellationToken,
            new CreateHostAnnouncementRequest("拒绝创建", "正文"));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{created.Id:D}",
            cancellationToken,
            new UpdateHostAnnouncementRequest("拒绝更新", "正文", created.Version));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            cancellationToken,
            new PublishHostAnnouncementRequest(created.Version));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/retract",
            cancellationToken,
            new RetractHostAnnouncementRequest(created.Version));

        var createOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostAnnouncementPermissions.Read,
                HostAnnouncementPermissions.Create,
            ],
            cancellationToken);
        var createOnlyTitle = $"仅创建-{Guid.NewGuid():N}"[..24];
        using var createOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            createOnlyToken,
            new CreateHostAnnouncementRequest(createOnlyTitle, "create-only"));
        using var createOnlyResponse = await client.SendAsync(createOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createOnlyResponse.StatusCode);
        var createOnlyAnnouncement = await createOnlyResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(createOnlyAnnouncement);
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            createOnlyToken,
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{createOnlyAnnouncement.Id:D}",
            cancellationToken,
            new UpdateHostAnnouncementRequest("拒绝", "正文", createOnlyAnnouncement.Version));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            createOnlyToken,
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{createOnlyAnnouncement.Id:D}/publish",
            cancellationToken,
            new PublishHostAnnouncementRequest(createOnlyAnnouncement.Version));

        var updateOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostAnnouncementPermissions.Read,
                HostAnnouncementPermissions.Update,
            ],
            cancellationToken);
        using var updateOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{created.Id:D}",
            updateOnlyToken,
            new UpdateHostAnnouncementRequest("更新仅更新", "正文", created.Version));
        using var updateOnlyResponse = await client.SendAsync(updateOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateOnlyResponse.StatusCode);
        var updatedByUpdateOnly = await updateOnlyResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(updatedByUpdateOnly);
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            updateOnlyToken,
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            cancellationToken,
            new CreateHostAnnouncementRequest("拒绝", "正文"));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            updateOnlyToken,
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{created.Id:D}/publish",
            cancellationToken,
            new PublishHostAnnouncementRequest(updatedByUpdateOnly.Version));

        var publishTargetTitle = $"发布目标-{Guid.NewGuid():N}"[..24];
        using var publishSeedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            adminToken,
            new CreateHostAnnouncementRequest(publishTargetTitle, "publish-only"));
        using var publishSeedResponse = await client.SendAsync(publishSeedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, publishSeedResponse.StatusCode);
        var publishTarget = await publishSeedResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(publishTarget);

        var publishOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostAnnouncementPermissions.Read,
                HostAnnouncementPermissions.Publish,
            ],
            cancellationToken);
        using var publishOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{publishTarget.Id:D}/publish",
            publishOnlyToken,
            new PublishHostAnnouncementRequest(publishTarget.Version));
        using var publishOnlyResponse = await client.SendAsync(publishOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishOnlyResponse.StatusCode);
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            publishOnlyToken,
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            cancellationToken,
            new CreateHostAnnouncementRequest("拒绝", "正文"));
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            publishOnlyToken,
            HttpMethod.Put,
            $"/api/v1/notifications/host-announcements/{publishTarget.Id:D}",
            cancellationToken,
            new UpdateHostAnnouncementRequest("拒绝", "正文", publishTarget.Version + 1));

        var retractTargetTitle = $"撤回目标-{Guid.NewGuid():N}"[..24];
        using var retractSeedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            adminToken,
            new CreateHostAnnouncementRequest(retractTargetTitle, "retract-only"));
        using var retractSeedResponse = await client.SendAsync(retractSeedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, retractSeedResponse.StatusCode);
        var retractTarget = await retractSeedResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(retractTarget);
        using var retractSeedPublishRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{retractTarget.Id:D}/publish",
            adminToken,
            new PublishHostAnnouncementRequest(retractTarget.Version));
        using var retractSeedPublishResponse = await client.SendAsync(
            retractSeedPublishRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, retractSeedPublishResponse.StatusCode);
        var retractSeedPublished = await retractSeedPublishResponse.Content.ReadFromJsonAsync<HostAnnouncementResponse>(
            cancellationToken);
        Assert.IsNotNull(retractSeedPublished);

        var retractOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostAnnouncementPermissions.Read,
                HostAnnouncementPermissions.Retract,
            ],
            cancellationToken);
        using var retractOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/host-announcements/{retractTarget.Id:D}/retract",
            retractOnlyToken,
            new RetractHostAnnouncementRequest(retractSeedPublished.Version));
        using var retractOnlyResponse = await client.SendAsync(retractOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, retractOnlyResponse.StatusCode);
        await AssertAnnouncementPermissionDeniedAsync(
            client,
            retractOnlyToken,
            HttpMethod.Post,
            "/api/v1/notifications/host-announcements",
            cancellationToken,
            new CreateHostAnnouncementRequest("拒绝", "正文"));
    }

    private static async Task AssertAnnouncementPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        TRequest body)
    {
        using var request = CreateBearerJsonRequest(method, path, accessToken, body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record PagedHostAnnouncementResponses(
        HostAnnouncementResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task VerifyPublishedOutboxAsync(
        FullNetApiFactory factory,
        HostAnnouncementResponse published,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var rows = await scope.ServiceProvider
                .GetRequiredService<IQueryExecutor>()
                .QueryAsync<NotificationOutboxRecord>(
                    new SqlStatement(
                        "test.notifications.announcement_published_outbox",
                        """
                        SELECT MessageType, SchemaVersion, Payload
                        FROM fn_outbox_message
                        WHERE MessageType = @MessageType
                        ORDER BY OccurredAtUtc DESC, Id
                        """,
                        SqlDataScope.Global),
                    new
                    {
                        MessageType =
                            NotificationRealtimeEventTypes.AnnouncementPublished,
                    },
                    cancellationToken);

            Assert.HasCount(1, rows);
            Assert.AreEqual(1, rows[0].SchemaVersion);
            var integrationEvent = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventSerializer>()
                .Deserialize<AnnouncementPublishedIntegrationEvent>(
                    rows[0].Payload);
            Assert.AreEqual(published.Id, integrationEvent.AnnouncementId);
            Assert.AreEqual(published.Title, integrationEvent.Title);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private sealed class NotificationOutboxRecord
    {
        public string MessageType { get; set; } = string.Empty;

        public int SchemaVersion { get; set; }

        public byte[] Payload { get; set; } = [];
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
