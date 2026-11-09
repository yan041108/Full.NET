using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Full.NET.Modules.Payments.Serialization;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Payments;

/// <summary>真实双库和正式事务，渠道仅使用确定性替身；不向真实商户发起扣款。</summary>
[TestClass]
public sealed partial class SubscriptionBillingRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Paid_outbox_and_receiver_recover_then_refund_revokes_only_subscription_source(DatabaseProvider provider) =>
        VerifyBillingRecoveryAsync(provider, null);

    private static async Task VerifyBillingRecoveryAsync(DatabaseProvider provider, BillingBrowser? browser)
    {
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new Channel();
        using var factory = new FullNetApiFactory(provider, cs, browser?.Settings, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        browser?.UseKestrel(factory);
        await factory.InitializeAsync();
        HttpClient CreateClient(string host) => browser is null ? factory.CreateClientForHost(host) : browser.CreateClient(factory, host);
        using var client = CreateClient("acme.localhost");
        var tenantContext = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default);
        var tenant = tenantContext.Id;
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
        var subscriptionsPath = $"/api/v1/tenancy/tenants/{tenant:D}/subscriptions";
        var subscriptionPath = $"{subscriptionsPath}/{terms.SubscriptionId:D}";
        var checkoutPath = $"/api/v1/payments/subscriptions/{tenant:D}/{terms.SubscriptionId:D}/checkout";
        var orderPath = $"/api/v1/payments/orders/{orderId:D}";
        var fulfillmentPath = orderPath + "/subscription-fulfillment";
        using var reader = CreateClient("localhost");
        using var subscriptionOperator = CreateClient("localhost");
        using var reconciler = CreateClient("localhost");
        reader.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(
            ["identity.navigation.read", TenancyTenantSubscriptionPermissions.Read, PaymentOrderPermissions.Read, PaymentRefundPermissions.Read]));
        subscriptionOperator.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(
            [TenancyTenantSubscriptionPermissions.Read, TenancyTenantSubscriptionPermissions.Manage, PaymentOrderPermissions.Read, PaymentOrderPermissions.Create]));
        reconciler.DefaultRequestHeaders.Authorization = new("Bearer", await factory.CreateHostAccessTokenAsync(
            [PaymentOrderPermissions.Read, PaymentOrderPermissions.Reconcile, PaymentRefundPermissions.Read]));

        await AssertPaymentNavigationAsync(sp, connection, reader);
        var invoice = await reader.GetFromJsonAsync<TenantSubscriptionInvoiceResponse>(subscriptionPath + "/invoice");
        Assert.IsNotNull(invoice);
        Assert.AreEqual(terms.SubscriptionId, invoice.SubscriptionId);
        Assert.AreEqual("12500", invoice.AmountMinor);
        Assert.AreEqual(orderId, (await reader.GetFromJsonAsync<PaymentOrderResponse>(orderPath))!.Id);
        Assert.AreEqual(terms.SubscriptionId,
            (await reader.GetFromJsonAsync<SubscriptionFulfillmentStatusResponse>(fulfillmentPath))!.SubscriptionId);

        // 读、订阅运营、资金对账权限各自独立，已知有效资源也必须按精确权限拒绝。
        await AssertForbiddenAsync(reader, HttpMethod.Post, subscriptionsPath + "/paid", request);
        await AssertForbiddenAsync(reader, HttpMethod.Post, subscriptionPath + "/cancel", new CancelTenantSubscriptionRequest(1));
        await AssertForbiddenAsync(reader, HttpMethod.Post, checkoutPath);
        await AssertForbiddenAsync(reader, HttpMethod.Post, orderPath + "/reconcile");
        await AssertForbiddenAsync(reader, HttpMethod.Post, orderPath + "/refunds", new CreatePaymentRefundRequest(1, "read-only"));
        await AssertForbiddenAsync(reader, HttpMethod.Post, fulfillmentPath + "/retry");
        await AssertForbiddenAsync(subscriptionOperator, HttpMethod.Post, orderPath + "/reconcile");
        await AssertForbiddenAsync(subscriptionOperator, HttpMethod.Post, fulfillmentPath + "/retry");
        await AssertForbiddenAsync(reconciler, HttpMethod.Get, subscriptionPath + "/invoice");
        await AssertForbiddenAsync(reconciler, HttpMethod.Get, fulfillmentPath);
        await AssertForbiddenAsync(reconciler, HttpMethod.Post, checkoutPath);
        await AssertForbiddenAsync(reconciler, HttpMethod.Post, fulfillmentPath + "/retry");
        await AssertForbiddenAsync(reconciler, HttpMethod.Post, orderPath + "/refunds", new CreatePaymentRefundRequest(1, "reconcile-only"));

        using var operatorCheckout = await subscriptionOperator.PostAsync(checkoutPath, null);
        Assert.AreEqual(HttpStatusCode.OK, operatorCheckout.StatusCode, await operatorCheckout.Content.ReadAsStringAsync());
        var operatorReceipt = await operatorCheckout.Content.ReadFromJsonAsync<SubscriptionCheckoutResponse>();
        Assert.IsNotNull(operatorReceipt);
        Assert.AreEqual(terms.SubscriptionId, operatorReceipt.SubscriptionId);
        Assert.AreEqual(orderId, operatorReceipt.Order.Id);
        Assert.AreEqual(12500, checkout.Value.Order.AmountMinor);
        Assert.AreEqual("Frozen billing plan", checkout.Value.Order.Subject);
        if (browser is not null)
        {
            await browser.CreateUsersAsync(factory);
            await browser.RunAsync(factory, tenantContext.Name, terms.SubscriptionId, orderId, null);
        }
        Assert.AreEqual(1, channel.CreateCount);

        // Outbox 写入故障必须回滚付款状态，不能出现成功订单没有可靠事实。
        await connection.ExecuteAsync("ALTER TABLE fn_outbox_message ADD CONSTRAINT CK_test_billing_outbox CHECK (MessageType <> 'fullnet.payments.order.paid')");
        await Assert.ThrowsAsync<Exception>(() => sp.GetRequiredService<PaymentOrderReconciliationService>().ReconcileAsync(orderId));
        Assert.AreEqual(PaymentTradeStateKeys.AwaitingPayment, await connection.QuerySingleAsync<string>("SELECT TradeStateKey FROM fn_payment_order WHERE Id=@Id", new { Id = orderId }));
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "ALTER TABLE fn_outbox_message DROP CONSTRAINT CK_test_billing_outbox"
            : "ALTER TABLE fn_outbox_message DROP CHECK CK_test_billing_outbox");
        using var reconciled = await reconciler.PostAsync(orderPath + "/reconcile", null);
        Assert.AreEqual(HttpStatusCode.OK, reconciled.StatusCode, await reconciled.Content.ReadAsStringAsync());
        var paidOrder = await reconciled.Content.ReadFromJsonAsync<PaymentOrderResponse>();
        Assert.IsNotNull(paidOrder);
        Assert.AreEqual(orderId, paidOrder.Id);
        Assert.AreEqual(PaymentTradeStateKeys.Succeeded, paidOrder.TradeStateKey);
        var paidBytes = await connection.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType='fullnet.payments.order.paid'");
        var paidHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentPaidFactHandler>().Single();
        var refundHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentRefundFactHandler>().Single();
        await paidHandler.HandleAsync(paidBytes, default);
        var pendingFulfillment = await reader.GetFromJsonAsync<SubscriptionFulfillmentStatusResponse>(fulfillmentPath);
        Assert.IsNotNull(pendingFulfillment);
        Assert.AreEqual("ReadyPaid", pendingFulfillment.StateKey);
        Assert.AreEqual(orderId, pendingFulfillment.PaymentOrderId);
        Assert.AreEqual(terms.SubscriptionId, pendingFulfillment.SubscriptionId);

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

        // 不能只用直接预留代替真实消费者：已履约的存储额度必须支持实际上传并确认字节占用。
        var storedFile = await VerifyStorageConsumerAsync(sp, connection, tenant, allowed: true);

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
        var refundReadback = await reconciler.GetFromJsonAsync<PaymentRefundResponse>($"/api/v1/payments/refunds/{refund.Value!.Id:D}");
        Assert.IsNotNull(refundReadback);
        Assert.AreEqual(refund.Value.Id, refundReadback.Id);
        Assert.AreEqual(orderId, refundReadback.OrderId);
        Assert.AreEqual(PaymentRefundStateKeys.Succeeded, refundReadback.RefundStateKey);
        var refundedFulfillment = await reader.GetFromJsonAsync<SubscriptionFulfillmentStatusResponse>(fulfillmentPath);
        Assert.IsNotNull(refundedFulfillment);
        Assert.AreEqual("Refunded", refundedFulfillment.StateKey);
        Assert.AreEqual(orderId, refundedFulfillment.PaymentOrderId);
        if (browser is not null)
            await browser.RunAsync(factory, tenantContext.Name, terms.SubscriptionId, orderId, refundReadback.Id);
        Assert.AreEqual(0L, await ActiveFeature(sp, tenant, code));
        Assert.AreEqual(1, await SourceCount(connection, terms.SubscriptionId));
        Assert.IsFalse((await billing.ApplyVerifiedPaymentAsync(new(terms.SubscriptionId, tenant, tenant, terms.PaymentOperationId,
            orderId, merchant, 12500, "CNY", "wx-paid", now))).IsSuccess);
        Assert.IsFalse((await sp.GetRequiredService<ITenantQuotaReservationService>().ReserveAsync(tenant,
            new(TenantQuotaMetricCodes.IdentitySeats, "after-refund", 1))).IsSuccess);
        await VerifyStorageConsumerAsync(sp, connection, tenant, allowed: false);
        // 退款阻止新增占用，但保留已存在业务文件，不能冒充删除或重建成功。
        await VerifyStoredContentAsync(sp, tenant, storedFile.ResourceId, storedFile.FileId);

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

        await VerifyFutureReviewRetryAsync(sp, connection, billing, package.Value.Id, tenant, now, paidBytes, refundBytes,
            browser is null ? null : (subscription, order, phase) => browser.RunAsync(factory, tenantContext.Name, subscription, order, null, phase));

    }

    private static async Task AssertForbiddenAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, path + " " + await response.Content.ReadAsStringAsync());
    }

    /// <summary>未来付款的人工重试保持生效门槛，未来账单的退款不受该门槛延误。</summary>
    private static async Task VerifyFutureReviewRetryAsync(IServiceProvider sp, DbConnection connection,
        TenantSubscriptionBillingService billing, Guid packageId, Guid tenant, DateTimeOffset now, byte[] paidBytes, byte[] refundBytes,
        Func<Guid, Guid, string, Task>? verifyBrowser = null)
    {
        var queries = sp.GetRequiredService<IQueryExecutor>();
        var paidHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentPaidFactHandler>().Single();
        var refundHandler = sp.GetServices<IIntegrationEventHandler>().OfType<PaymentRefundFactHandler>().Single();
        // 首次事实已按生效时间排队；人工复核后的付款重试也不能提前耗尽失败预算。
        var future = await billing.CreateAsync(tenant, new(packageId, 2, now.AddDays(1), Guid.CreateVersion7()), default);
        Assert.IsTrue(future.IsSuccess, future.Error?.Code);
        var futureCheckout = await sp.GetRequiredService<SubscriptionCheckoutService>().CheckoutAsync(tenant, future.Value!.SubscriptionId, default);
        Assert.IsTrue(futureCheckout.IsSuccess, futureCheckout.Error?.Code);
        var futureOrder = futureCheckout.Value!.Order.Id;
        Assert.IsTrue((await sp.GetRequiredService<PaymentOrderReconciliationService>().ReconcileAsync(futureOrder)).IsSuccess);
        var futurePaid = (await connection.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType='fullnet.payments.order.paid'"))
            .Single(payload => !payload.SequenceEqual(paidBytes));
        await paidHandler.HandleAsync(futurePaid, default);
        await MarkFutureReviewAsync();
        await VerifyScheduleSnapshotAsync(sp, futureOrder, "Review", future.Value.EffectiveFromUtc, null);
        if (verifyBrowser is not null) await verifyBrowser(future.Value.SubscriptionId, futureOrder, "Review");
        var recovery = sp.GetRequiredService<SubscriptionFulfillmentProcessor>();
        Assert.AreEqual(1, await recovery.RetryAsync(futureOrder, default));
        var waiting = (await queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            P(("Id", future.Value.PaymentOperationId))))!;
        Assert.AreEqual("ReadyPaid", waiting.Phase); Assert.AreEqual(future.Value.EffectiveFromUtc, waiting.DueAtUtc);
        Assert.AreEqual(0, waiting.Attempt); Assert.IsNull(waiting.ErrorCode);
        await VerifyScheduleSnapshotAsync(sp, futureOrder, "ReadyPaid", future.Value.EffectiveFromUtc, future.Value.EffectiveFromUtc);
        if (verifyBrowser is not null) await verifyBrowser(future.Value.SubscriptionId, futureOrder, "ReadyPaid");
        var early = await recovery.ProcessAsync(future.Value.PaymentOperationId, default);
        Assert.IsTrue(early.IsSuccess); Assert.IsFalse(early.Value);
        await paidHandler.HandleAsync(futurePaid, default);
        waiting = (await queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            P(("Id", future.Value.PaymentOperationId))))!;
        Assert.AreEqual(future.Value.EffectiveFromUtc, waiting.DueAtUtc); Assert.AreEqual(0, waiting.Attempt);
        Assert.AreEqual(0, await SourceCount(connection, future.Value.SubscriptionId));

        // 同一未来账单若已退款，复核重试必须立即撤销，不等待原生效日。
        var futureRefund = await sp.GetRequiredService<PaymentRefundManagementService>().CreateForOrderAsync(futureOrder, new(null, "future refund"));
        Assert.IsTrue(futureRefund.IsSuccess, futureRefund.Error?.Code);
        var futureRefundFact = (await connection.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType='fullnet.payments.refund.succeeded'"))
            .Single(payload => !payload.SequenceEqual(refundBytes));
        await refundHandler.HandleAsync(futureRefundFact, default);
        await MarkFutureReviewAsync();
        Assert.AreEqual(1, await recovery.RetryAsync(futureOrder, default));
        var refundWork = (await queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find,
            P(("Id", future.Value.PaymentOperationId))))!;
        Assert.AreEqual("ReadyRefund", refundWork.Phase); Assert.IsTrue(refundWork.DueAtUtc <= DateTimeOffset.UtcNow);
        Assert.IsTrue((await recovery.ProcessAsync(future.Value.PaymentOperationId, default)).Value);
        Assert.AreEqual("Refunded", await connection.QuerySingleAsync<string>("SELECT Phase FROM fn_payment_subscription_fulfillment WHERE Id=@Id", new { Id = future.Value.PaymentOperationId }));
        Assert.AreEqual(0, await SourceCount(connection, future.Value.SubscriptionId));
        await VerifyScheduleSnapshotAsync(sp, futureOrder, "Refunded", future.Value.EffectiveFromUtc, null);

        Task<int> MarkFutureReviewAsync() => connection.ExecuteAsync("""
            UPDATE fn_payment_subscription_fulfillment SET Phase='Review',ErrorCode='test.manual_review',Attempt=10,
                LeaseId=NULL,LeaseUntilUtc=NULL,Version=Version+1 WHERE Id=@Id
            """, new { Id = future.Value.PaymentOperationId });
    }

    /// <summary>队列时间来自原始持久化快照，源生成 JSON 与服务端读取时间保持同一合同。</summary>
    private static async Task VerifyScheduleSnapshotAsync(IServiceProvider sp, Guid orderId, string phase,
        DateTimeOffset effectiveFromUtc, DateTimeOffset? nextAttemptAtUtc)
    {
        var before = sp.GetRequiredService<IClock>().UtcNow;
        var result = await sp.GetRequiredService<SubscriptionCheckoutService>().StatusAsync(orderId, default);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code); var status = result.Value!;
        Assert.AreEqual(phase, status.StateKey); Assert.AreEqual(effectiveFromUtc, status.EffectiveFromUtc);
        Assert.AreEqual(nextAttemptAtUtc, status.NextAttemptAtUtc); Assert.IsTrue(status.ReadAtUtc >= before);
        var context = new PaymentsJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(status, context.SubscriptionFulfillmentStatusResponse));
        Assert.AreEqual(status.EffectiveFromUtc, json.RootElement.GetProperty("effectiveFromUtc").GetDateTimeOffset());
        Assert.AreEqual(status.ReadAtUtc, json.RootElement.GetProperty("readAtUtc").GetDateTimeOffset());
        Assert.AreEqual(nextAttemptAtUtc, json.RootElement.GetProperty("nextAttemptAtUtc").ValueKind == JsonValueKind.Null
            ? null : json.RootElement.GetProperty("nextAttemptAtUtc").GetDateTimeOffset());
    }

    /// <summary>使用真实 Files、存储和配额端口核对新增占用，不以账面余额推导业务成功。</summary>
    private static async Task<(Guid ResourceId, Guid FileId)> VerifyStorageConsumerAsync(IServiceProvider sp, DbConnection connection, Guid tenant, bool allowed)
    {
        var resource = Guid.CreateVersion7();
        var before = await ReadUsageAsync();
        var context = sp.GetRequiredService<ICurrentTenantContextWriter>(); context.SetTenant(new(tenant, "acme", "Acme"));
        Guid fileId = default;
        try
        {
            using var bytes = new MemoryStream(new byte[] { 1, 2, 3 });
            var result = await sp.GetRequiredService<ITenantResourceFileStore>().UploadAsync("import_export", resource,
                Guid.CreateVersion7(), "paid-storage.bin", "application/octet-stream", bytes, 3);
            Assert.AreEqual(allowed, result.IsSuccess, result.Error?.Code);
            if (allowed) fileId = result.Value!.FileId;
            else Assert.AreEqual(FilesErrorCodes.StorageQuotaExceeded, result.Error!.Code);
        }
        finally { context.SetHost(); }
        var after = await ReadUsageAsync();
        Assert.AreEqual(before.Used + (allowed ? 3 : 0), after.Used);
        Assert.AreEqual(before.Reserved, after.Reserved, "成功确认或拒绝后不遗留新预留。");
        Assert.AreEqual(allowed ? 1 : 0, await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM fn_files_tenant_resource_file WHERE TenantId=@Tenant AND OwnerModuleKey='import_export' AND ResourceId=@Resource",
            new { Tenant = tenant, Resource = resource }));
        if (allowed) await VerifyStoredContentAsync(sp, tenant, resource, fileId);
        return (resource, fileId);

        Task<(long Used, long Reserved)> ReadUsageAsync() => connection.QuerySingleAsync<(long Used, long Reserved)>(
            "SELECT UsedValue,ReservedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode='files.storage_bytes' AND PeriodKey='default'",
            new { Tenant = tenant });
    }

    /// <summary>原文件独立按租户和资源读取，付款、退款或重放不应悄悄破坏字节内容。</summary>
    private static async Task VerifyStoredContentAsync(IServiceProvider sp, Guid tenant, Guid resource, Guid fileId)
    {
        var context = sp.GetRequiredService<ICurrentTenantContextWriter>(); context.SetTenant(new(tenant, "acme", "Acme"));
        try
        {
            var result = await sp.GetRequiredService<ITenantResourceFileStore>().OpenReadyContentAsync("import_export", resource, fileId);
            Assert.IsTrue(result.IsSuccess, result.Error?.Code);
            await using var content = result.Value!.Content; using var copy = new MemoryStream();
            await content.CopyToAsync(copy); CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, copy.ToArray());
        }
        finally { context.SetHost(); }
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
        // 同一订单重查保持渠道身份，新增订单不能复用首单金额或流水。
        private readonly Dictionary<string, long> amounts = new(StringComparer.Ordinal);
        private string? firstTradeNo;
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default)
        { CreateCount++; firstTradeNo ??= outTradeNo; amounts[outTradeNo] = amountMinor; return Task.FromResult(WeChatNativePayResult.Success("weixin://test-only")); }
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, CancellationToken cancellationToken = default) => Task.FromResult(WeChatTransactionQueryResult.Success("SUCCESS", outTradeNo == firstTradeNo ? "wx-paid" : "wx-paid-" + outTradeNo, amounts[outTradeNo]));
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo, string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default) => Task.FromResult(WeChatRefundResult.Success(outTradeNo == firstTradeNo ? "wx-refund" : "wx-refund-" + outRefundNo, "SUCCESS"));
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FutureClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
