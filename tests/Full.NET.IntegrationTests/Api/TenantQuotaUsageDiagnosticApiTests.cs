using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>正式双库读取真实成员/资源元数据计数；元数据夹具不冒充文件上传旅程。</summary>
[TestClass]
public sealed class TenantQuotaUsageDiagnosticApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Diagnostic_reads_owner_counts_with_exact_target_and_never_repairs(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs); await factory.InitializeAsync();
        using var contextClient = factory.CreateClientForHost("acme.localhost");
        var tenant = (await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(contextClient, default)).Id;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var code = "quota-diag-" + Guid.NewGuid().ToString("N")[..10];
        var provision = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new(code, code, code + ".localhost"));
        Assert.IsTrue(provision.IsSuccess, provision.Error?.Code); var other = provision.Value!.Id;
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await db.OpenAsync();
        var memberCount = await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_identity_tenant_member WHERE TenantId=@Tenant AND Status=@Status",
            new { Tenant = tenant, Status = TenantMemberStatuses.Active });
        var priorBytes = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(SizeBytes),0) FROM fn_files_tenant_resource_file WHERE TenantId=@Tenant AND StatusKey='ready'", new { Tenant = tenant });
        const long largeBytes = 9007199254740993L;
        await SeedResourceAsync(tenant, largeBytes, "ready"); await SeedResourceAsync(tenant, 77, "pending"); await SeedResourceAsync(other, 1999, "ready");
        var bytes = priorBytes + largeBytes;
        await ConfigureMetricAsync(tenant, TenantQuotaMetricCodes.IdentitySeats, memberCount + 2);
        await ConfigureMetricAsync(tenant, TenantQuotaMetricCodes.FilesStorageBytes, bytes + 7);
        await ConfigureMetricAsync(other, TenantQuotaMetricCodes.IdentitySeats, 77);
        var baseline = await db.QuerySingleAsync<(long UsedValue, long ReservedValue, int Version)>(
            "SELECT UsedValue,ReservedValue,Version FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey });
        using var client = factory.CreateClientForHost("localhost");
        var path = $"/api/v1/tenancy/tenants/{tenant:D}/quota/usage-diagnostic";
        using var anonymous = await client.GetAsync(path + "?metricCode=identity.seats"); Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var permission in new[] { "tenancy.tenant_quota.read", "tenancy.tenant_quota.reconcile_usage_baseline" })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([permission]));
            using var denied = await client.GetAsync(path + "?metricCode=identity.seats"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(["tenancy.tenant_quota.read", "tenancy.tenant_quota.reconcile_usage_baseline"]));
        using var response = await client.GetAsync(path + "?metricCode=identity.seats"); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var seats = (await response.Content.ReadFromJsonAsync<TenantQuotaUsageDiagnosticResponse>())!;
        Assert.AreEqual(tenant, seats.TenantId); Assert.AreEqual("Different", seats.State);
        Assert.AreEqual(memberCount.ToString(CultureInfo.InvariantCulture), seats.AuthoritativeUsedValue); Assert.AreEqual("-2", seats.ObservedDifference);
        using var storageResponse = await client.GetAsync(path + "?metricCode=files.storage_bytes"); Assert.AreEqual(HttpStatusCode.OK, storageResponse.StatusCode);
        using var json = JsonDocument.Parse(await storageResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(JsonValueKind.String, json.RootElement.GetProperty("authoritativeUsedValue").ValueKind);
        var storage = (await storageResponse.Content.ReadFromJsonAsync<TenantQuotaUsageDiagnosticResponse>())!;
        Assert.AreEqual(bytes.ToString(CultureInfo.InvariantCulture), storage.AuthoritativeUsedValue); Assert.AreEqual("-7", storage.ObservedDifference);
        Assert.AreEqual(bytes + 7, long.Parse(storage.BookUsedValue!, CultureInfo.InvariantCulture));
        Assert.AreEqual(baseline, await db.QuerySingleAsync<(long UsedValue, long ReservedValue, int Version)>(
            "SELECT UsedValue,ReservedValue,Version FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey }));
        Assert.AreEqual(77L, await db.ExecuteScalarAsync<long>("SELECT UsedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = other, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey }));
        await db.ExecuteAsync("UPDATE fn_tenancy_quota_metric SET ReservedValue=1,Version=Version+1 WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey });
        var inProgress = (await client.GetFromJsonAsync<TenantQuotaUsageDiagnosticResponse>(path + "?metricCode=identity.seats"))!;
        Assert.AreEqual("InProgress", inProgress.State); Assert.IsNull(inProgress.ObservedDifference);
        await db.ExecuteAsync("DELETE FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = other, Metric = TenantQuotaMetricCodes.FilesStorageBytes, Period = TenantQuotaDefaults.PeriodKey });
        var missing = (await client.GetFromJsonAsync<TenantQuotaUsageDiagnosticResponse>($"/api/v1/tenancy/tenants/{other:D}/quota/usage-diagnostic?metricCode=files.storage_bytes"))!;
        Assert.AreEqual("Unconfigured", missing.State); Assert.IsNull(missing.BookUsedValue); Assert.IsNull(missing.ObservedDifference); Assert.AreEqual("1999", missing.AuthoritativeUsedValue);
        using var unsupported = await client.GetAsync(path + "?metricCode=custom.metric"); Assert.AreEqual(HttpStatusCode.BadRequest, unsupported.StatusCode);
        foreach (var absent in new[] { Guid.Empty, Guid.CreateVersion7() })
        {
            using var notFound = await client.GetAsync($"/api/v1/tenancy/tenants/{absent:D}/quota/usage-diagnostic?metricCode=identity.seats");
            Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
        }

        async Task SeedResourceAsync(Guid target, long size, string state)
        {
            var id = Guid.CreateVersion7();
            await db.ExecuteAsync("""
                INSERT INTO fn_files_tenant_resource_file
                  (Id,TenantId,OwnerModuleKey,ResourceId,OriginalFileName,ContentType,SizeBytes,ContentHash,ProviderKey,StorageKey,StatusKey,CreatedByUserId,CreatedAtUtc)
                VALUES (@Id,@Tenant,'diagnostic',@Resource,'diagnostic.bin','application/octet-stream',@Size,'','local',@Key,@State,@Actor,@Now)
                """, new { Id = id, Tenant = target, Resource = Guid.CreateVersion7(), Size = size, Key = "diagnostic/" + id.ToString("N"), State = state, Actor = Guid.CreateVersion7(), Now = DateTimeOffset.UtcNow });
        }
        async Task ConfigureMetricAsync(Guid target, string metric, long used)
        {
            await db.ExecuteAsync("""
                INSERT INTO fn_tenancy_quota_metric (Id,TenantId,MetricCode,PeriodKey,LimitValue,UsedValue,ReservedValue,CreatedAtUtc,UpdatedAtUtc,Version)
                SELECT @Id,@Tenant,@Metric,@Period,@Limit,@Used,0,@Now,@Now,1
                WHERE NOT EXISTS (SELECT 1 FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period)
                """, new { Id = Guid.CreateVersion7(), Tenant = target, Metric = metric, Period = TenantQuotaDefaults.PeriodKey, Limit = long.MaxValue, Used = used, Now = DateTimeOffset.UtcNow });
            await db.ExecuteAsync("UPDATE fn_tenancy_quota_metric SET LimitValue=@Limit,UsedValue=@Used,ReservedValue=0,Version=Version+1 WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
                new { Tenant = target, Metric = metric, Period = TenantQuotaDefaults.PeriodKey, Limit = long.MaxValue, Used = used });
        }
    }
}
