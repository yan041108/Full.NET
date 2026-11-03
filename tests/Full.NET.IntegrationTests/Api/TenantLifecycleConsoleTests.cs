using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>两个停止入口并发时仍保留一家活动企业，终态不能借运营启用恢复。</summary>
[TestClass]
public sealed class TenantLifecycleConsoleTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Lifecycle_console_enforces_read_CAS_terminal_and_concurrent_last_active_boundaries(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var token = await factory.CreateHostAccessTokenAsync(["tenancy.host_tenants.read", "tenancy.tenant_lifecycle.read",
            "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate", "tenancy.tenant_lifecycle.close",
            "tenancy.tenants.enable", "tenancy.tenants.disable"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var page = await client.GetFromJsonAsync<PagedResult<TenantSummary>>("/api/v1/tenancy/tenants?pageSize=100");
        var a = page!.Items.First(row => row.Identifier == "acme");
        TenantSummary b;
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            setup.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var identifier = "lifecycle-" + Guid.NewGuid().ToString("N")[..10];
            var created = await setup.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest(identifier, "Lifecycle second tenant", identifier + ".localhost"));
            Assert.IsTrue(created.IsSuccess, created.Error?.Code);
            b = created.Value!;
        }
        // 独立测试库固定两家活动企业，防止其他 Overlay 行使最后一名场景失去约束。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.lifecycle.isolate_active_pair", """
                UPDATE fn_tenancy_tenant SET IsActive=0,LifecycleStatus='Suspended',Version=Version+1
                WHERE Id<>@A AND Id<>@B
                """, SqlDataScope.HostOnly), new { A = a.Id, B = b.Id });
        }
        using (var reader = factory.CreateClientForHost("localhost"))
        {
            reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                await factory.CreateHostAccessTokenAsync(["tenancy.tenant_lifecycle.read"]));
            using var read = await reader.GetAsync(Path(a.Id) + "/lifecycle");
            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
            using var forbidden = await reader.PostAsJsonAsync(Path(a.Id) + "/suspend", new { a.Version });
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        a = await ReadAsync(a.Id);
        var originalVersion = a.Version;
        using (var response = await client.PostAsJsonAsync(Path(a.Id) + "/suspend", new { a.Version }))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            a = (await response.Content.ReadFromJsonAsync<TenantSummary>())!;
        }
        Assert.AreEqual("Suspended", a.LifecycleStatus);
        using (var blocked = factory.CreateClientForHost(a.Domain))
        using (var response = await blocked.GetAsync("/api/v1/tenancy/current")) Assert.IsFalse(response.IsSuccessStatusCode);
        using (var bypass = await client.PostAsync(Path(a.Id) + "/enable", null)) Assert.AreEqual(HttpStatusCode.UnprocessableEntity, bypass.StatusCode);
        using (var stale = await client.PostAsJsonAsync(Path(a.Id) + "/reactivate", new { version = originalVersion })) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        using (var response = await client.PostAsJsonAsync(Path(a.Id) + "/reactivate", new { a.Version }))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            a = (await response.Content.ReadFromJsonAsync<TenantSummary>())!;
        }
        b = await ReadAsync(b.Id);
        var stopping = await Task.WhenAll(client.PostAsJsonAsync(Path(a.Id) + "/suspend", new { a.Version }), client.PostAsync(Path(b.Id) + "/disable", null));
        try
        {
            Assert.AreEqual(1, stopping.Count(response => response.IsSuccessStatusCode));
            Assert.AreEqual(1, stopping.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
        }
        finally { foreach (var response in stopping) response.Dispose(); }
        a = await ReadAsync(a.Id); b = await ReadAsync(b.Id);
        Assert.AreEqual(1, new[] { a, b }.Count(row => row.IsActive));
        var inactive = a.IsActive ? b : a;
        var active = a.IsActive ? a : b;
        using (var response = await client.PostAsJsonAsync(Path(inactive.Id) + "/close", new { inactive.Version }))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            inactive = (await response.Content.ReadFromJsonAsync<TenantSummary>())!;
        }
        Assert.AreEqual("Closed", inactive.LifecycleStatus);
        using (var bypass = await client.PostAsync(Path(inactive.Id) + "/enable", null)) Assert.AreEqual(HttpStatusCode.UnprocessableEntity, bypass.StatusCode);
        using (var bypass = await client.PostAsJsonAsync(Path(inactive.Id) + "/reactivate", new { inactive.Version })) Assert.AreEqual(HttpStatusCode.UnprocessableEntity, bypass.StatusCode);
        foreach (var operation in new[] { "suspend", "close", "disable" })
        {
            using var rejected = operation == "disable" ? await client.PostAsync(Path(active.Id) + "/disable", null)
                : await client.PostAsJsonAsync(Path(active.Id) + "/" + operation, new { active.Version });
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, rejected.StatusCode, operation);
        }
        Assert.IsTrue((await ReadAsync(active.Id)).IsActive);

        static string Path(Guid id) => $"/api/v1/tenancy/tenants/{id:D}";
        async Task<TenantSummary> ReadAsync(Guid id) => (await client.GetFromJsonAsync<TenantSummary>(Path(id) + "/lifecycle"))!;
    }
}
