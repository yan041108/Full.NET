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

namespace Full.NET.IntegrationTests.Printing;

/// <summary>系统打印分组修复可重复执行，并保留自定义项与叶子权限。</summary>
[TestClass]
public sealed class PrintingNavigationDirectoryApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Tenant_printing_navigation_survives_legacy_directory_repair(DatabaseProvider provider)
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
        const string selector = "ScopeKey='host' AND TenantId IS NULL AND MenuType='directory' AND RouteName='module-printing' AND Path='/modules/printing'";
        Assert.AreEqual("identity.navigation.read", await PermissionAsync());
        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='printing.templates.read' WHERE " + selector));
        var before = await VersionAsync(); await SyncAsync();
        Assert.AreEqual("identity.navigation.read", await PermissionAsync()); Assert.AreEqual(before + 1, await VersionAsync());
        await SyncAsync(); Assert.AreEqual(before + 1, await VersionAsync());
        // 用户接管的目录以及不属于旧打印权限的系统目录均不被自动重写。
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET IsSystem=0, RequiredPermission='printing.templates.read' WHERE " + selector);
        await SyncAsync(); Assert.AreEqual("printing.templates.read", await PermissionAsync()); Assert.AreEqual(before + 1, await VersionAsync());
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET IsSystem=1, RequiredPermission='identity.users.read' WHERE " + selector);
        await SyncAsync(); Assert.AreEqual("identity.users.read", await PermissionAsync()); Assert.AreEqual(before + 1, await VersionAsync());
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='identity.navigation.read' WHERE " + selector);
        var tenants = await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        var tenant = tenants!.Single(value => value.Identifier == "acme");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id));
        var routes = Flatten((await client.GetFromJsonAsync<NavigationNodeResponse[]>("/api/v1/navigation"))!).Select(item => item.RouteName).ToArray();
        CollectionAssert.Contains(routes, "printing-published-templates"); CollectionAssert.DoesNotContain(routes, "printing-preview");

        Task<string?> PermissionAsync() => db.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE " + selector);
        Task<long> VersionAsync() => db.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + selector);
        async Task SyncAsync()
        {
            using var result = await client.PostAsync("/api/v1/identity/menus/sync-catalog", null);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode, await result.Content.ReadAsStringAsync());
        }
    }

    private static IEnumerable<NavigationNodeResponse> Flatten(IEnumerable<NavigationNodeResponse> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}
