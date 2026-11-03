using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>恢复必须复核资金权威，并在响应丢失或退款竞态下保留可恢复状态。</summary>
[TestClass]
public sealed class SubscriptionFulfillmentProcessorTests
{
    [TestMethod]
    [DataRow("amount")]
    [DataRow("currency")]
    [DataRow("merchant")]
    [DataRow("tenant")]
    [DataRow("creation")]
    [DataRow("transaction")]
    public async Task Mismatched_authority_never_calls_entitlement_port(string field)
    {
        var f = new Fixture();
        f.Funds = field switch
        {
            "amount" => f.Funds with { AmountMinor = 1 },
            "currency" => f.Funds with { Currency = "USD" },
            "merchant" => f.Funds with { MerchantConfigId = Guid.CreateVersion7() },
            "tenant" => f.Funds with { TenantId = Guid.CreateVersion7() },
            "creation" => f.Funds with { CreationKey = Guid.CreateVersion7() },
            _ => f.Funds with { ProviderTransactionId = "other" }
        };
        Assert.IsFalse((await f.Processor().ProcessAsync(f.Work.Id, default)).IsSuccess);
        await f.Billing.DidNotReceiveWithAnyArgs().ApplyVerifiedPaymentAsync(default!, default);
        Assert.AreEqual("Review", f.SavedPhase);
    }

    [TestMethod]
    public async Task Lost_receiver_response_retries_original_funds_with_new_processor()
    {
        var f = new Fixture();
        var receivers = new HashSet<Guid>();
        var calls = 0;
        f.Billing.ApplyVerifiedPaymentAsync(Arg.Any<TenantSubscriptionPaymentReceipt>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                receivers.Add(call.Arg<TenantSubscriptionPaymentReceipt>()!.PaymentOrderId);
                if (++calls == 1) throw new IOException("response lost after receiver commit");
                return Result<bool>.Success(true);
            });
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Processor().ProcessAsync(f.Work.Id, default));
        Assert.IsNull(f.SavedPhase);
        Assert.IsTrue((await f.Processor().ProcessAsync(f.Work.Id, default)).Value);
        Assert.AreEqual(1, receivers.Count);
        Assert.AreEqual("Applied", f.SavedPhase);
        await f.Commands.Received(2).ExecuteAsync(SubscriptionFulfillmentSql.Release, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Refund_during_remote_apply_cannot_be_overwritten_by_paid_completion()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(SubscriptionFulfillmentSql.Save, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Processor().ProcessAsync(f.Work.Id, default));
        await f.Commands.Received(1).ExecuteAsync(SubscriptionFulfillmentSql.Release, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Unsettled_funds_wait_without_granting()
    {
        var f = new Fixture();
        f.Funds = f.Funds with { TradeStateKey = PaymentTradeStateKeys.Refunding };
        Assert.IsFalse((await f.Processor().ProcessAsync(f.Work.Id, default)).Value);
        await f.Billing.DidNotReceiveWithAnyArgs().ApplyVerifiedPaymentAsync(default!, default);
        Assert.AreEqual("ReadyPaid", f.SavedPhase);
    }

    [TestMethod]
    public async Task Refund_message_without_successful_refund_authority_never_revokes()
    {
        var f = new Fixture();
        f.Work = f.Work with { Phase = "ReadyRefund", LastRefundId = Guid.CreateVersion7(), RefundAmountMinor = 100 };
        Assert.IsFalse((await f.Processor().ProcessAsync(f.Work.Id, default)).IsSuccess);
        await f.Billing.DidNotReceiveWithAnyArgs().ApplyVerifiedRefundAsync(default!, default);
        Assert.AreEqual("Review", f.SavedPhase);
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ITenantSubscriptionBillingPort Billing = Substitute.For<ITenantSubscriptionBillingPort>();
        private readonly IClock _clock = Substitute.For<IClock>();
        internal SubscriptionFulfillmentRecord Work;
        internal SubscriptionPaymentAuthorityRecord Funds;
        internal string? SavedPhase;
        internal Fixture()
        {
            var now = DateTimeOffset.UtcNow;
            Work = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), 100, "CNY", now.AddMinutes(-1), now.AddDays(30), Guid.CreateVersion7(), "provider-1", now,
                null, 0, "ReadyPaid", null, now, null, null, 1, now, now, 1);
            Funds = new(Work.PaymentOrderId!.Value, Work.TenantId, Work.MerchantConfigId, Work.Id, Work.AmountMinor,
                Work.Currency, PaymentTradeStateKeys.Succeeded, Work.ProviderTransactionId, now);
            _clock.UtcNow.Returns(now);
            Queries.QuerySingleOrDefaultAsync<SubscriptionFulfillmentRecord>(SubscriptionFulfillmentSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Work);
            Queries.QuerySingleOrDefaultAsync<SubscriptionPaymentAuthorityRecord>(SubscriptionFulfillmentSql.ReadFunds, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Funds);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Commands.ExecuteAsync(SubscriptionFulfillmentSql.Save, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                SavedPhase = (string)((IReadOnlyDictionary<string, object?>)call[1]!)["Phase"]!;
                return 1;
            });
            Billing.ApplyVerifiedPaymentAsync(Arg.Any<TenantSubscriptionPaymentReceipt>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        }
        internal SubscriptionFulfillmentProcessor Processor() => new(Queries, Commands, Billing, _clock);
    }
}
