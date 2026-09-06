using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>与支付渠道对账并同步本地订单状态。</summary>
internal sealed class PaymentOrderReconciliationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentOrderQueryService queries,
    WeChatNativePayClient weChatNativePayClient,
    AlipayPagePayClient alipayPagePayClient,
    IClock clock)
{
    /// <summary>按订单标识查询渠道交易并回写本地状态。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步后的订单详情。</returns>
    public Task<Result<PaymentOrderResponse>> ReconcileAsync(
        Guid orderId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReconcileCoreAsync(orderId, token),
            cancellationToken);

    private async Task<Result<PaymentOrderResponse>> ReconcileCoreAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(
                PaymentOrderSql.FindById,
                PaymentSqlParameters.Create(("OrderId", orderId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderNotFound,
                "The payment order was not found.",
                ErrorType.NotFound));
        }

        if (order.TradeStateKey is PaymentTradeStateKeys.Refunded or PaymentTradeStateKeys.Refunding)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderStateInvalid,
                "Refunding orders cannot be reconciled.",
                ErrorType.Validation));
        }

        var merchantConfig = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", order.MerchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null || !merchantConfig.IsEnabled)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "Merchant configuration is unavailable.",
                ErrorType.Validation));
        }

        var providerSnapshot = await QueryProviderAsync(
                merchantConfig,
                order,
                cancellationToken)
            .ConfigureAwait(false);
        if (!providerSnapshot.Succeeded)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderProviderFailed,
                providerSnapshot.FailMessage ?? "Payment provider query failed.",
                ErrorType.Validation));
        }

        if (providerSnapshot.AmountMinor != order.AmountMinor)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.NotifyRejected,
                "Provider amount does not match the local order.",
                ErrorType.Validation));
        }

        var mappedState = MapTradeState(order.ChannelKey, providerSnapshot.TradeState);
        if (mappedState is null)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderProviderFailed,
                $"Unsupported provider trade state: {providerSnapshot.TradeState}",
                ErrorType.Validation));
        }

        if (mappedState == order.TradeStateKey
            && string.Equals(
                order.ProviderTransactionId,
                providerSnapshot.TransactionId,
                StringComparison.Ordinal))
        {
            return await queries.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var paidAtUtc = mappedState == PaymentTradeStateKeys.Succeeded ? now : order.PaidAtUtc;
        var affected = await commandExecutor.ExecuteAsync(
                PaymentOrderSql.UpdateTradeState,
                PaymentSqlParameters.Create(
                    ("OrderId", order.Id),
                    ("TradeStateKey", mappedState),
                    ("ProviderTransactionId", providerSnapshot.TransactionId),
                    ("FailMessage", null),
                    ("UpdatedAtUtc", now),
                    ("PaidAtUtc", paidAtUtc),
                    ("Version", order.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderInvalid,
                "The payment order could not be updated during reconciliation.",
                ErrorType.Conflict));
        }

        return await queries.GetByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderQuerySnapshot> QueryProviderAsync(
        PaymentMerchantConfigRecord merchantConfig,
        PaymentOrderRecord order,
        CancellationToken cancellationToken)
    {
        if (string.Equals(order.ChannelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal))
        {
            var weChatResult = await weChatNativePayClient.QueryTransactionByOutTradeNoAsync(
                    merchantConfig,
                    order.OutTradeNo,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ProviderQuerySnapshot(
                weChatResult.Succeeded,
                weChatResult.TradeState,
                weChatResult.TransactionId,
                weChatResult.AmountMinor,
                weChatResult.FailMessage);
        }

        if (string.Equals(order.ChannelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal))
        {
            var alipayResult = await alipayPagePayClient.QueryTradeByOutTradeNoAsync(
                    merchantConfig,
                    order.OutTradeNo,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ProviderQuerySnapshot(
                alipayResult.Succeeded,
                alipayResult.TradeStatus,
                alipayResult.TradeNo,
                alipayResult.AmountMinor,
                alipayResult.FailMessage);
        }

        return new ProviderQuerySnapshot(
            false,
            null,
            null,
            null,
            $"Unsupported payment channel: {order.ChannelKey}.");
    }

    private static string? MapTradeState(string channelKey, string? tradeState)
    {
        if (string.Equals(channelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal))
        {
            return tradeState switch
            {
                "SUCCESS" => PaymentTradeStateKeys.Succeeded,
                "NOTPAY" => PaymentTradeStateKeys.AwaitingPayment,
                "CLOSED" => PaymentTradeStateKeys.Closed,
                "PAYERROR" => PaymentTradeStateKeys.Failed,
                "REFUND" => PaymentTradeStateKeys.Refunded,
                _ => null,
            };
        }

        if (string.Equals(channelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal))
        {
            return tradeState switch
            {
                "TRADE_SUCCESS" => PaymentTradeStateKeys.Succeeded,
                "WAIT_BUYER_PAY" => PaymentTradeStateKeys.AwaitingPayment,
                "TRADE_CLOSED" => PaymentTradeStateKeys.Closed,
                _ => null,
            };
        }

        return null;
    }

    private sealed record ProviderQuerySnapshot(
        bool Succeeded,
        string? TradeState,
        string? TransactionId,
        long? AmountMinor,
        string? FailMessage);
}
