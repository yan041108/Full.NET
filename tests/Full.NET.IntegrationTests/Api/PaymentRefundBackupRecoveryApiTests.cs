using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Data;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>验证真实数据库备份恢复后，已受理退款按原渠道编号落账并产生唯一资金事实。</summary>
[TestClass]
[DoNotParallelize]
public sealed class PaymentRefundBackupRecoveryApiTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Restoring_preserves_original_refund_and_reconciles_without_resubmitting(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = deadline.Token;
        var sourceConnection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var channel = new ControlledRefundChannel();
        using var source = BuildApi(sourceConnection);
        await source.InitializeAsync(token, useSchemaTemplate: false);
        using var client = source.CreateClientForHost("localhost");
        await AuthenticateAsync(client);
        var tenant = (await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available", token))!
            .Single(item => item.Identifier == "acme").Id;
        var merchant = Guid.CreateVersion7();
        var order = Guid.CreateVersion7();
        var originalOrderNumber = order.ToString("N");
        var creationKey = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = source.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, PaymentSqlParameters.Create(
                ("Id", merchant), ("TenantId", tenant), ("Name", "backup-recovery"),
                ("ChannelKey", PaymentChannelKeys.WeChatNative), ("AppId", "test"), ("MerchantId", "test"),
                ("CertificateSerialNo", "test"), ("NotifyUrl", "https://example.test/notify"), ("ReturnUrl", null),
                ("ApiV3KeyProtected", "test-only"), ("PrivateKeyProtected", "test-only"), ("IsDefault", false),
                ("IsEnabled", true), ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)), token);
            await commands.ExecuteAsync(PaymentOrderSql.Insert, PaymentSqlParameters.Create(
                ("Id", order), ("TenantId", tenant), ("MerchantConfigId", merchant), ("ChannelKey", PaymentChannelKeys.WeChatNative),
                ("OutTradeNo", originalOrderNumber), ("TradeStateKey", PaymentTradeStateKeys.Succeeded), ("AmountMinor", 100L),
                ("Currency", "CNY"), ("Subject", "backup-recovery"), ("Description", null), ("CodeUrl", null),
                ("ProviderTransactionId", "original-trade"), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now),
                ("PaidAtUtc", now), ("Version", 1), ("IdempotencyKey", creationKey), ("RequestHash", new string('A', 64))), token);
        }

        var orderPath = $"/api/v1/payments/orders/{order:D}";
        using var accepted = await client.PostAsJsonAsync(orderPath + "/refunds", new CreatePaymentRefundRequest(29, "original"), token);
        Assert.AreEqual(HttpStatusCode.Created, accepted.StatusCode, await accepted.Content.ReadAsStringAsync(token));
        var original = await accepted.Content.ReadFromJsonAsync<PaymentRefundResponse>(token);
        Assert.IsNotNull(original);
        Assert.AreEqual(PaymentRefundStateKeys.Processing, original.RefundStateKey);
        Assert.AreEqual(original.OutRefundNo, channel.CreatedRefundNo);
        var refundPath = $"/api/v1/payments/refunds/{original.Id:D}";
        var sourceSerializer = source.Services.GetRequiredService<IIntegrationEventSerializer>();
        Assert.HasCount(0, await ReadFactsAsync(sourceConnection));

        var restoredConnection = await DatabaseRecoveryBackup.RestoreAsync(provider, sourceConnection, async () =>
        {
            channel.Settled = true;
            await ReconcileAsync(client, HttpStatusCode.OK);
            var settled = await client.GetFromJsonAsync<PaymentRefundResponse>(refundPath, token);
            Assert.AreEqual(PaymentRefundStateKeys.Succeeded, settled!.RefundStateKey);
            Assert.HasCount(1, await ReadFactsAsync(sourceConnection));
        }, token);

        using var restored = BuildApi(restoredConnection);
        using var recovered = restored.CreateClientForHost("localhost");
        await AuthenticateAsync(recovered);
        var pending = await recovered.GetFromJsonAsync<PaymentRefundResponse>(refundPath, token);
        Assert.AreEqual(original, pending);
        var pendingOrder = await recovered.GetFromJsonAsync<PaymentOrderResponse>(orderPath, token);
        Assert.AreEqual(PaymentTradeStateKeys.Refunding, pendingOrder!.TradeStateKey);
        Assert.HasCount(0, await ReadFactsAsync(restoredConnection));

        await ReconcileAsync(recovered, HttpStatusCode.OK);
        var confirmed = await recovered.GetFromJsonAsync<PaymentRefundResponse>(refundPath, token);
        Assert.IsNotNull(confirmed);
        Assert.AreEqual(original.Id, confirmed.Id);
        Assert.AreEqual(original.OutRefundNo, confirmed.OutRefundNo);
        Assert.AreEqual(original.ProviderRefundId, confirmed.ProviderRefundId);
        Assert.AreEqual(original.Version + 1, confirmed.Version);
        Assert.AreEqual(PaymentRefundStateKeys.Succeeded, confirmed.RefundStateKey);
        await ReconcileAsync(recovered, HttpStatusCode.BadRequest);
        Assert.AreEqual(1, channel.CreateCalls);
        CollectionAssert.AreEqual(new[] { original.OutRefundNo, original.OutRefundNo }, channel.QueriedRefundNumbers);

        var originalFact = sourceSerializer.Deserialize<PaymentRefundSucceededFact>((await ReadFactsAsync(sourceConnection)).Single());
        var recoveredFact = restored.Services.GetRequiredService<IIntegrationEventSerializer>()
            .Deserialize<PaymentRefundSucceededFact>((await ReadFactsAsync(restoredConnection)).Single());
        Assert.AreEqual(originalFact, recoveredFact);
        Assert.AreEqual(original.Id, recoveredFact.RefundId);
        Assert.AreEqual(order, recoveredFact.Payment.OrderId);
        Assert.AreEqual(tenant, recoveredFact.Payment.TenantId);
        Assert.AreEqual(creationKey, recoveredFact.Payment.CreationKey);
        Assert.AreEqual(29L, recoveredFact.RefundAmountMinor);

        FullNetApiFactory BuildApi(string connection) => new(provider, connection, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWeChatNativePayClient>(channel)));

        async Task AuthenticateAsync(HttpClient caller) => caller.DefaultRequestHeaders.Authorization = new("Bearer",
            await IntegrationTestAuthHelper.LoginAsHostUserAsync(caller, "admin", cancellationToken: token));

        async Task ReconcileAsync(HttpClient caller, HttpStatusCode expected)
        {
            using var response = await caller.PostAsync(orderPath + "/reconcile", null, token);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync(token));
        }

        async Task<byte[][]> ReadFactsAsync(string connection)
        {
            await using var observer = MyTasksApiTests.Open(provider, connection);
            await observer.OpenAsync(token);
            var messages = await observer.QueryAsync<byte[]>(new CommandDefinition(
                "SELECT Payload FROM fn_outbox_message WHERE MessageType = @Type",
                new { Type = PaymentRefundSucceededFact.EventType }, cancellationToken: token));
            return messages.ToArray();
        }
    }

    private sealed class ControlledRefundChannel : IWeChatNativePayClient
    {
        internal bool Settled { get; set; }
        internal int CreateCalls { get; private set; }
        internal string? CreatedRefundNo { get; private set; }
        internal List<string> QueriedRefundNumbers { get; } = [];

        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo,
            long amountMinor, string currency, string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig,
            string outTradeNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo,
            string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            CreatedRefundNo = outRefundNo;
            return Task.FromResult(WeChatRefundResult.Success("original-provider-refund", "PROCESSING"));
        }

        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outRefundNo,
            CancellationToken cancellationToken = default)
        {
            QueriedRefundNumbers.Add(outRefundNo);
            return Task.FromResult(new WeChatRefundQueryResult(true, "original-provider-refund", outRefundNo,
                Settled ? "SUCCESS" : "PROCESSING", 29L, "CNY", null));
        }
    }
}
