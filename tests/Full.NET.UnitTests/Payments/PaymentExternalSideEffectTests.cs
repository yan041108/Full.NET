using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>支付与退款必须先提交本地意图，再在事务外调用渠道，并保留未知结果。</summary>
[TestClass]
public sealed class PaymentExternalSideEffectTests
{
    /// <summary>租户目录读取必须发生在订单意图事务之前。</summary>
    [TestMethod]
    public async Task Order_tenant_directory_is_checked_before_intent_transaction_async()
    {
        var fixture = CreateOrderFixture();
        fixture.Tenants.IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.AreEqual(0, fixture.Coordinator.BeginCount);
                return true;
            });
        fixture.WeChat.CreateNativeOrderAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(WeChatNativePayResult.Success("weixin://wxpay/bizpayurl"));

        var result = await fixture.Orders.CreateAsync(CreateRequest(fixture.TenantId, fixture.Merchant.Id));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Tenants.Received(1).IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>());
    }

    /// <summary>非活动租户必须在开启事务前失败。</summary>
    [TestMethod]
    public async Task Inactive_tenant_does_not_begin_order_transaction_async()
    {
        var fixture = CreateOrderFixture();
        fixture.Tenants.IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await fixture.Orders.CreateAsync(CreateRequest(fixture.TenantId, fixture.Merchant.Id));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(PaymentErrorCodes.TenantNotFound, result.Error!.Code);
        Assert.AreEqual(0, fixture.Coordinator.BeginCount);
        Assert.AreEqual(0, fixture.Store.Orders.Count);
        await fixture.WeChat.DidNotReceive().CreateNativeOrderAsync(
            Arg.Any<PaymentMerchantConfigRecord>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>渠道下单只能发生在意图事务提交之后，不能占用本地事务连接。</summary>
    [TestMethod]
    public async Task Order_provider_is_invoked_after_intent_commit_async()
    {
        var fixture = CreateOrderFixture();
        fixture.WeChat.CreateNativeOrderAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                Assert.IsTrue(fixture.Coordinator.CommitCount >= 1);
                return WeChatNativePayResult.Success("weixin://wxpay/bizpayurl");
            });

        var result = await fixture.Orders.CreateAsync(CreateRequest(fixture.TenantId, fixture.Merchant.Id));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentTradeStateKeys.AwaitingPayment, fixture.Store.Orders.Values.Single().TradeStateKey);
        await fixture.WeChat.Received(1).CreateNativeOrderAsync(
            Arg.Any<PaymentMerchantConfigRecord>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>渠道超时必须写成未知状态，不能把可能已生效的下单标记为失败。</summary>
    [TestMethod]
    public async Task Order_timeout_persists_provider_unknown_async()
    {
        var fixture = CreateOrderFixture();
        fixture.WeChat.CreateNativeOrderAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<WeChatNativePayResult>(_ => throw new TaskCanceledException("provider timeout"));

        var result = await fixture.Orders.CreateAsync(CreateRequest(fixture.TenantId, fixture.Merchant.Id));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(PaymentErrorCodes.OrderProviderUnknown, result.Error!.Code);
        Assert.AreEqual(PaymentTradeStateKeys.ProviderUnknown, fixture.Store.Orders.Values.Single().TradeStateKey);
    }

    /// <summary>渠道已经成功时，本地回写失败仍必须保留已提交意图，供后续对账。</summary>
    [TestMethod]
    public async Task Order_keeps_committed_intent_when_result_persist_fails_async()
    {
        var fixture = CreateOrderFixture();
        fixture.WeChat.CreateNativeOrderAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(WeChatNativePayResult.Success("weixin://wxpay/bizpayurl"));
        fixture.Store.ThrowOnProviderResultUpdate = new InvalidOperationException("local persist failed");

        var result = await fixture.Orders.CreateAsync(CreateRequest(fixture.TenantId, fixture.Merchant.Id));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(PaymentErrorCodes.OrderProviderUnknown, result.Error!.Code);
        Assert.AreEqual(PaymentTradeStateKeys.Created, fixture.Store.Orders.Values.Single().TradeStateKey);
        await fixture.WeChat.Received(1).CreateNativeOrderAsync(
            Arg.Any<PaymentMerchantConfigRecord>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>对账查询渠道时不得开启本地事务。</summary>
    [TestMethod]
    public async Task Reconciliation_queries_provider_outside_transaction_async()
    {
        var fixture = CreateOrderFixture();
        var orderId = Guid.NewGuid();
        fixture.Store.Orders[orderId] = new PaymentOrderRecord
        {
            Id = orderId,
            TenantId = fixture.TenantId,
            MerchantConfigId = fixture.Merchant.Id,
            ChannelKey = PaymentChannelKeys.WeChatNative,
            OutTradeNo = orderId.ToString("N"),
            TradeStateKey = PaymentTradeStateKeys.ProviderUnknown,
            AmountMinor = 100,
            Currency = "CNY",
            Subject = "review",
            Version = 1,
        };
        fixture.WeChat.QueryTransactionByOutTradeNoAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.IsFalse(fixture.Coordinator.HasTransaction);
                return WeChatTransactionQueryResult.Success("NOTPAY", "wx-tx", 100);
            });

        var result = await fixture.Reconciliation.ReconcileAsync(orderId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PaymentTradeStateKeys.AwaitingPayment, fixture.Store.Orders[orderId].TradeStateKey);
    }

    /// <summary>并发退款只有领取到订单的一方可以调用渠道。</summary>
    [TestMethod]
    public async Task Concurrent_refund_second_request_does_not_invoke_provider_async()
    {
        var fixture = CreateOrderFixture();
        var orderId = Guid.NewGuid();
        fixture.Store.Orders[orderId] = SucceededOrder(orderId, fixture);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.WeChat.CreateDomesticRefundAsync(
                Arg.Any<PaymentMerchantConfigRecord>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return WeChatRefundResult.Success("wx-refund", "SUCCESS");
            });

        var first = fixture.Refunds.CreateForOrderAsync(orderId, new CreatePaymentRefundRequest(null, "duplicate-guard"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await fixture.Refunds.CreateForOrderAsync(orderId, new CreatePaymentRefundRequest(null, "duplicate-guard"));
        release.TrySetResult();
        var firstResult = await first;

        Assert.IsTrue(firstResult.IsSuccess);
        Assert.IsFalse(second.IsSuccess);
        Assert.AreEqual(PaymentErrorCodes.RefundInProgress, second.Error!.Code);
        await fixture.WeChat.Received(1).CreateDomesticRefundAsync(
            Arg.Any<PaymentMerchantConfigRecord>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>建立带真实事务提交语义的订单/退款服务。</summary>
    /// <returns>隔离测试夹具。</returns>
    private static OrderFixture CreateOrderFixture()
    {
        var tenantId = Guid.NewGuid();
        var merchant = new PaymentMerchantConfigRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelKey = PaymentChannelKeys.WeChatNative,
            IsEnabled = true,
        };
        var store = new PaymentStore { Merchant = merchant };
        var coordinator = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(coordinator);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 7, 2, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var tenants = Substitute.For<IIdentityActiveTenantDirectory>();
        tenants.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);
        var weChat = Substitute.For<IWeChatNativePayClient>();
        var alipay = Substitute.For<IAlipayPagePayClient>();
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        var orderQueries = new PaymentOrderQueryService(store, options);
        var refundQueries = new PaymentRefundQueryService(store, options);
        return new OrderFixture(
            tenantId,
            merchant,
            store,
            coordinator,
            weChat,
            tenants,
            new PaymentOrderManagementService(
                store, store, transaction, orderQueries, weChat, alipay, tenants, clock, ids, options),
            new PaymentRefundManagementService(
                store, store, transaction, refundQueries, weChat, clock, ids),
            new PaymentOrderReconciliationService(
                store, store, transaction, orderQueries, weChat, alipay, clock));
    }

    /// <summary>构造合法创建请求。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="merchantId">商户配置标识。</param>
    /// <returns>创建请求。</returns>
    private static CreatePaymentOrderRequest CreateRequest(Guid tenantId, Guid merchantId) =>
        new(tenantId, merchantId, PaymentChannelKeys.WeChatNative, 100, "CNY", "review-order", null);

    /// <summary>构造已支付成功、可退款的订单。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="fixture">测试夹具。</param>
    /// <returns>成功订单。</returns>
    private static PaymentOrderRecord SucceededOrder(Guid orderId, OrderFixture fixture) =>
        new()
        {
            Id = orderId,
            TenantId = fixture.TenantId,
            MerchantConfigId = fixture.Merchant.Id,
            ChannelKey = PaymentChannelKeys.WeChatNative,
            OutTradeNo = orderId.ToString("N"),
            TradeStateKey = PaymentTradeStateKeys.Succeeded,
            AmountMinor = 100,
            Currency = "CNY",
            Subject = "paid",
            Version = 1,
            PaidAtUtc = new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero),
        };

    /// <summary>订单外部副作用测试夹具。</summary>
    /// <param name="TenantId">租户标识。</param>
    /// <param name="Merchant">商户配置。</param>
    /// <param name="Store">内存持久化。</param>
    /// <param name="Coordinator">事务记录器。</param>
    /// <param name="WeChat">微信渠道替身。</param>
    /// <param name="Tenants">权威租户目录替身。</param>
    /// <param name="Orders">订单服务。</param>
    /// <param name="Refunds">退款服务。</param>
    /// <param name="Reconciliation">对账服务。</param>
    private sealed record OrderFixture(
        Guid TenantId,
        PaymentMerchantConfigRecord Merchant,
        PaymentStore Store,
        RecordingDbTransactionCoordinator Coordinator,
        IWeChatNativePayClient WeChat,
        IIdentityActiveTenantDirectory Tenants,
        PaymentOrderManagementService Orders,
        PaymentRefundManagementService Refunds,
        PaymentOrderReconciliationService Reconciliation);

    /// <summary>按语句名维护支付订单与退款的内存状态，用于验证事务外副作用。</summary>
    private sealed class PaymentStore : IQueryExecutor, ICommandExecutor
    {
        /// <summary>当前测试商户。</summary>
        public PaymentMerchantConfigRecord Merchant { get; init; } = null!;

        /// <summary>已提交订单。</summary>
        public Dictionary<Guid, PaymentOrderRecord> Orders { get; } = [];

        /// <summary>已提交退款。</summary>
        public Dictionary<Guid, PaymentRefundRecord> Refunds { get; } = [];

        /// <summary>回写渠道结果时抛出的异常；为空表示正常更新。</summary>
        public Exception? ThrowOnProviderResultUpdate { get; set; }

        /// <inheritdoc />
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = Params(parameters);
            if (typeof(T) == typeof(PaymentMerchantConfigRecord))
            {
                return Task.FromResult((T?)(object?)Merchant);
            }

            if (typeof(T) == typeof(PaymentOrderRecord)
                && values.TryGetValue("OrderId", out var orderId)
                && orderId is Guid id
                && Orders.TryGetValue(id, out var order))
            {
                return Task.FromResult((T?)(object?)order);
            }

            if (typeof(T) == typeof(PaymentRefundRecord)
                && values.TryGetValue("RefundId", out var refundId)
                && refundId is Guid refundKey
                && Refunds.TryGetValue(refundKey, out var refund))
            {
                return Task.FromResult((T?)(object?)refund);
            }

            return Task.FromResult(default(T?));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(PaymentRefundRecord)
                && statement.Name == PaymentRefundSql.ListRecoverableByOrderId.Name)
            {
                var orderId = (Guid)Params(parameters)["OrderId"]!;
                IReadOnlyList<PaymentRefundRecord> rows = [.. Refunds.Values
                    .Where(row => row.OrderId == orderId
                        && row.RefundStateKey is PaymentRefundStateKeys.Created
                            or PaymentRefundStateKeys.ProviderUnknown)
                    .OrderBy(row => row.CreatedAtUtc)
                    .ThenBy(row => row.Id)];
                return Task.FromResult((IReadOnlyList<T>)rows);
            }

            return Task.FromResult<IReadOnlyList<T>>([]);
        }

        /// <inheritdoc />
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var values = Params(parameters);
            switch (statement.Name)
            {
                case "payments.insert_order":
                    var insertedId = (Guid)values["Id"]!;
                    Orders[insertedId] = new PaymentOrderRecord
                    {
                        Id = insertedId,
                        TenantId = (Guid)values["TenantId"]!,
                        MerchantConfigId = (Guid)values["MerchantConfigId"]!,
                        ChannelKey = (string)values["ChannelKey"]!,
                        OutTradeNo = (string)values["OutTradeNo"]!,
                        TradeStateKey = (string)values["TradeStateKey"]!,
                        AmountMinor = (long)values["AmountMinor"]!,
                        Currency = (string)values["Currency"]!,
                        Subject = (string)values["Subject"]!,
                        Description = values["Description"] as string,
                        CreatedAtUtc = (DateTimeOffset)values["CreatedAtUtc"]!,
                        UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                        Version = (int)values["Version"]!,
                    };
                    return Task.FromResult(1);
                case "payments.update_order_provider_result":
                    if (ThrowOnProviderResultUpdate is not null)
                    {
                        throw ThrowOnProviderResultUpdate;
                    }

                    return Task.FromResult(UpdateOrder(
                        (Guid)values["OrderId"]!,
                        (int)values["Version"]!,
                        current => new PaymentOrderRecord
                        {
                            Id = current.Id,
                            TenantId = current.TenantId,
                            MerchantConfigId = current.MerchantConfigId,
                            ChannelKey = current.ChannelKey,
                            OutTradeNo = current.OutTradeNo,
                            TradeStateKey = (string)values["TradeStateKey"]!,
                            AmountMinor = current.AmountMinor,
                            Currency = current.Currency,
                            Subject = current.Subject,
                            Description = current.Description,
                            CodeUrl = values["CodeUrl"] as string,
                            ProviderTransactionId = values["ProviderTransactionId"] as string,
                            FailMessage = values["FailMessage"] as string,
                            CreatedAtUtc = current.CreatedAtUtc,
                            UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                            PaidAtUtc = current.PaidAtUtc,
                            Version = current.Version + 1,
                        }));
                case "payments.claim_order_trade_state":
                    return Task.FromResult(UpdateOrder(
                        (Guid)values["OrderId"]!,
                        (int)values["Version"]!,
                        current => current.TradeStateKey != (string)values["ExpectedTradeStateKey"]!
                            ? current
                            : new PaymentOrderRecord
                            {
                                Id = current.Id,
                                TenantId = current.TenantId,
                                MerchantConfigId = current.MerchantConfigId,
                                ChannelKey = current.ChannelKey,
                                OutTradeNo = current.OutTradeNo,
                                TradeStateKey = (string)values["TradeStateKey"]!,
                                AmountMinor = current.AmountMinor,
                                Currency = current.Currency,
                                Subject = current.Subject,
                                Description = current.Description,
                                CodeUrl = current.CodeUrl,
                                ProviderTransactionId = current.ProviderTransactionId,
                                FailMessage = values["FailMessage"] as string,
                                CreatedAtUtc = current.CreatedAtUtc,
                                UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                                PaidAtUtc = current.PaidAtUtc,
                                Version = current.Version + 1,
                            },
                        requireStateChange: true));
                case "payments.update_order_trade_state":
                    return Task.FromResult(UpdateOrder(
                        (Guid)values["OrderId"]!,
                        (int)values["Version"]!,
                        current => new PaymentOrderRecord
                        {
                            Id = current.Id,
                            TenantId = current.TenantId,
                            MerchantConfigId = current.MerchantConfigId,
                            ChannelKey = current.ChannelKey,
                            OutTradeNo = current.OutTradeNo,
                            TradeStateKey = (string)values["TradeStateKey"]!,
                            AmountMinor = current.AmountMinor,
                            Currency = current.Currency,
                            Subject = current.Subject,
                            Description = current.Description,
                            CodeUrl = current.CodeUrl,
                            ProviderTransactionId = values["ProviderTransactionId"] as string ?? current.ProviderTransactionId,
                            FailMessage = values["FailMessage"] as string,
                            CreatedAtUtc = current.CreatedAtUtc,
                            UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                            PaidAtUtc = values["PaidAtUtc"] as DateTimeOffset? ?? current.PaidAtUtc,
                            Version = current.Version + 1,
                        }));
                case "payments.insert_refund":
                    var refundId = (Guid)values["Id"]!;
                    Refunds[refundId] = new PaymentRefundRecord
                    {
                        Id = refundId,
                        TenantId = (Guid)values["TenantId"]!,
                        OrderId = (Guid)values["OrderId"]!,
                        MerchantConfigId = (Guid)values["MerchantConfigId"]!,
                        OutTradeNo = (string)values["OutTradeNo"]!,
                        OutRefundNo = (string)values["OutRefundNo"]!,
                        RefundStateKey = (string)values["RefundStateKey"]!,
                        AmountMinor = (long)values["AmountMinor"]!,
                        Currency = (string)values["Currency"]!,
                        Reason = (string)values["Reason"]!,
                        CreatedAtUtc = (DateTimeOffset)values["CreatedAtUtc"]!,
                        UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                        Version = (int)values["Version"]!,
                    };
                    return Task.FromResult(1);
                case "payments.update_refund_provider_result":
                    return Task.FromResult(UpdateRefund(
                        (Guid)values["RefundId"]!,
                        (int)values["Version"]!,
                        current => new PaymentRefundRecord
                        {
                            Id = current.Id,
                            TenantId = current.TenantId,
                            OrderId = current.OrderId,
                            MerchantConfigId = current.MerchantConfigId,
                            OutTradeNo = current.OutTradeNo,
                            OutRefundNo = current.OutRefundNo,
                            RefundStateKey = (string)values["RefundStateKey"]!,
                            AmountMinor = current.AmountMinor,
                            Currency = current.Currency,
                            Reason = current.Reason,
                            ProviderRefundId = values["ProviderRefundId"] as string,
                            FailMessage = values["FailMessage"] as string,
                            CreatedAtUtc = current.CreatedAtUtc,
                            UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                            CompletedAtUtc = values["CompletedAtUtc"] as DateTimeOffset?,
                            Version = current.Version + 1,
                        }));
                case "payments.claim_refund_invocation":
                    return Task.FromResult(UpdateRefund(
                        (Guid)values["RefundId"]!,
                        (int)values["Version"]!,
                        current => new PaymentRefundRecord
                        {
                            Id = current.Id,
                            TenantId = current.TenantId,
                            OrderId = current.OrderId,
                            MerchantConfigId = current.MerchantConfigId,
                            OutTradeNo = current.OutTradeNo,
                            OutRefundNo = current.OutRefundNo,
                            RefundStateKey = current.RefundStateKey,
                            AmountMinor = current.AmountMinor,
                            Currency = current.Currency,
                            Reason = current.Reason,
                            ProviderRefundId = current.ProviderRefundId,
                            FailMessage = current.FailMessage,
                            CreatedAtUtc = current.CreatedAtUtc,
                            UpdatedAtUtc = values["UpdatedAtUtc"] as DateTimeOffset?,
                            CompletedAtUtc = current.CompletedAtUtc,
                            Version = current.Version + 1,
                        }));
                default:
                    return Task.FromResult(0);
            }
        }

        /// <summary>按版本更新订单；状态领取失败时返回 0。</summary>
        /// <param name="orderId">订单标识。</param>
        /// <param name="version">期望版本。</param>
        /// <param name="update">新行工厂。</param>
        /// <param name="requireStateChange">领取场景下状态未变视为冲突。</param>
        /// <returns>受影响行数。</returns>
        private int UpdateOrder(
            Guid orderId,
            int version,
            Func<PaymentOrderRecord, PaymentOrderRecord> update,
            bool requireStateChange = false)
        {
            if (!Orders.TryGetValue(orderId, out var current) || current.Version != version)
            {
                return 0;
            }

            var next = update(current);
            if (requireStateChange && ReferenceEquals(next, current))
            {
                return 0;
            }

            Orders[orderId] = next;
            return 1;
        }

        /// <summary>按版本更新退款。</summary>
        /// <param name="refundId">退款标识。</param>
        /// <param name="version">期望版本。</param>
        /// <param name="update">新行工厂。</param>
        /// <returns>受影响行数。</returns>
        private int UpdateRefund(
            Guid refundId,
            int version,
            Func<PaymentRefundRecord, PaymentRefundRecord> update)
        {
            if (!Refunds.TryGetValue(refundId, out var current) || current.Version != version)
            {
                return 0;
            }

            Refunds[refundId] = update(current);
            return 1;
        }

        /// <summary>读取命名 SQL 参数。</summary>
        /// <param name="parameters">命令参数。</param>
        /// <returns>参数字典。</returns>
        private static IReadOnlyDictionary<string, object?> Params(object? parameters) =>
            (IReadOnlyDictionary<string, object?>)parameters!;
    }
}
