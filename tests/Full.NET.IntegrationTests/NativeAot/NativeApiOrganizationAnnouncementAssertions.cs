using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>通过正式 HTTP 验证非空机构公告受众、去重人数及阅读回执的原生查询。</summary>
internal static class NativeApiOrganizationAnnouncementAssertions
{
    internal static async Task VerifyAsync(
        HttpClient client,
        string hostToken,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        const string announcements = "/api/v1/notifications/host-announcements";
        const string received = "/api/v1/notifications/my-host-announcements";
        var host = new AnnouncementHttp(client, hostToken, logFilePath, cancellationToken);
        var actor = await host.SendAsync(HttpMethod.Get, "/api/v1/me");
        var actorId = actor.GetProperty("id").GetGuid();
        var available = await host.SendAsync(HttpMethod.Get, "/api/v1/tenancy/available");
        var tenantId = available.EnumerateArray().Single(item => item.GetProperty("identifier").GetString() == "local")
            .GetProperty("id").GetGuid();
        var tenantToken = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, hostToken, cancellationToken);
        var tenant = new AnnouncementHttp(client, tenantToken, logFilePath, cancellationToken);
        var units = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var unit = await tenant.SendAsync(HttpMethod.Post, "/api/v1/organization/units", new
            {
                parentId = (Guid?)null, code = $"native-ann-{Guid.NewGuid():N}",
                name = $"原生公告受众机构 {index}", displayOrder = index,
            }, HttpStatusCode.Created);
            var unitId = unit.GetProperty("id").GetGuid();
            units.Add(unitId);
            // 前两个机构只有同一名成员，第三个保持无隶属关系，分别检验去重和不可见边界。
            if (index < 2)
            {
                await tenant.SendAsync(HttpMethod.Post, "/api/v1/organization/user-units", new
                {
                    userId = actorId, unitId, isPrimary = false,
                }, HttpStatusCode.Created);
            }
        }

        await tenant.SendAsync(HttpMethod.Get, announcements, expected: HttpStatusCode.Forbidden);
        // 会话切换后旧令牌失效；后续所有 Host 请求均使用同一会话返回的新令牌。
        var changed = await tenant.SendAsync(HttpMethod.Put, "/api/v1/tenancy/context", new ChangeTenantContextRequest(null));
        var token = changed.GetProperty("accessToken").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        host = new AnnouncementHttp(client, token, logFilePath, cancellationToken);

        var title = $"native-org-{Guid.NewGuid():N}";
        var visible = await PublishAsync(host, title + "-visible", units.Take(2).ToArray(), tenantId);
        var hidden = await PublishAsync(host, title + "-hidden", [units[2]], tenantId);
        var visibleId = visible.GetProperty("id").GetGuid();
        var hiddenId = hidden.GetProperty("id").GetGuid();
        var statsPath = $"{announcements}/{visibleId:D}/read-stats";
        await host.SendAsync(HttpMethod.Get, statsPath, expected: HttpStatusCode.Unauthorized, anonymous: true);
        AssertStats(await host.SendAsync(HttpMethod.Get, statsPath), 1, 0, 1);
        AssertStats(await host.SendAsync(HttpMethod.Get, $"{announcements}/{hiddenId:D}/read-stats"), 0, 0, 0);

        var listPath = $"{received}?page=1&pageSize=100&title={Uri.EscapeDataString(title)}";
        await host.SendAsync(HttpMethod.Get, listPath, expected: HttpStatusCode.Unauthorized, anonymous: true);
        var page = await host.SendAsync(HttpMethod.Get, listPath);
        Assert.AreEqual(1L, page.GetProperty("total").GetInt64());
        Assert.AreEqual(1, page.GetProperty("items").GetArrayLength());
        var listed = page.GetProperty("items")[0];
        Assert.AreEqual(visibleId, listed.GetProperty("id").GetGuid());
        Assert.AreEqual(title + "-visible", listed.GetProperty("title").GetString());
        Assert.AreEqual(AnnouncementAudienceKinds.Organizations, listed.GetProperty("audienceKind").GetString());
        Assert.IsFalse(listed.GetProperty("isRead").GetBoolean());
        var denied = await host.SendAsync(HttpMethod.Get, $"{received}/{hiddenId:D}", expected: HttpStatusCode.Forbidden);
        Assert.AreEqual(NotificationsErrorCodes.AnnouncementNotRecipient, denied.GetProperty("code").GetString());

        var unreadBefore = await host.SendAsync(HttpMethod.Get, received + "/unread-count");
        var detail = await host.SendAsync(HttpMethod.Post, $"{received}/{visibleId:D}/read");
        Assert.AreEqual(visibleId, detail.GetProperty("id").GetGuid());
        Assert.IsTrue(detail.GetProperty("isRead").GetBoolean());
        Assert.AreNotEqual(JsonValueKind.Null, detail.GetProperty("readAtUtc").ValueKind);
        await host.SendAsync(HttpMethod.Post, $"{received}/{visibleId:D}/read");
        AssertStats(await host.SendAsync(HttpMethod.Get, statsPath), 1, 1, 0);
        var unreadAfter = await host.SendAsync(HttpMethod.Get, received + "/unread-count");
        Assert.AreEqual(unreadBefore.GetProperty("unreadCount").GetInt32() - 1,
            unreadAfter.GetProperty("unreadCount").GetInt32());
        var readPage = await host.SendAsync(HttpMethod.Get, listPath + "&isRead=true");
        Assert.AreEqual(1L, readPage.GetProperty("total").GetInt64());
        Assert.AreEqual(visibleId, readPage.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> PublishAsync(AnnouncementHttp host, string title, Guid[] units, Guid tenantId)
    {
        const string path = "/api/v1/notifications/host-announcements";
        var created = await host.SendAsync(HttpMethod.Post, path, new CreateHostAnnouncementRequest(
            title, "原生机构受众公告正文", AudienceKind: AnnouncementAudienceKinds.Organizations,
            TargetOrganizations: units.Select(unitId => new HostAnnouncementTargetOrganization(tenantId, unitId)).ToArray()),
            HttpStatusCode.Created);
        var published = await host.SendAsync(HttpMethod.Post, $"{path}/{created.GetProperty("id").GetGuid():D}/publish",
            new PublishHostAnnouncementRequest(created.GetProperty("version").GetInt32()));
        Assert.AreEqual(AnnouncementStatuses.Published, published.GetProperty("status").GetString());
        Assert.AreEqual(units.Length, published.GetProperty("targetOrganizations").GetArrayLength());
        return published;
    }

    private static void AssertStats(JsonElement stats, long eligible, long read, long unread)
    {
        Assert.AreEqual(eligible, stats.GetProperty("eligibleRecipientCount").GetInt64());
        Assert.AreEqual(read, stats.GetProperty("readCount").GetInt64());
        Assert.AreEqual(unread, stats.GetProperty("unreadCount").GetInt64());
    }

    private sealed class AnnouncementHttp(HttpClient client, string token, string logPath, CancellationToken cancellationToken)
    {
        internal async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null,
            HttpStatusCode expected = HttpStatusCode.OK, bool anonymous = false)
        {
            using var request = new HttpRequestMessage(method, path);
            if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, cancellationToken);
            await NativeApiE2EAssertions.AssertStatusAsync(response, expected, $"Native organization announcement {method} {path}",
                cancellationToken, logPath);
            if (expected == HttpStatusCode.Unauthorized) return default;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return json.RootElement.Clone();
        }
    }
}
