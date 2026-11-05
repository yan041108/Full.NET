using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>状态条件同时约束双库分页和总数，不能把其他企业同状态历史混入结果。</summary>
[TestClass]
public sealed class TenantSubscriptionStatusFilterApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Status_filter_pages_exact_tenant_history_with_read_only_permission(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connectionString); await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(["tenancy.tenant_subscriptions.read"]));
        var target = await ProvisionAsync(); var other = await ProvisionAsync();
        var path = $"/api/v1/tenancy/tenants/{target.Id:D}/subscriptions";
        var original = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path))!;
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.OpenAsync();
        var now = DateTimeOffset.UtcNow; var expectedIds = new HashSet<Guid>();
        for (var index = 0; index < 27; index++)
        {
            var id = Guid.CreateVersion7(); var tenant = index == 26 ? other.Id : target.Id;
            var status = index < 22 || index == 26 ? "Expired" : index < 25 ? "Cancelled" : "PastDue";
            if (index < 22) expectedIds.Add(id);
            await connection.ExecuteAsync("""
                INSERT INTO fn_tenancy_tenant_subscription
                    (Id,TenantId,PackageId,Status,TrialEndsAtUtc,CurrentPeriodStartUtc,CurrentPeriodEndUtc,CancelledAtUtc,CreatedAtUtc,UpdatedAtUtc,Version)
                VALUES (@Id,@Tenant,NULL,@Status,NULL,@Start,@End,@Cancelled,@Now,@Now,1)
                """, new { Id = id, Tenant = tenant, Status = status, Start = now.AddDays(-60), End = now.AddDays(-30),
                    Cancelled = status == "Cancelled" ? (DateTimeOffset?)now.AddDays(-40) : null, Now = now });
        }
        var first = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path + "?status=Expired&page=1&pageSize=20"))!;
        var second = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path + "?status=Expired&page=2&pageSize=20"))!;
        Assert.AreEqual(22L, first.Total); Assert.AreEqual(first.Total, second.Total);
        Assert.AreEqual(20, first.Items.Count); Assert.AreEqual(2, second.Items.Count);
        Assert.IsTrue(first.Items.Concat(second.Items).All(row => row.TenantId == target.Id && row.Status == "Expired"));
        CollectionAssert.AreEquivalent(expectedIds.ToArray(), first.Items.Concat(second.Items).Select(row => row.Id).ToArray());
        var cancelled = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path + "?status=Cancelled"))!;
        Assert.AreEqual(3L, cancelled.Total); Assert.IsTrue(cancelled.Items.All(row => row.Status == "Cancelled"));
        var outside = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path + "?status=Expired&page=99"))!;
        Assert.AreEqual(22L, outside.Total); Assert.AreEqual(0, outside.Items.Count);
        var all = (await client.GetFromJsonAsync<PagedResult<TenantSubscriptionResponse>>(path))!;
        Assert.AreEqual(original.Total + 26, all.Total);
        using var invalid = await client.GetAsync(path + "?status=" + Uri.EscapeDataString("Active' OR 1=1--"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);

        async Task<TenantSummary> ProvisionAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>(); context.SetHost();
            try
            {
                var identifier = "subscription-filter-" + Guid.NewGuid().ToString("N")[..10];
                var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                    .ProvisionAsync(new ProvisionTenantRequest(identifier, identifier, identifier + ".localhost"));
                Assert.IsTrue(result.IsSuccess, result.Error?.Code); return result.Value!;
            }
            finally { context.Clear(); }
        }
    }
}
