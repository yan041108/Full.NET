using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>目录同步修复已存在的系统分组；租户只获得已授权收件箱，不暴露 Host 公告。</summary>
[TestClass]
public sealed class NotificationNavigationDirectoryApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tenant_inbox_navigation_survives_system_directory_repair(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await db.OpenAsync();
        const string selector = "ScopeKey='host' AND TenantId IS NULL AND IsSystem=1 AND MenuType='directory' AND RouteName='module-notifications'";
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='notifications.announcements.read' WHERE " + selector));
        var before = await db.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector);
        using (var sync = await client.PostAsync("/api/v1/identity/menus/sync-catalog", null))
            Assert.AreEqual(HttpStatusCode.OK, sync.StatusCode, await sync.Content.ReadAsStringAsync());
        Assert.AreEqual("identity.navigation.read", await db.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE " + selector));
        Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector));
        using (var sync = await client.PostAsync("/api/v1/identity/menus/sync-catalog", null))
            Assert.AreEqual(HttpStatusCode.OK, sync.StatusCode, await sync.Content.ReadAsStringAsync());
        Assert.AreEqual(before + 1, await db.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector));
        var tenants = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
        var navigation = await client.GetFromJsonAsync<NavigationNodeResponse[]>("/api/v1/navigation");
        var routes = Flatten(navigation!).Select(item => item.RouteName).ToArray();
        CollectionAssert.Contains(routes, "inbox-messages"); CollectionAssert.DoesNotContain(routes, "host-announcements");
    }

    private static IEnumerable<NavigationNodeResponse> Flatten(IEnumerable<NavigationNodeResponse> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
