using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>查询已受理退款，再在本模块事务内同时收敛退款和订单；远程失败保留原状态。</summary>
internal sealed class PaymentRefundReconciliationService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, IWeChatNativePayClient client, IClock clock)
{
    internal async Task<Result<bool>> ReconcileAsync(PaymentOrderRecord order, CancellationToken cancellationToken)
    {
        var rows = await queries.QueryAsync<PaymentRefundRecord>(PaymentRefundSql.ListUnsettledByOrderId,
            PaymentSqlParameters.Create(("OrderId", order.Id)), cancellationToken).ConfigureAwait(false);
        var refund = rows.FirstOrDefault();
        var merchant = await queries.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(PaymentMerchantConfigSql.FindById,
            PaymentSqlParameters.Create(("MerchantConfigId", order.MerchantConfigId)), cancellationToken).ConfigureAwait(false);
        if (refund is null || merchant is null || refund.MerchantConfigId != merchant.Id)
            return Failure("No matching refund intent or merchant is available.");
        WeChatRefundQueryResult snapshot;
        try { snapshot = await client.QueryDomesticRefundAsync(merchant, refund.OutRefundNo, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Failure("Refund provider query failed."); }
        if (!snapshot.Succeeded || snapshot.OutRefundNo != refund.OutRefundNo || snapshot.AmountMinor != refund.AmountMinor
            || snapshot.Currency != refund.Currency || string.IsNullOrWhiteSpace(snapshot.ProviderRefundId))
            return Failure("Refund provider snapshot does not match the local intent.");
        var state = snapshot.StatusKey switch
        {
            "SUCCESS" => PaymentRefundStateKeys.Succeeded,
            "CLOSED" => PaymentRefundStateKeys.Closed,
            "PROCESSING" => PaymentRefundStateKeys.Processing,
            "ABNORMAL" => PaymentRefundStateKeys.Failed,
            _ => null,
        };
        if (state is null) return Failure("Refund provider returned an unknown status.");
        // 异常退款仍可能需要商户平台处理，不能释放订单后再创建一笔新的退款。
        var orderState = state == PaymentRefundStateKeys.Succeeded ? PaymentTradeStateKeys.Refunded
            : state == PaymentRefundStateKeys.Closed ? PaymentTradeStateKeys.Succeeded : PaymentTradeStateKeys.Refunding;
        return await transaction.ExecuteResultAsync(async token =>
        {
            var now = clock.UtcNow;
            var updated = await commands.ExecuteAsync(PaymentRefundSql.UpdateProviderResult,
                PaymentSqlParameters.Create(("RefundId", refund.Id), ("RefundStateKey", state),
                    ("ProviderRefundId", snapshot.ProviderRefundId), ("FailMessage", state == PaymentRefundStateKeys.Failed ? "Refund requires merchant intervention." : null),
                    ("UpdatedAtUtc", now), ("CompletedAtUtc", state is PaymentRefundStateKeys.Succeeded or PaymentRefundStateKeys.Closed ? now : null),
                    ("Version", refund.Version)), token).ConfigureAwait(false);
            if (updated != 1) return Failure("Refund changed during reconciliation.");
            updated = await commands.ExecuteAsync(PaymentOrderSql.ClaimTradeState,
                PaymentSqlParameters.Create(("OrderId", order.Id), ("TradeStateKey", orderState), ("FailMessage", null),
                    ("UpdatedAtUtc", now), ("Version", order.Version), ("ExpectedTradeStateKey", PaymentTradeStateKeys.Refunding)), token).ConfigureAwait(false);
            return updated == 1 ? Result<bool>.Success(true) : Failure("Order changed during reconciliation.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Result<bool> Failure(string message) => Result<bool>.Failure(new Error(
        PaymentErrorCodes.RefundProviderUnknown, message, ErrorType.Conflict));
}
