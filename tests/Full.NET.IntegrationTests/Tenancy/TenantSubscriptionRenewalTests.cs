using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Tenancy;

/// <summary>真实双库验证续期来源、租户根锁和独立周期；资金事实通过正式模块契约输入。</summary>
[TestClass]
public sealed class TenantSubscriptionRenewalTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Renewal_is_independent_serialized_and_does_not_replace_current_quota_early(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var clock = new RenewalClock();
        using var factory = new FullNetApiFactory(provider, cs, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = (await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default)).Id;
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
            : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);
        await db.OpenAsync();
        await db.ExecuteAsync("UPDATE fn_tenancy_settings SET EntitlementEnforcementPhase='Enforced'");
        await db.ExecuteAsync("UPDATE fn_tenancy_tenant_subscription SET Status='Expired' WHERE TenantId=@Tenant", new { Tenant = tenant });
        foreach (var metric in new[] { TenantQuotaMetricCodes.IdentitySeats, TenantQuotaMetricCodes.FilesStorageBytes })
            await db.ExecuteAsync("""
                INSERT INTO fn_tenancy_quota_metric (Id,TenantId,MetricCode,PeriodKey,LimitValue,UsedValue,ReservedValue,CreatedAtUtc,UpdatedAtUtc,Version)
                SELECT @Id,@Tenant,@Metric,@Period,100,2,1,@Now,@Now,1
                WHERE NOT EXISTS (SELECT 1 FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period)
                """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Metric = metric, Period = TenantQuotaDefaults.PeriodKey, Now = clock.UtcNow });
        await db.ExecuteAsync("UPDATE fn_tenancy_quota_metric SET UsedValue=2,ReservedValue=1 WHERE TenantId=@Tenant", new { Tenant = tenant });
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider; sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var package = await sp.GetRequiredService<HostTenantPackageManagementService>().CreateAsync(new("renew-" + Guid.NewGuid().ToString("N"), "Renewal plan", null));
        Assert.IsTrue(package.IsSuccess, package.Error?.Code);
        var packageId = package.Value!.Id; var merchant = Guid.CreateVersion7();
        var offers = sp.GetRequiredService<TenantPackageOfferService>();
        Assert.IsTrue((await offers.SaveAsync(packageId, new(tenant, merchant, 12500, "CNY", 20, 100000, 30, [], true, 0), default)).IsSuccess);
        var billing = sp.GetRequiredService<TenantSubscriptionBillingService>();
        var originalRequest = new CreatePaidTenantSubscriptionRequest(packageId, 1, clock.UtcNow.AddMinutes(-1), Guid.CreateVersion7());
        var original = (await billing.CreateAsync(tenant, originalRequest, default)).Value!;
        Assert.IsNotNull(original);
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(
            "UPDATE fn_tenancy_subscription_invoice SET RenewalOfSubscriptionId=@Source WHERE Id=@Id",
            new { Source = Guid.CreateVersion7(), Id = original.SubscriptionId }));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(
            "UPDATE fn_tenancy_subscription_invoice SET RenewalOfSubscriptionId=@Source,RenewalSourceVersion=0 WHERE Id=@Id",
            new { Source = Guid.CreateVersion7(), Id = original.SubscriptionId }));
        var previewService = sp.GetRequiredService<TenantSubscriptionRenewalPreviewService>();
        Assert.IsFalse((await previewService.GetAsync(tenant, original.SubscriptionId, default)).IsSuccess, "未支付账单不能作为来源");
        var paid = Receipt(original, clock.UtcNow);
        Assert.IsTrue((await billing.ApplyVerifiedPaymentAsync(paid)).IsSuccess);
        var preview = (await previewService.GetAsync(tenant, original.SubscriptionId, default)).Value!;
        Assert.AreEqual(original.EffectiveToUtc, preview.EffectiveFromUtc); Assert.AreEqual(2, preview.SourceVersion);
        await VerifyPreviewAuthorizationAsync(factory, tenant, original.SubscriptionId);
        Assert.IsTrue((await offers.SaveAsync(packageId, new(tenant, merchant, 25000, "CNY", 1, 1, 60, [], true, 1), default)).IsSuccess);
        preview = (await previewService.GetAsync(tenant, original.SubscriptionId, default)).Value!;
        var request = new CreatePaidTenantSubscriptionRequest(packageId, preview.Offer.Version, preview.EffectiveFromUtc, Guid.CreateVersion7())
            { RenewalOfSubscriptionId = original.SubscriptionId, RenewalSourceVersion = preview.SourceVersion };
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { RenewalSourceVersion = 1 }, default)).IsSuccess);
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { EffectiveFromUtc = preview.EffectiveFromUtc.AddSeconds(1) }, default)).IsSuccess);
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { PackageId = Guid.CreateVersion7() }, default)).IsSuccess);
        Assert.IsFalse((await previewService.GetAsync(Guid.CreateVersion7(), original.SubscriptionId, default)).IsSuccess);
        var cancelled = await sp.GetRequiredService<TenantSubscriptionManagementService>().CancelAsync(tenant, original.SubscriptionId,
            new(preview.SourceVersion), default);
        Assert.IsTrue(cancelled.IsSuccess, cancelled.Error?.Code);
        Assert.IsFalse((await billing.CreateAsync(tenant, originalRequest with { IdempotencyKey = Guid.CreateVersion7(), OfferVersion = 2 }, default)).IsSuccess,
            "仍有效的已付款取消周期必须阻止普通新购");
        Assert.IsFalse((await sp.GetRequiredService<TenantSubscriptionManagementService>().CreateAsync(tenant,
            new(packageId, TenantSubscriptionStatuses.Trial, clock.UtcNow.AddDays(30), clock.UtcNow, clock.UtcNow.AddDays(30)))).IsSuccess,
            "试用入口也不能绕过仍有效的已付款取消周期");
        preview = (await previewService.GetAsync(tenant, original.SubscriptionId, default)).Value!;
        request = request with { RenewalSourceVersion = preview.SourceVersion };
        var results = await Task.WhenAll(CreateInOwnScope(request), CreateInOwnScope(request with { IdempotencyKey = Guid.CreateVersion7() }));
        Assert.AreEqual(1, results.Count(result => result.IsSuccess), "并发只能接受一个未来周期");
        var renewed = results.Single(result => result.IsSuccess).Value!;
        var accepted = results[0].IsSuccess ? request : request with { IdempotencyKey = await db.QuerySingleAsync<Guid>(
            "SELECT RequestId FROM fn_tenancy_subscription_invoice WHERE Id=@Id", new { Id = renewed.SubscriptionId }) };
        Assert.AreNotEqual(original.SubscriptionId, renewed.SubscriptionId); Assert.AreNotEqual(original.PaymentOperationId, renewed.PaymentOperationId);
        Assert.AreEqual(original.SubscriptionId, renewed.RenewalOfSubscriptionId); Assert.AreEqual(preview.SourceVersion, renewed.RenewalSourceVersion);
        Assert.AreEqual(original.EffectiveToUtc, renewed.EffectiveFromUtc); Assert.AreEqual(renewed.EffectiveFromUtc.AddDays(60), renewed.EffectiveToUtc);
        Assert.AreEqual(25000L, renewed.AmountMinor);
        var nextReceipt = Receipt(renewed, clock.UtcNow);
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(nextReceipt)).IsSuccess);
        await AssertQuotaAsync(original.SubscriptionId, 20);
        Assert.IsTrue((await billing.ApplyVerifiedRefundAsync(new(paid, Guid.CreateVersion7(), paid.AmountMinor))).IsSuccess);
        Assert.IsFalse((await previewService.GetAsync(tenant, original.SubscriptionId, default)).IsSuccess);
        Assert.AreEqual(renewed, (await billing.CreateAsync(tenant, accepted, default)).Value, "来源退款后原请求仍返回原账单");
        Assert.IsFalse((await billing.CreateAsync(tenant, accepted with { RenewalSourceVersion = 999 }, default)).IsSuccess);
        clock.UtcNow = renewed.EffectiveFromUtc.AddSeconds(1);
        Assert.IsTrue((await billing.ApplyVerifiedPaymentAsync(nextReceipt)).IsSuccess);
        await AssertQuotaAsync(renewed.SubscriptionId, 1);
        Assert.IsTrue((await billing.ApplyVerifiedRefundAsync(new(paid, Guid.CreateVersion7(), paid.AmountMinor))).IsSuccess);
        await AssertQuotaAsync(renewed.SubscriptionId, 1);
        Assert.AreEqual(renewed, (await billing.CreateAsync(tenant, accepted, default)).Value, "来源到期后原请求仍幂等");

        async Task<Full.NET.Abstractions.Results.Result<TenantSubscriptionPaymentTerms>> CreateInOwnScope(CreatePaidTenantSubscriptionRequest body)
        {
            await using var child = factory.Services.CreateAsyncScope(); child.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            return await child.ServiceProvider.GetRequiredService<TenantSubscriptionBillingService>().CreateAsync(tenant, body, default);
        }
        async Task AssertQuotaAsync(Guid source, long limit)
        {
            var row = await db.QuerySingleAsync<(Guid SourceSubscriptionId, long LimitValue, long UsedValue, long ReservedValue)>(
                "SELECT SourceSubscriptionId,LimitValue,UsedValue,ReservedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
                new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey });
            Assert.AreEqual(source, row.SourceSubscriptionId); Assert.AreEqual(limit, row.LimitValue);
            Assert.AreEqual(2L, row.UsedValue); Assert.AreEqual(1L, row.ReservedValue);
        }
    }

    private static async Task VerifyPreviewAuthorizationAsync(FullNetApiFactory factory, Guid tenant, Guid source)
    {
        using var client = factory.CreateClientForHost("localhost");
        var path = $"/api/v1/tenancy/tenants/{tenant:D}/subscriptions/{source:D}/renewal-preview";
        using var anonymous = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var missing in new[] { "tenancy.tenant_subscriptions.read", "tenancy.tenant_packages.read" })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync([missing]));
            using var forbidden = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(["tenancy.tenant_subscriptions.read", "tenancy.tenant_packages.read"]));
        using var response = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var preview = (await response.Content.ReadFromJsonAsync<TenantSubscriptionRenewalPreviewResponse>())!;
        Assert.AreEqual(tenant, preview.TenantId); Assert.AreEqual(source, preview.SourceSubscriptionId);
    }

    private static TenantSubscriptionPaymentReceipt Receipt(TenantSubscriptionPaymentTerms terms, DateTimeOffset now) =>
        new(terms.SubscriptionId, terms.TenantId, terms.PaymentTenantId, terms.PaymentOperationId, Guid.CreateVersion7(),
            terms.MerchantConfigId, terms.AmountMinor, terms.Currency, "renewal-" + Guid.NewGuid().ToString("N"), now);
    private sealed class RenewalClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
