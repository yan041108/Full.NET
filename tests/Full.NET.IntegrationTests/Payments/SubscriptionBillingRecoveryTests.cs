using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Payments;

/// <summary>真实双库和正式事务，渠道仅使用确定性替身；不向真实商户发起扣款。</summary>
[TestClass]
public sealed class SubscriptionBillingRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Paid_outbox_and_receiver_recover_then_refund_revokes_only_subscription_source(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new Channel();
        using var factory = new FullNetApiFactory(provider, cs, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = (await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default)).Id;
        await using var connection = Open(provider, cs);
        await connection.OpenAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var now = DateTimeOffset.UtcNow;
        var merchant = Guid.CreateVersion7();
        var entitlement = Guid.CreateVersion7();
        var code = "billing-" + entitlement.ToString("N");
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_entitlement_catalog (Id,Code,Name,Description,EntitlementType,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Code,'Billing feature',NULL,'Feature',1,@Now,@Now,1)
            """, new { Id = entitlement, Code = code, Now = now });
        await connection.ExecuteAsync("UPDATE fn_tenancy_settings SET EntitlementEnforcementPhase='Enforced'");
        await connection.ExecuteAsync("UPDATE fn_tenancy_tenant_subscription SET Status='Expired' WHERE TenantId=@Tenant", new { Tenant = tenant });
        foreach (var metric in new[] { TenantQuotaMetricCodes.IdentitySeats, TenantQuotaMetricCodes.FilesStorageBytes })
            await connection.ExecuteAsync("""
                INSERT INTO fn_tenancy_quota_metric (Id,TenantId,MetricCode,PeriodKey,LimitValue,UsedValue,ReservedValue,CreatedAtUtc,UpdatedAtUtc,Version)
                SELECT @Id,@Tenant,@Metric,@Period,100,2,0,@Now,@Now,1
                WHERE NOT EXISTS (SELECT 1 FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period)
                """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Metric = metric, Period = TenantQuotaDefaults.PeriodKey, Now = now });
        var package = await sp.GetRequiredService<HostTenantPackageManagementService>().CreateAsync(new(code, "Frozen billing plan", null));
        Assert.IsTrue(package.IsSuccess, package.Error?.Code);
        var commands = sp.GetRequiredService<ICommandExecutor>();
        await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, P(("Id", merchant), ("TenantId", tenant),
            ("Name", "Billing test"), ("ChannelKey", PaymentChannelKeys.WeChatNative), ("AppId", "app"), ("MerchantId", "merchant"),
            ("CertificateSerialNo", "serial"), ("NotifyUrl", "https://example.com/notify"), ("ReturnUrl", null),
            ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"), ("IsDefault", false), ("IsEnabled", true),
            ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)));
        var offers = sp.GetRequiredService<TenantPackageOfferService>();
        var offer = await offers.SaveAsync(package.Value!.Id, new(tenant, merchant, 12500, "CNY", 20, 100000, 30, [entitlement], true, 0), default);
        Assert.IsTrue(offer.IsSuccess, offer.Error?.Code);
        var billing = sp.GetRequiredService<TenantSubscriptionBillingService>();
        var request = new CreatePaidTenantSubscriptionRequest(package.Value.Id, 1, now.AddMinutes(-1), Guid.CreateVersion7());
        var created = await billing.CreateAsync(tenant, request, default);
        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        var terms = created.Value!;
        Assert.AreEqual(terms, (await billing.CreateAsync(tenant, request, default)).Value);
        Assert.AreEqual(request.EffectiveFromUtc.AddTicks(-(request.EffectiveFromUtc.Ticks % 10)).AddDays(30), terms.EffectiveToUtc);
        var edited = await offers.SaveAsync(package.Value.Id, new(tenant, merchant, 25000, "CNY", 40, 200000, 60, [], true, 1), default);
        Assert.IsTrue(edited.IsSuccess);
        Assert.AreEqual(12500, (await billing.GetPaymentTermsAsync(tenant, terms.SubscriptionId)).Value!.AmountMinor);
        Assert.AreEqual(0, await SourceCount(connection, terms.SubscriptionId));
        var beforeUsage = await connection.QuerySingleAsync<long>("SELECT UsedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey });
        var checkout = await sp.GetRequiredService<SubscriptionCheckoutService>().CheckoutAsync(tenant, terms.SubscriptionId, default);
        Assert.IsTrue(checkout.IsSuccess, checkout.Error?.Code);
        var orderId = checkout.Value!.Order.Id;
        Assert.AreEqual(12500, checkout.Value.Order.AmountMinor);
        Assert.AreEqual("Frozen billing plan", checkout.Value.Order.Subject);
        Assert.AreEqual(orderId, (await sp.GetRequiredService<SubscriptionCheckoutService>().CheckoutAsync(tenant, terms.SubscriptionId, default)).Value!.Order.Id);
        Assert.AreEqual(1, channel.CreateCount);

        // Outbox 写入故障必须回滚付款状态，不能出现成功订单没有可靠事实。
        await connection.ExecuteAsync("ALTER TABLE fn_outbox_message ADD CONSTRAINT CK_test_billing_outbox CHECK (MessageType <> 'fullnet.payments.order.paid')");
        await Assert.ThrowsAsync<Exception>(() => sp.GetRequiredService<PaymentOrderReconciliationService>().ReconcileAsync(orderId));
        Assert.AreEqual(PaymentTradeStateKeys.AwaitingPayment, await connection.QuerySingleAsync<string>("SELECT TradeStateKey FROM fn_payment_order WHERE Id=@Id", new { Id = orderId }));
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_outbox_message DROP CONSTRAINT CK_test_billing_outbox"
            : "ALTER TABLE fn_outbox_message DROP CHECK CK_test_billing_outbox");
        Assert.IsTrue((await sp.GetRequiredService<PaymentOrderReconciliationService>().ReconcileAsync(orderId)).IsSuccess);
        var paidBytes = await connection.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType='fullnet.payments.order.paid'");
        var paidHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentPaidFactHandler>().Single();
        var refundHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentRefundFactHandler>().Single();
        await paidHandler.HandleAsync(paidBytes, default);

        // 接收端提交后丢失响应，使用新恢复器重放原事实，不重复发放。
        var lost = new LostResponsePort(billing);
        var processor = new SubscriptionFulfillmentProcessor(sp.GetRequiredService<IQueryExecutor>(), commands, lost, sp.GetRequiredService<IClock>());
        await Assert.ThrowsExactlyAsync<IOException>(() => processor.ProcessAsync(terms.PaymentOperationId, default));
        Assert.AreEqual(1, await SourceCount(connection, terms.SubscriptionId));
        await connection.ExecuteAsync("UPDATE fn_payment_subscription_fulfillment SET DueAtUtc=@Now WHERE Id=@Id", new { Id = terms.PaymentOperationId, Now = now.AddMinutes(-1) });
        Assert.IsTrue((await sp.GetRequiredService<SubscriptionFulfillmentProcessor>().ProcessAsync(terms.PaymentOperationId, default)).Value);
        Assert.AreEqual(1, await SourceCount(connection, terms.SubscriptionId));
        Assert.AreEqual(beforeUsage, await connection.QuerySingleAsync<long>("SELECT UsedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode=@Metric AND PeriodKey=@Period",
            new { Tenant = tenant, Metric = TenantQuotaMetricCodes.IdentitySeats, Period = TenantQuotaDefaults.PeriodKey }));
        Assert.AreEqual(1L, await ActiveFeature(sp, tenant, code));

        // 未记账迁移重跑保留资金、工作项和快照，SQL Server 补回缺失索引。
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("DROP INDEX IX_fn_payment_subscription_fulfillment_Phase_DueAtUtc ON fn_payment_subscription_fulfillment");
        foreach (var migration in new[] { "325_TenancySubscriptionBilling.sql", "326_PaymentsSubscriptionFulfillment.sql" })
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, migration, provider == DatabaseProvider.SqlServer);
        await ReviewFixMigrationRecoverySupport.CreateRunner(provider, cs).MigrateAsync();
        Assert.AreEqual(1, await SourceCount(connection, terms.SubscriptionId));

        var refund = await sp.GetRequiredService<PaymentRefundManagementService>().CreateForOrderAsync(orderId, new(null, "test refund"));
        Assert.IsTrue(refund.IsSuccess, refund.Error?.Code);
        var refundBytes = await connection.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType='fullnet.payments.refund.succeeded'");
        await refundHandler.HandleAsync(refundBytes, default);
        await paidHandler.HandleAsync(paidBytes, default);
        Assert.IsTrue((await sp.GetRequiredService<SubscriptionFulfillmentProcessor>().ProcessAsync(terms.PaymentOperationId, default)).Value);
        await refundHandler.HandleAsync(refundBytes, default);
        Assert.AreEqual("Refunded", await connection.QuerySingleAsync<string>("SELECT Phase FROM fn_payment_subscription_fulfillment WHERE Id=@Id", new { Id = terms.PaymentOperationId }));
        Assert.AreEqual(0L, await ActiveFeature(sp, tenant, code));
        Assert.AreEqual(1, await SourceCount(connection, terms.SubscriptionId));
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(new(terms.SubscriptionId, tenant, tenant, terms.PaymentOperationId,
            orderId, merchant, 12500, "CNY", "wx-paid", now))).IsSuccess);
        Assert.IsFalse((await sp.GetRequiredService<ITenantQuotaReservationService>().ReserveAsync(tenant,
            new(TenantQuotaMetricCodes.IdentitySeats, "after-refund", 1))).IsSuccess);

        // 人工独立授权仍可生效；退款不会删除历史绑定或业务资源。
        await connection.ExecuteAsync("""
            INSERT INTO fn_tenancy_tenant_entitlement_binding
            (Id,TenantId,EntitlementId,EffectiveFromUtc,EffectiveToUtc,SourcePackageId,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,@Entitlement,@Now,NULL,NULL,@Now,@Now,1)
            """, new { Id = Guid.CreateVersion7(), Tenant = tenant, Entitlement = entitlement, Now = now.AddMinutes(-1) });
        Assert.AreEqual(1L, await ActiveFeature(sp, tenant, code));
        var subscriptions = sp.GetRequiredService<TenantSubscriptionManagementService>();
        var version = await connection.QuerySingleAsync<int>("SELECT Version FROM fn_tenancy_tenant_subscription WHERE Id=@Id", new { Id = terms.SubscriptionId });
        Assert.IsTrue((await subscriptions.CancelAsync(tenant, terms.SubscriptionId, new(version))).IsSuccess);
        var pending = await billing.CreateAsync(tenant, new(package.Value.Id, 2, now, Guid.CreateVersion7()), default);
        Assert.IsTrue(pending.IsSuccess, pending.Error?.Code);
        var cancelled = await subscriptions.CancelAsync(tenant, pending.Value!.SubscriptionId, new(1));
        Assert.IsTrue(cancelled.IsSuccess);
        Assert.IsTrue((await subscriptions.CancelAsync(tenant, pending.Value.SubscriptionId, new(1))).IsSuccess);
        var receipt = new TenantSubscriptionPaymentReceipt(pending.Value.SubscriptionId, tenant, tenant, pending.Value.PaymentOperationId,
            Guid.CreateVersion7(), merchant, 25000, "CNY", "late-paid", now);
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(receipt)).IsSuccess);
        Assert.AreEqual(0, await SourceCount(connection, pending.Value.SubscriptionId));
        var expiring = await billing.CreateAsync(tenant, new(package.Value.Id, 2, now, Guid.CreateVersion7()), default);
        Assert.IsTrue(expiring.IsSuccess, expiring.Error?.Code);
        var futureClock = new FutureClock(expiring.Value!.EffectiveToUtc.AddSeconds(1));
        var queries = sp.GetRequiredService<IQueryExecutor>();
        var database = Options.Create(new DatabaseOptions { Provider = provider });
        await new TenantSubscriptionExpirySweep(queries, sp.GetRequiredService<ICommandTransaction>(),
            new TenantSubscriptionWriteBoundary(queries, commands, database, futureClock), futureClock, database).SweepAsync(default);
        Assert.AreEqual("Expired", await connection.QuerySingleAsync<string>("SELECT Status FROM fn_tenancy_tenant_subscription WHERE Id=@Id", new { Id = expiring.Value.SubscriptionId }));
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(receipt with { SubscriptionId = expiring.Value.SubscriptionId,
            PaymentOperationId = expiring.Value.PaymentOperationId })).IsSuccess);
        Assert.AreEqual(1L, await ActiveFeature(sp, tenant, code));
    }

    private static Task<int> SourceCount(DbConnection connection, Guid id) => connection.QuerySingleAsync<int>(
        "SELECT COUNT(1) FROM fn_tenancy_tenant_entitlement_binding WHERE SourceSubscriptionId=@Id", new { Id = id });
    private static Task<long> ActiveFeature(IServiceProvider sp, Guid tenant, string code) => sp.GetRequiredService<IQueryExecutor>()
        .QuerySingleOrDefaultAsync<long>(TenantEntitlementSql.CountActiveFeatureBindings,
            P(("TenantId", tenant), ("EntitlementCode", code), ("NowUtc", DateTimeOffset.UtcNow)));
    private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value);
    private static DbConnection Open(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(cs) : ReviewFixMigrationRecoverySupport.MySqlConnection(cs);

    private sealed class LostResponsePort(ITenantSubscriptionBillingPort inner) : ITenantSubscriptionBillingPort
    {
        public Task<Result<TenantSubscriptionPaymentTerms>> GetPaymentTermsAsync(Guid tenantId, Guid subscriptionId, CancellationToken cancellationToken = default) => inner.GetPaymentTermsAsync(tenantId, subscriptionId, cancellationToken);
        public async Task<Result<bool>> ApplyVerifiedPaymentAsync(TenantSubscriptionPaymentReceipt receipt, CancellationToken cancellationToken = default)
        {
            Assert.IsTrue((await inner.ApplyVerifiedPaymentAsync(receipt, cancellationToken)).IsSuccess);
            throw new IOException("receiver response lost");
        }
        public Task<Result<bool>> ApplyVerifiedRefundAsync(TenantSubscriptionRefundReceipt receipt, CancellationToken cancellationToken = default) => inner.ApplyVerifiedRefundAsync(receipt, cancellationToken);
    }

    private sealed class Channel : IWeChatNativePayClient
    {
        internal int CreateCount;
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default)
        { CreateCount++; return Task.FromResult(WeChatNativePayResult.Success("weixin://test-only")); }
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default) => Task.FromResult(WeChatTransactionQueryResult.Success("SUCCESS", "wx-paid", 12500));
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default) => Task.FromResult(WeChatRefundResult.Success("wx-refund", "SUCCESS"));
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FutureClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
