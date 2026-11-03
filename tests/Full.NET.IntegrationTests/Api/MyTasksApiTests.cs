using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Api;

/// <summary>生产端点在正式双库中先隔离主体再分页；同时间顺序以各库的 UUID 比较规则为准。</summary>
[TestClass]
public sealed class MyTasksApiTests
{
    [TestMethod]
    public Task Mine_pages_only_current_actor_and_context_bound_cursors_with_sql_server() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task Mine_pages_only_current_actor_and_context_bound_cursors_with_mysql() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync();
        using var host = factory.CreateClientForHost("localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"); host.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var tenant = (await host.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var userName = "tasks." + Guid.CreateVersion7().ToString("N");
        var user = await factory.CreateHostIdentityAsync(userName, ["identity.navigation.read", "tenancy.tenants.read", "tenancy.tenants.switch",
            "platform.my_tasks.read", "import_export.import_tasks.read", "reporting.export_tasks.read"], password: FullNetApiFactory.TestPassword);
        var email = userName + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, userName);
        var otherName = "tasks-other-" + Guid.CreateVersion7().ToString("N")[..12];
        var otherTenant = await SendAsync(host, HttpMethod.Post, "/api/v1/tenancy/tenants", new ProvisionTenantRequest(otherName, otherName, otherName + ".localhost"), HttpStatusCode.Created);
        var otherTenantId = otherTenant.GetProperty("id").GetGuid();
        await using (var navigationDb = Open(provider, connectionString))
        {
            await navigationDb.OpenAsync();
            const string selector = "ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory' AND RouteName='module-platform' AND Path='/modules/platform'";
            Assert.AreEqual(1, await navigationDb.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='platform.release_notes.host_manage.read' WHERE " + selector));
            var before = await navigationDb.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector);
            await SendAsync(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
            await SendAsync(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
            Assert.AreEqual(before + 1, await navigationDb.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector));
            Assert.AreEqual("identity.navigation.read", await navigationDb.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE " + selector));
        }
        using var member = factory.CreateClientForHost("localhost");
        // Host 模式必须显式拒绝本人租户查询，不回退全局数据。
        foreach (var path in Paths) await SendAsync(host, HttpMethod.Get, path + "/mine", null, HttpStatusCode.Forbidden);
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, token, tenant.Id));
        var invitation = await SendAsync(host, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
        var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, userName); member.DefaultRequestHeaders.Authorization = new("Bearer", memberToken);
        await SendAsync(member, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, memberToken, tenant.Id));
        await using var database = Open(provider, connectionString); await database.OpenAsync();
        await MyTasksQueryEvidence.SeedAsync(database, provider, tenant.Id, otherTenantId, user.UserId);
        var cursors = new List<string>();
        foreach (var path in Paths)
        {
            var table = path.Contains("import-export", StringComparison.Ordinal) ? "fn_import_export_task" : "fn_reporting_export_task";
            var expected = (await database.QueryAsync<Guid>($"SELECT Id FROM {table} WHERE TenantId=@TenantId AND RequestedByUserId=@UserId ORDER BY CreatedAtUtc DESC,Id DESC", new { TenantId = tenant.Id, UserId = user.UserId })).ToArray();
            var seen = new List<Guid>(); string? cursor = null;
            do
            {
                var result = await SendAsync(member, HttpMethod.Get, path + "/mine?pageSize=17" + (cursor is null ? "" : "&cursor=" + cursor), null, HttpStatusCode.OK);
                Assert.IsFalse(result.TryGetProperty("total", out _)); var items = result.GetProperty("items"); Assert.IsTrue(items.GetArrayLength() <= 17);
                foreach (var row in items.EnumerateArray())
                {
                    Assert.AreEqual(user.UserId, row.GetProperty("requestedByUserId").GetGuid());
                    foreach (var forbidden in new[] { "previewRowsJson", "executionRowsJson", "actorSessionBindingJson", "actorPermissionCodesJson", "parametersJson", "parameters" })
                        Assert.IsFalse(row.TryGetProperty(forbidden, out _));
                    seen.Add(row.GetProperty("id").GetGuid());
                }
                cursor = result.GetProperty("nextCursor").GetString(); Assert.AreEqual(cursor is not null, result.GetProperty("hasMore").GetBoolean());
                if (cursor is not null && cursors.Count < 2) cursors.Add(cursor);
            } while (cursor is not null);
            CollectionAssert.AreEqual(expected, seen.ToArray()); Assert.AreEqual(100, seen.Count);
            var status = table.Contains("import", StringComparison.Ordinal) ? "execution_failed" : "failed";
            var filtered = await SendAsync(member, HttpMethod.Get, path + "/mine?pageSize=1000&statusKey=" + status + "&requestedByUserId=" + Guid.CreateVersion7(), null, HttpStatusCode.OK);
            Assert.AreEqual(50, filtered.GetProperty("items").GetArrayLength());
            Assert.IsTrue(filtered.GetProperty("items").EnumerateArray().All(row => row.GetProperty("statusKey").GetString() == status));
            foreach (var bad in new[] { "cursor=bad", "statusKey=bogus", "statusKey=" + status + "&cursor=" + cursors[0] })
                await SendAsync(member, HttpMethod.Get, path + "/mine?" + bad, null, HttpStatusCode.BadRequest);
            var legacy = await SendAsync(host, HttpMethod.Get, path + "?page=1&pageSize=1", null, HttpStatusCode.OK);
            Assert.AreEqual(1000, legacy.GetProperty("total").GetInt32());
        }
        // 报表端点不能接纳导入来源的位置，即使用户、租户完全相同。
        await SendAsync(member, HttpMethod.Get, Paths[1] + "/mine?cursor=" + cursors[0], null, HttpStatusCode.BadRequest);
        var nav = await SendAsync(member, HttpMethod.Get, "/api/v1/navigation", null, HttpStatusCode.OK);
        StringAssert.Contains(nav.ToString(), "my-tasks");
        foreach (var path in Paths) await SendAsync(host, HttpMethod.Get, path + "/mine?cursor=" + cursors[0], null, HttpStatusCode.BadRequest);
        host.DefaultRequestHeaders.Authorization = null; token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, token, otherTenantId));
        foreach (var path in Paths)
        {
            var empty = await SendAsync(host, HttpMethod.Get, path + "/mine", null, HttpStatusCode.OK);
            Assert.AreEqual(0, empty.GetProperty("items").GetArrayLength());
            await SendAsync(host, HttpMethod.Get, path + "/mine?cursor=" + cursors[0], null, HttpStatusCode.BadRequest);
        }
        using var denied = factory.CreateClientForHost("localhost");
        var deniedUser = "tasks.denied." + Guid.CreateVersion7().ToString("N");
        await factory.CreateHostIdentityAsync(deniedUser, ["platform.my_tasks.read"], password: FullNetApiFactory.TestPassword);
        denied.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(denied, deniedUser));
        foreach (var path in Paths) await SendAsync(denied, HttpMethod.Get, path + "/mine", null, HttpStatusCode.Forbidden);
        await MyTasksQueryEvidence.MeasureAsync(database, provider, connectionString, tenant.Id, user.UserId);
    }

    private static readonly string[] Paths = ["/api/v1/import-export/tasks", "/api/v1/reporting/export-tasks"];
    internal static DbConnection Open(DatabaseProvider provider, string connection) => provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
        : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var message = new HttpRequestMessage(method, path); if (body is not null) message.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(message); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
