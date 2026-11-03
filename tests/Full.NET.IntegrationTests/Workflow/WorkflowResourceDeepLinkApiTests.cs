using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>深链精确消息读取的真实双库边界；测试数据不代替运行时收件人与可信租户守卫。</summary>
[TestClass]
public sealed class WorkflowResourceDeepLinkApiTests
{
    [TestMethod]
    public Task Resource_message_lookup_with_sql_server() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task Resource_message_lookup_with_mysql() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            settingsOverrides: new Dictionary<string, string?> { ["Identity:SessionLoginPolicy"] = "AllowMultiple" });
        await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var admin = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        var me = await host.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me"); Assert.IsNotNull(me);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        using var tenantClient = factory.CreateClientForHost("localhost");
        tenantClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var tenantSession = await IntegrationTestAuthHelper.LoginAsHostUserAsync(tenantClient, "admin");
        tenantClient.DefaultRequestHeaders.Authorization = new("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient, tenantSession, tenant.Id));
        using var outsider = factory.CreateClientForHost("localhost");
        outsider.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([InboxPermissions.Read]));
        using var forbidden = factory.CreateClientForHost("localhost");
        forbidden.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([InboxPermissions.MarkRead]));
        using var anonymous = factory.CreateClientForHost("localhost");
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));

        foreach (var scope in new Guid?[] { null, tenant.Id })
        {
            var id = Guid.CreateVersion7(); var resource = Guid.CreateVersion7();
            var scopeKey = scope is null ? "host" : $"tenant:{scope:N}";
            const string insert = """
                INSERT INTO fn_notifications_inbox_message
                  (Id,TenantId,RecipientUserId,Title,Content,Status,ReadAtUtc,CreatedAtUtc,CreatedByUserId,ScopeKey,TenantScopeKey,IntentId,DeepLinkJson)
                VALUES (@Id,@TenantId,@UserId,'deep link','private body','unread',NULL,@CreatedAtUtc,NULL,@ScopeKey,@TenantScopeKey,NULL,@DeepLinkJson)
                """;
            var link = JsonSerializer.Serialize(new { resourceTypeKey = "workflow_todo", resourceId = resource });
            await db.ExecuteAsync(insert, new { Id = id, TenantId = scope, UserId = me.Id, CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                ScopeKey = scope is null ? "host" : "tenant", TenantScopeKey = scopeKey, DeepLinkJson = link });
            // 将目标放到前100条之外，验证详情不再依赖分页查找；无额外业务事件或权限写入。
            await db.ExecuteAsync(insert, Enumerable.Range(0, 101).Select(_ => new { Id = Guid.CreateVersion7(), TenantId = scope,
                UserId = me.Id, CreatedAtUtc = DateTimeOffset.UtcNow, ScopeKey = scope is null ? "host" : "tenant",
                TenantScopeKey = scopeKey, DeepLinkJson = (string?)null }));
            var recipient = scope is null ? host : tenantClient;
            var wrongScope = scope is null ? tenantClient : host;
            var path = $"/api/v1/notifications/my-inbox-messages/{id:D}";
            var before = await recipient.GetFromJsonAsync<InboxUnreadCountResponse>("/api/v1/notifications/my-inbox-messages/unread-count");
            var value = await recipient.GetFromJsonAsync<InboxMessageResponse>(path); Assert.IsNotNull(value);
            Assert.AreEqual(id, value.Id); Assert.AreEqual("private body", value.Content); Assert.AreEqual("unread", value.Status);
            Assert.IsNull(value.ReadAtUtc); Assert.IsNotNull(value.DeepLink); Assert.AreEqual(resource, value.DeepLink.ResourceId);
            var after = await recipient.GetFromJsonAsync<InboxUnreadCountResponse>("/api/v1/notifications/my-inbox-messages/unread-count");
            Assert.AreEqual(before!.UnreadCount, after!.UnreadCount, "GET不能隐式标记已读或改变徽标。");
            foreach (var (client, status) in new[] { (wrongScope, HttpStatusCode.NotFound), (outsider, HttpStatusCode.NotFound),
                (forbidden, HttpStatusCode.Forbidden), (anonymous, HttpStatusCode.Unauthorized) })
            {
                using var denied = await client.GetAsync(path); Assert.AreEqual(status, denied.StatusCode);
                Assert.IsFalse((await denied.Content.ReadAsStringAsync()).Contains("private body", StringComparison.Ordinal));
            }
            await db.ExecuteAsync("UPDATE fn_notifications_inbox_message SET RevokedAtUtc=@Now,RevocationReason='revoked' WHERE Id=@Id",
                new { Now = DateTimeOffset.UtcNow, Id = id });
            var revoked = await recipient.GetFromJsonAsync<InboxMessageResponse>(path); Assert.IsNotNull(revoked);
            Assert.IsNotNull(revoked.RevokedAtUtc); Assert.AreEqual(string.Empty, revoked.Content); Assert.IsNull(revoked.DeepLink);
        }
        using var missing = await host.GetAsync($"/api/v1/notifications/my-inbox-messages/{Guid.CreateVersion7():D}");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        // 只导出本次真实Host生成的协议；规范快照合入由双库一致性检查另行完成。
        if (Environment.GetEnvironmentVariable("FULLNET_CLIENT_OPENAPI_EXPORT_PATH") is { Length: > 0 } export)
            await File.WriteAllTextAsync(export, await host.GetStringAsync("/openapi/v1.json"));
    }
}
