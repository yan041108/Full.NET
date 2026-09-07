using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>与支付渠道对账并同步本地订单状态；渠道查询必须在事务外执行。</summary>
/// <param name="queryExecutor">当前模块查询执行器。</param>
/// <param name="commandExecutor">当前模块写入执行器。</param>
/// <param name="transaction">本地命令事务。</param>
/// <param name="queries">订单响应查询服务。</param>
/// <param name="weChatNativePayClient">微信渠道客户端。</param>
/// <param name="alipayPagePayClient">支付宝渠道客户端。</param>
/// <param name="clock">业务时钟。</param>
internal sealed class PaymentOrderReconciliationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentOrderQueryService queries,
    IWeChatNativePayClient weChatNativePayClient,
    IAlipayPagePayClient alipayPagePayClient,
    IClock clock)
{
    /// <summary>按订单标识查询渠道交易并回写本地状态。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>同步后的订单详情。</returns>
    public async Task<Result<PaymentOrderResponse>> ReconcileAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
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

        ProviderQuerySnapshot providerSnapshot;
        try
        {
            // 对账查询是远程副作用观察，不能占用本地事务连接。
            providerSnapshot = await QueryProviderAsync(merchantConfig, order, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderProviderUnknown,
                exception.Message,
                ErrorType.Conflict));
        }

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

        return await transaction.ExecuteResultAsync(
                token => ApplyProviderSnapshotAsync(order, providerSnapshot, mappedState, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>在短事务中按当前版本回写对账快照。</summary>
    /// <param name="order">对账前读取的订单。</param>
    /// <param name="providerSnapshot">渠道查询快照。</param>
    /// <param name="mappedState">映射后的本地状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的订单详情。</returns>
    private async Task<Result<PaymentOrderResponse>> ApplyProviderSnapshotAsync(
        PaymentOrderRecord order,
        ProviderQuerySnapshot providerSnapshot,
        string mappedState,
        CancellationToken cancellationToken)
    {
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

        return await queries.GetByIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按渠道查询远程交易快照。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="order">本地订单。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询快照。</returns>
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

    /// <summary>将渠道交易状态映射为本地稳定键。</summary>
    /// <param name="channelKey">支付渠道键。</param>
    /// <param name="tradeState">渠道状态。</param>
    /// <returns>本地状态；无法映射时返回 null。</returns>
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

    /// <summary>渠道交易查询快照。</summary>
    /// <param name="Succeeded">查询是否明确成功。</param>
    /// <param name="TradeState">渠道交易状态。</param>
    /// <param name="TransactionId">渠道交易标识。</param>
    /// <param name="AmountMinor">渠道金额。</param>
    /// <param name="FailMessage">失败摘要。</param>
    private sealed record ProviderQuerySnapshot(
        bool Succeeded,
        string? TradeState,
        string? TransactionId,
        long? AmountMinor,
        string? FailMessage);
}
