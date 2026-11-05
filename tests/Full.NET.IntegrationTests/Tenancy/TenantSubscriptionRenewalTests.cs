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
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Renewal_is_independent_serialized_and_does_not_replace_current_quota_early(DatabaseProvider provider, bool changePackage)
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
        await VerifyPreviewAuthorizationAsync(factory, tenant, original.SubscriptionId, packageId);
        await VerifyInvoiceHistoryAsync(factory, tenant, original, "Applied");
        var targetPackageId = packageId;
        if (changePackage)
        {
            var target = await sp.GetRequiredService<HostTenantPackageManagementService>().CreateAsync(new("next-" + Guid.NewGuid().ToString("N"), "Next period plan", null));
            Assert.IsTrue(target.IsSuccess); targetPackageId = target.Value!.Id;
            Assert.IsTrue((await offers.SaveAsync(targetPackageId, new(tenant, merchant, 25000, "CNY", 1, 1, 60, [], true, 0), default)).IsSuccess);
            await db.ExecuteAsync("UPDATE fn_tenancy_tenant_package SET IsActive=0,Version=Version+1 WHERE Id=@Id", new { Id = packageId });
            Assert.IsFalse((await previewService.GetAsync(tenant, original.SubscriptionId, default)).IsSuccess, "退出销售的原套餐不能原样续期");
            await VerifyPreviewAuthorizationAsync(factory, tenant, original.SubscriptionId, packageId, targetPackageId);
        }
        else Assert.IsTrue((await offers.SaveAsync(packageId, new(tenant, merchant, 25000, "CNY", 1, 1, 60, [], true, 1), default)).IsSuccess);
        preview = (await previewService.GetAsync(tenant, original.SubscriptionId, default, targetPackageId)).Value!;
        Assert.AreEqual(packageId, preview.SourcePackageId); Assert.AreEqual(targetPackageId, preview.Offer.PackageId);
        var request = new CreatePaidTenantSubscriptionRequest(targetPackageId, preview.Offer.Version, preview.EffectiveFromUtc, Guid.CreateVersion7())
            { RenewalOfSubscriptionId = original.SubscriptionId, RenewalSourceVersion = preview.SourceVersion };
        if (changePackage)
        {
            await db.ExecuteAsync("UPDATE fn_tenancy_tenant_package SET IsActive=0,Version=Version+1 WHERE Id=@Id", new { Id = targetPackageId });
            Assert.IsFalse((await previewService.GetAsync(tenant, original.SubscriptionId, default, targetPackageId)).IsSuccess);
            Assert.IsFalse((await billing.CreateAsync(tenant, request, default)).IsSuccess, "停用目标不能创建下一周期");
            await db.ExecuteAsync("UPDATE fn_tenancy_tenant_package SET IsActive=1,Version=Version+1 WHERE Id=@Id", new { Id = targetPackageId });
        }
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { RenewalSourceVersion = 1 }, default)).IsSuccess);
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { EffectiveFromUtc = preview.EffectiveFromUtc.AddSeconds(1) }, default)).IsSuccess);
        Assert.IsFalse((await billing.CreateAsync(tenant, request with { PackageId = Guid.CreateVersion7() }, default)).IsSuccess);
        Assert.IsFalse((await previewService.GetAsync(Guid.CreateVersion7(), original.SubscriptionId, default)).IsSuccess);
        var cancelled = await sp.GetRequiredService<TenantSubscriptionManagementService>().CancelAsync(tenant, original.SubscriptionId,
            new(preview.SourceVersion), default);
        Assert.IsTrue(cancelled.IsSuccess, cancelled.Error?.Code);
        Assert.IsFalse((await billing.CreateAsync(tenant, new(targetPackageId, preview.Offer.Version, clock.UtcNow, Guid.CreateVersion7()), default)).IsSuccess,
            "仍有效的已付款取消周期必须阻止普通新购");
        Assert.IsFalse((await sp.GetRequiredService<TenantSubscriptionManagementService>().CreateAsync(tenant,
            new(targetPackageId, TenantSubscriptionStatuses.Trial, clock.UtcNow.AddDays(30), clock.UtcNow, clock.UtcNow.AddDays(30)))).IsSuccess,
            "试用入口也不能绕过仍有效的已付款取消周期");
        preview = (await previewService.GetAsync(tenant, original.SubscriptionId, default, targetPackageId)).Value!;
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
        Assert.AreEqual(targetPackageId, renewed.PackageId);
        Assert.AreEqual(packageId, await db.ExecuteScalarAsync<Guid>("SELECT TenantPackageId FROM fn_tenancy_tenant WHERE Id=@Tenant", new { Tenant = tenant }));
        await VerifyInvoiceHistoryAsync(factory, tenant, renewed, "Pending");
        var nextReceipt = Receipt(renewed, clock.UtcNow);
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(nextReceipt)).IsSuccess);
        await AssertQuotaAsync(original.SubscriptionId, 20);
        Assert.IsTrue((await billing.ApplyVerifiedRefundAsync(new(paid, Guid.CreateVersion7(), paid.AmountMinor))).IsSuccess);
        Assert.IsFalse((await previewService.GetAsync(tenant, original.SubscriptionId, default)).IsSuccess);
        await VerifyInvoiceHistoryAsync(factory, tenant, original, "Refunded");
        Assert.AreEqual(renewed, (await billing.CreateAsync(tenant, accepted, default)).Value, "来源退款后原请求仍返回原账单");
        Assert.IsFalse((await billing.CreateAsync(tenant, accepted with { RenewalSourceVersion = 999 }, default)).IsSuccess);
        clock.UtcNow = renewed.EffectiveFromUtc.AddSeconds(1);
        Assert.IsTrue((await billing.ApplyVerifiedPaymentAsync(nextReceipt)).IsSuccess);
        Assert.AreEqual(targetPackageId, await db.ExecuteScalarAsync<Guid>("SELECT TenantPackageId FROM fn_tenancy_tenant WHERE Id=@Tenant", new { Tenant = tenant }));
        await AssertQuotaAsync(renewed.SubscriptionId, 1);
        Assert.IsTrue((await billing.ApplyVerifiedRefundAsync(new(paid, Guid.CreateVersion7(), paid.AmountMinor))).IsSuccess);
        await AssertQuotaAsync(renewed.SubscriptionId, 1);
        Assert.AreEqual(renewed, (await billing.CreateAsync(tenant, accepted, default)).Value, "来源到期后原请求仍幂等");
        await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=0,LifecycleStatus='Suspended' WHERE Id=@Tenant", new { Tenant = tenant });
        var inactiveHistory = await sp.GetRequiredService<TenantSubscriptionInvoiceQueryService>().GetAsync(tenant, original.SubscriptionId, default);
        Assert.IsTrue(inactiveHistory.IsSuccess); Assert.AreEqual("Refunded", inactiveHistory.Value!.Phase);
        Assert.IsFalse((await billing.GetPaymentTermsAsync(tenant, renewed.SubscriptionId)).IsSuccess, "只读回读不能放宽停用企业付款资格");

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

    private static async Task VerifyInvoiceHistoryAsync(FullNetApiFactory factory, Guid tenant, TenantSubscriptionPaymentTerms terms, string phase)
    {
        using var client = factory.CreateClientForHost("localhost");
        var path = $"/api/v1/tenancy/tenants/{tenant:D}/subscriptions/{terms.SubscriptionId:D}/invoice";
        using var anonymous = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(["tenancy.tenant_subscriptions.manage"]));
        using var denied = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(["tenancy.tenant_subscriptions.read"]));
        using var response = await client.GetAsync(path); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(System.Text.Json.JsonValueKind.String, json.RootElement.GetProperty("amountMinor").ValueKind);
        foreach (var hidden in new[] { "requestHash", "requestId", "paymentOperationId", "merchantConfigId", "providerTransactionId" })
            Assert.IsFalse(json.RootElement.TryGetProperty(hidden, out _), hidden);
        var invoice = (await response.Content.ReadFromJsonAsync<TenantSubscriptionInvoiceResponse>())!;
        Assert.AreEqual(terms.SubscriptionId, invoice.SubscriptionId); Assert.AreEqual(tenant, invoice.TenantId);
        Assert.AreEqual(terms.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture), invoice.AmountMinor);
        Assert.AreEqual(terms.EffectiveFromUtc, invoice.EffectiveFromUtc); Assert.AreEqual(terms.EffectiveToUtc, invoice.EffectiveToUtc);
        Assert.AreEqual(terms.RenewalOfSubscriptionId, invoice.RenewalOfSubscriptionId); Assert.AreEqual(terms.RenewalSourceVersion, invoice.RenewalSourceVersion);
        Assert.AreEqual(phase, invoice.Phase);
        using var wrongTenant = await client.GetAsync($"/api/v1/tenancy/tenants/{Guid.CreateVersion7():D}/subscriptions/{terms.SubscriptionId:D}/invoice");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongTenant.StatusCode);
        using var missing = await client.GetAsync($"/api/v1/tenancy/tenants/{tenant:D}/subscriptions/{Guid.CreateVersion7():D}/invoice");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        using var emptyTenant = await client.GetAsync($"/api/v1/tenancy/tenants/{Guid.Empty:D}/subscriptions/{terms.SubscriptionId:D}/invoice");
        Assert.AreEqual(HttpStatusCode.NotFound, emptyTenant.StatusCode);
        using var emptySubscription = await client.GetAsync($"/api/v1/tenancy/tenants/{tenant:D}/subscriptions/{Guid.Empty:D}/invoice");
        Assert.AreEqual(HttpStatusCode.NotFound, emptySubscription.StatusCode);
    }

    private static async Task VerifyPreviewAuthorizationAsync(FullNetApiFactory factory, Guid tenant, Guid source, Guid sourcePackageId, Guid? targetPackageId = null)
    {
        using var client = factory.CreateClientForHost("localhost");
        var path = $"/api/v1/tenancy/tenants/{tenant:D}/subscriptions/{source:D}/renewal-preview";
        if (targetPackageId is { } target) path += $"?targetPackageId={target:D}";
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
        Assert.AreEqual(sourcePackageId, preview.SourcePackageId); Assert.AreEqual(targetPackageId ?? sourcePackageId, preview.Offer.PackageId);
    }

    private static TenantSubscriptionPaymentReceipt Receipt(TenantSubscriptionPaymentTerms terms, DateTimeOffset now) =>
        new(terms.SubscriptionId, terms.TenantId, terms.PaymentTenantId, terms.PaymentOperationId, Guid.CreateVersion7(),
            terms.MerchantConfigId, terms.AmountMinor, terms.Currency, "renewal-" + Guid.NewGuid().ToString("N"), now);
    private sealed class RenewalClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
}
