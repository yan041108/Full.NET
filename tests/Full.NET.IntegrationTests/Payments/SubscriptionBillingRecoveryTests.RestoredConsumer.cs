extern alias workerhost;

using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RealOutboxProcessor = workerhost::Full.NET.Host.Worker.OutboxProcessor;
using RealOutboxOptions = workerhost::Full.NET.Host.Worker.OutboxWorkerOptions;

namespace Full.NET.IntegrationTests.Payments;

/// <summary>通过恢复库的正式后台消费者验证退款重复投递及迟到付款。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SubscriptionRefundBackupConsumerTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Restored_worker_replays_refund_without_changing_entitlements_twice(DatabaseProvider provider) =>
        SubscriptionBillingRecoveryTests.VerifyBackupConsumerAsync(provider);
}

public sealed partial class SubscriptionBillingRecoveryTests
{
    internal static Task VerifyBackupConsumerAsync(DatabaseProvider provider) =>
        VerifyBillingRecoveryAsync(provider, browser: null, verifyRestoredConsumption: true);

    private static async Task VerifyRestoredRefundConsumptionAsync(FullNetApiFactory source, DbConnection sourceConnection,
        IServiceProvider sourceServices, HttpClient reconciler, Channel channel, TenantSubscriptionPaymentTerms terms,
        string orderPath, Guid orderId, Guid entitlement, string featureCode)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = budget.Token;
        var subscription = terms.SubscriptionId;
        var tenant = terms.TenantId;
        var creationKey = terms.PaymentOperationId;

        var manual = Guid.CreateVersion7();
        await sourceConnection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_tenancy_tenant_entitlement_binding
            (Id,TenantId,EntitlementId,EffectiveFromUtc,EffectiveToUtc,SourcePackageId,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@Tenant,@Entitlement,@Now,NULL,NULL,@Now,@Now,1)
            """, new { Id = manual, Tenant = tenant, Entitlement = entitlement, Now = DateTimeOffset.UtcNow.AddMinutes(-1) }, cancellationToken: token));
        channel.HoldRefundForRecovery = true;
        var created = await sourceServices.GetRequiredService<PaymentRefundManagementService>().CreateForOrderAsync(orderId, new(null, "backup refund"), token);
        Assert.IsTrue(created.IsSuccess, created.Error?.Code);
        var refund = created.Value!;
        Assert.AreEqual(PaymentRefundStateKeys.Processing, refund.RefundStateKey);
        var recoveredConnection = await DatabaseRecoveryBackup.RestoreAsync(source.Provider, source.ConnectionString,
            () => ReconcileAsync(reconciler), token);

        using var recovered = new FullNetApiFactory(source.Provider, recoveredConnection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));
        using var recoveryReconciler = recovered.CreateClientForHost("localhost");
        recoveryReconciler.DefaultRequestHeaders.Authorization = new("Bearer", await recovered.CreateHostAccessTokenAsync(
            [PaymentOrderPermissions.Read, PaymentOrderPermissions.Reconcile, PaymentRefundPermissions.Read], token));
        using var recoveryObserver = recovered.CreateClientForHost("localhost");
        recoveryObserver.DefaultRequestHeaders.Authorization = new("Bearer", await recovered.CreateHostAccessTokenAsync(
            [PaymentOrderPermissions.Read, TenancyTenantSubscriptionPermissions.Read], token));
        var original = await recoveryReconciler.GetFromJsonAsync<PaymentRefundResponse>($"/api/v1/payments/refunds/{refund.Id:D}", token);
        Assert.AreEqual(refund, original);
        await using var db = Open(source.Provider, recoveredConnection);
        await db.OpenAsync(token);
        await using var scope = recovered.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        Assert.AreEqual(2L, await ActiveFeature(sp, tenant, featureCode));
        var before = await ReadStateAsync();
        Assert.AreEqual("Applied", before.InvoicePhase);
        Assert.AreEqual("Active", before.SubscriptionStatus);
        Assert.AreEqual(0, await CountRefundAuditAsync());

        await ReconcileAsync(recoveryReconciler);
        var workPath = orderPath + "/subscription-fulfillment";
        await ConsumeAsync("BillingRecovery.First", expectedMessages: 2);
        var after = await ReadStateAsync();
        Assert.AreEqual("Refunded", after.InvoicePhase);
        Assert.AreEqual("PastDue", after.SubscriptionStatus);
        Assert.AreEqual(refund.Id, after.RefundId);
        Assert.AreEqual(before.InvoiceVersion + 1, after.InvoiceVersion);
        Assert.AreEqual(before.SubscriptionVersion + 1, after.SubscriptionVersion);
        Assert.AreEqual(1L, await ActiveFeature(sp, tenant, featureCode));
        Assert.AreEqual(1, await SourceCount(db, subscription));
        Assert.AreEqual(1, await CountRefundAuditAsync());

        // 重建的消息信封拥有新的 Outbox ID，验证消费端按原业务身份处理重复退款和迟到付款。
        var serializer = sp.GetRequiredService<IIntegrationEventSerializer>();
        var paid = serializer.Deserialize<PaymentSucceededFact>(await ReadPayloadAsync(PaymentSucceededFact.EventType));
        var settled = serializer.Deserialize<PaymentRefundSucceededFact>(await ReadPayloadAsync(PaymentRefundSucceededFact.EventType));
        var writer = sp.GetRequiredService<IOutboxWriter>();
        await sp.GetRequiredService<ICommandTransaction>().ExecuteAsync(async local =>
        {
            await writer.AddAsync(PaymentRefundSucceededFact.EventType, 1, settled, local);
            await writer.AddAsync(PaymentSucceededFact.EventType, 1, paid, local);
            return true;
        }, token);
        await ConsumeAsync("BillingRecovery.Replay", expectedMessages: 4);
        Assert.AreEqual(after, await ReadStateAsync());
        Assert.AreEqual(1, await CountRefundAuditAsync());
        Assert.AreEqual(1L, await ActiveFeature(sp, tenant, featureCode));
        Assert.AreEqual(1, await SourceCount(db, subscription));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT Version FROM fn_tenancy_tenant_entitlement_binding WHERE Id=@Id AND TenantId=@Tenant
            """, new { Id = manual, Tenant = tenant }, cancellationToken: token)));
        Assert.AreEqual(1, channel.RefundCreateCount);

        async Task ReconcileAsync(HttpClient client)
        {
            using var response = await client.PostAsync(orderPath + "/reconcile", null, token);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(token));
        }

        Task<(string InvoicePhase, int InvoiceVersion, Guid? RefundId, string SubscriptionStatus, int SubscriptionVersion, int WorkVersion)> ReadStateAsync() =>
            db.QuerySingleAsync<(string, int, Guid?, string, int, int)>(new CommandDefinition("""
                SELECT i.Phase,i.Version,i.LastRefundId,s.Status,s.Version,w.Version
                FROM fn_tenancy_subscription_invoice i
                INNER JOIN fn_tenancy_tenant_subscription s ON s.Id=i.Id AND s.TenantId=i.TenantId
                INNER JOIN fn_payment_subscription_fulfillment w ON w.Id=i.PaymentOperationId AND w.BeneficiaryTenantId=i.TenantId
                WHERE i.Id=@Subscription AND i.TenantId=@Tenant AND w.Id=@CreationKey
                """, new { Subscription = subscription, Tenant = tenant, CreationKey = creationKey }, cancellationToken: token));

        Task<int> CountRefundAuditAsync() => db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM fn_tenancy_domain_audit
            WHERE EntityId=@Id AND TenantId=@Tenant AND ActionKey='tenancy.subscription.refund_applied'
            """, new { Id = subscription, Tenant = tenant }, cancellationToken: token));

        Task<byte[]> ReadPayloadAsync(string type) => db.QuerySingleAsync<byte[]>(new CommandDefinition(
            "SELECT Payload FROM fn_outbox_message WHERE MessageType=@Type", new { Type = type }, cancellationToken: token));

        async Task ConsumeAsync(string name, int expectedMessages)
        {
            using var worker = await IntegrationWorkerHostFactory.BuildAsync(source.Provider, recoveredConnection,
                new Dictionary<string, string?>(), name, nameof(SubscriptionFulfillmentWorker),
                additionalHostedServiceNames: [typeof(RealOutboxProcessor).Name], configureServices: services =>
                {
                    services.AddOptions<RealOutboxOptions>().Configure(options =>
                    {
                        options.PollMilliseconds = 100;
                        options.MaximumIdlePollMilliseconds = 1000;
                    });
                    services.AddHostedService<RealOutboxProcessor>();
                });
            var hosted = worker.Services.GetServices<IHostedService>().ToArray();
            Assert.IsTrue(hosted.Any(service => service is RealOutboxProcessor));
            Assert.IsTrue(hosted.Any(service => service is SubscriptionFulfillmentWorker));
            await worker.StartAsync(token);
            string? phase = null;
            var processed = 0;
            try
            {
                using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
                window.CancelAfter(TimeSpan.FromSeconds(35));
                while (true)
                {
                    var status = await recoveryObserver.GetFromJsonAsync<SubscriptionFulfillmentStatusResponse>(workPath, window.Token);
                    phase = status?.StateKey;
                    processed = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                        SELECT COUNT(*) FROM fn_outbox_message
                        WHERE MessageType IN (@Paid,@Refunded) AND ProcessedAtUtc IS NOT NULL
                        """, new { Paid = PaymentSucceededFact.EventType, Refunded = PaymentRefundSucceededFact.EventType }, cancellationToken: window.Token));
                    if (phase == "Refunded" && processed == expectedMessages) return;
                    await Task.Delay(200, window.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Assert.Fail($"{name} 未在窗口内完成消费，履约状态 {phase}，Outbox 已处理 {processed}/{expectedMessages}。");
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
    }
}
