using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Payments;

public sealed partial class SubscriptionBillingRecoveryTests
{
    internal static async Task VerifyPaidTermBoundaryAsync(DatabaseProvider provider, bool downgrade)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var clock = new BillingBoundaryClock();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = (await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default)).Id;
        await using var connection = Open(provider, connectionString);
        await connection.OpenAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var merchant = Guid.CreateVersion7();
        var entitlement = Guid.CreateVersion7();
        var code = $"boundary-{entitlement:N}";
        var now = clock.UtcNow.AddMinutes(-1);
        await connection.ExecuteAsync("UPDATE fn_tenancy_settings SET EntitlementEnforcementPhase='Enforced'");
        await connection.ExecuteAsync("UPDATE fn_tenancy_tenant_subscription SET Status='Expired' WHERE TenantId=@Tenant", new { Tenant = tenant });
        foreach (var metric in new[] { TenantQuotaMetricCodes.IdentitySeats, TenantQuotaMetricCodes.FilesStorageBytes })
            await connection.ExecuteAsync("""
                INSERT INTO fn_tenancy_quota_metric (Id,TenantId,MetricCode,PeriodKey,LimitValue,UsedValue,ReservedValue,CreatedAtUtc,UpdatedAtUtc,Version)
                SELECT @Id,@Tenant,@Metric,@Period,100,0,0,@Now,@Now,1
                WHERE NOT EXISTS (SELECT 1 FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period)
                """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Metric = metric, Period = TenantQuotaDefaults.PeriodKey, Now = now });
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_entitlement_catalog (Id,Code,Name,EntitlementType,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Entitlement,@Code,'Boundary feature','Feature',1,@Now,@Now,1)
            """, new { Entitlement = entitlement, Code = code, Now = now });
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_tenant_entitlement_binding
            (Id,TenantId,EntitlementId,EffectiveFromUtc,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,@Entitlement,@Now,@Now,@Now,1)
            """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Entitlement = entitlement, Now = now });

        var billing = services.GetRequiredService<TenantSubscriptionBillingService>();
        var initialPackage = await CreatePackageAsync("current", 20, 100000, [entitlement]);
        var created = await billing.CreateAsync(tenant, new(initialPackage, 1, now, Guid.CreateVersion7()), default);
        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        var original = created.Value!;
        var applied = await billing.ApplyVerifiedPaymentAsync(Paid(original));
        Assert.IsTrue(applied.IsSuccess, applied.Error?.Code);

        var subscriptions = services.GetRequiredService<TenantSubscriptionManagementService>();
        TenantSubscriptionPaymentReceipt? nextPayment = null;
        if (downgrade)
        {
            var lower = await CreatePackageAsync("lower", 1, 1, []);
            var preview = await services.GetRequiredService<TenantSubscriptionRenewalPreviewService>()
                .GetAsync(tenant, original.SubscriptionId, default, lower);
            Assert.IsTrue(preview.IsSuccess, preview.Error?.Code);
            var request = new CreatePaidTenantSubscriptionRequest(lower, preview.Value!.Offer.Version,
                preview.Value.EffectiveFromUtc, Guid.CreateVersion7())
            { RenewalOfSubscriptionId = original.SubscriptionId, RenewalSourceVersion = preview.Value.SourceVersion };
            var attempts = await Task.WhenAll(CreateRenewalAsync(request), CreateRenewalAsync(request with { IdempotencyKey = Guid.CreateVersion7() }));
            Assert.AreEqual(1, attempts.Count(result => result.IsSuccess), "同一活动周期只能新增一个后续周期。");
            Assert.AreEqual(TenancyErrorCodes.SubscriptionStatusInvalid, attempts.Single(result => !result.IsSuccess).Error?.Code);
            nextPayment = Paid(attempts.Single(result => result.IsSuccess).Value!);
            var early = await billing.ApplyVerifiedPaymentAsync(nextPayment);
            Assert.IsFalse(early.IsSuccess, "下一周期不得提前覆盖当前额度。");
        }
        else
        {
            var cancelled = await subscriptions.CancelAsync(tenant, original.SubscriptionId, new(2));
            Assert.IsTrue(cancelled.IsSuccess, cancelled.Error?.Code);
        }

        clock.UtcNow = original.EffectiveToUtc.AddSeconds(-1);
        var memberId = await VerifySeatConsumerAsync(services, connection, tenant, allowed: true);
        var file = await VerifyStorageConsumerAsync(services, connection, tenant, allowed: true);
        var usageBefore = (await ReadConsumerQuotasAsync()).ToArray();
        Assert.AreEqual(2L, await ReadActiveFeaturesAsync());

        clock.UtcNow = original.EffectiveToUtc;
        if (nextPayment is not null)
        {
            var renewed = await billing.ApplyVerifiedPaymentAsync(nextPayment);
            Assert.IsTrue(renewed.IsSuccess, renewed.Error?.Code);
            var replay = await billing.ApplyVerifiedPaymentAsync(nextPayment);
            Assert.IsTrue(replay.IsSuccess, replay.Error?.Code);
        }
        await VerifySeatConsumerAsync(services, connection, tenant, allowed: false);
        await VerifyStorageConsumerAsync(services, connection, tenant, allowed: false);
        var usageAfter = (await ReadConsumerQuotasAsync()).ToArray();
        Assert.AreEqual(2, usageBefore.Length);
        Assert.AreEqual(2, usageAfter.Length);
        foreach (var after in usageAfter)
        {
            var before = usageBefore.Single(row => row.MetricCode == after.MetricCode);
            Assert.AreEqual(before.UsedValue, after.UsedValue, "周期切换和重放必须保留真实用量。");
            Assert.AreEqual(before.ReservedValue, after.ReservedValue, "周期切换和重放必须保留已有预留。");
            if (nextPayment is not null)
            {
                Assert.AreEqual(nextPayment.SubscriptionId, after.SourceSubscriptionId);
                Assert.AreEqual(1L, after.LimitValue);
            }
        }
        await services.GetRequiredService<TenantSubscriptionExpirySweep>().SweepAsync(default);
        Assert.AreEqual(TenantSubscriptionStatuses.Expired, await connection.QuerySingleAsync<string>(
            "SELECT Status FROM fn_tenancy_tenant_subscription WHERE Id=@Id", new { Id = original.SubscriptionId }));
        Assert.AreEqual(TenantMemberStatuses.Active, await connection.QuerySingleAsync<string>(
            "SELECT Status FROM fn_identity_tenant_member WHERE TenantId=@Tenant AND Id=@Id", new { Tenant = tenant, Id = memberId }));
        await VerifyStoredContentAsync(services, tenant, file.ResourceId, file.FileId);
        Assert.AreEqual(1L, await ReadActiveFeaturesAsync(), "独立人工授权在付费来源到期后保留。");

        async Task<Full.NET.Abstractions.Results.Result<TenantSubscriptionPaymentTerms>> CreateRenewalAsync(CreatePaidTenantSubscriptionRequest request)
        {
            await using var child = factory.Services.CreateAsyncScope();
            child.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            return await child.ServiceProvider.GetRequiredService<TenantSubscriptionBillingService>().CreateAsync(tenant, request, default);
        }

        async Task<Guid> CreatePackageAsync(string suffix, long seats, long storage, Guid[] entitlements)
        {
            var package = await services.GetRequiredService<HostTenantPackageManagementService>()
                .CreateAsync(new($"{suffix}-{entitlement:N}", "Boundary plan", null));
            Assert.IsTrue(package.IsSuccess, package.Error?.Code);
            var packageId = package.Value!.Id;
            var offer = await services.GetRequiredService<TenantPackageOfferService>()
                .SaveAsync(packageId, new(tenant, merchant, 12500, "CNY", seats, storage, 30, entitlements, true, 0), default);
            Assert.IsTrue(offer.IsSuccess, offer.Error?.Code);
            return packageId;
        }

        TenantSubscriptionPaymentReceipt Paid(TenantSubscriptionPaymentTerms terms) =>
            new(terms.SubscriptionId, tenant, tenant, terms.PaymentOperationId, Guid.CreateVersion7(), merchant,
                terms.AmountMinor, terms.Currency, $"boundary-{terms.SubscriptionId:N}", clock.UtcNow);

        Task<IEnumerable<ConsumerQuota>> ReadConsumerQuotasAsync() => connection.QueryAsync<ConsumerQuota>("""
                SELECT MetricCode,SourceSubscriptionId,LimitValue,UsedValue,ReservedValue FROM fn_tenancy_quota_metric
                WHERE TenantId=@Tenant AND PeriodKey=@Period AND MetricCode IN (@Seats,@Storage) ORDER BY MetricCode
                """, new { Tenant = tenant, Period = TenantQuotaDefaults.PeriodKey,
                    Seats = TenantQuotaMetricCodes.IdentitySeats, Storage = TenantQuotaMetricCodes.FilesStorageBytes });

        Task<long> ReadActiveFeaturesAsync() => services.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<long>(TenantEntitlementSql.CountActiveFeatureBindings,
                P(("TenantId", tenant), ("EntitlementCode", code), ("NowUtc", clock.UtcNow)));
    }

    private sealed record ConsumerQuota(string MetricCode, Guid SourceSubscriptionId, long LimitValue, long UsedValue, long ReservedValue);

    private sealed class BillingBoundaryClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }
}

/// <summary>通过真实成员与文件入口验证付费周期切换时的用量边界。</summary>
[TestClass]
public sealed class SubscriptionPaidTermBoundaryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public Task Paid_term_boundary_preserves_existing_members_and_files(DatabaseProvider provider, bool downgrade) =>
        SubscriptionBillingRecoveryTests.VerifyPaidTermBoundaryAsync(provider, downgrade);
}
