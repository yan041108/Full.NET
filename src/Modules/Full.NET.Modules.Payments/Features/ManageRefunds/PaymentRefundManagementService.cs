using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>支付退款创建与渠道调用；先领取订单并提交退款意图，再在事务外调用渠道。</summary>
/// <param name="queryExecutor">当前模块查询执行器。</param>
/// <param name="commandExecutor">当前模块写入执行器。</param>
/// <param name="transaction">本地命令事务。</param>
/// <param name="queries">退款响应查询服务。</param>
/// <param name="weChatNativePayClient">微信渠道客户端。</param>
/// <param name="clock">业务时钟。</param>
/// <param name="idGenerator">退款唯一标识生成器。</param>
internal sealed class PaymentRefundManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentRefundQueryService queries,
    IWeChatNativePayClient weChatNativePayClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>为指定订单创建退款并调用微信退款 API。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="request">退款请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款详情或稳定业务错误。</returns>
    public async Task<Result<PaymentRefundResponse>> CreateForOrderAsync(
        Guid orderId,
        CreatePaymentRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await transaction.ExecuteResultAsync(
                token => PersistRefundIntentAsync(orderId, request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<PaymentRefundResponse>.Failure(prepared.Error!);
        }

        var intent = prepared.Value!;
        WeChatRefundResult providerResult;
        try
        {
            // 退款请求不可回滚，必须发生在订单领取和退款意图提交之后。
            providerResult = await weChatNativePayClient.CreateDomesticRefundAsync(
                    intent.MerchantConfig,
                    intent.OutTradeNo,
                    intent.OutRefundNo,
                    intent.AmountMinor,
                    intent.OrderAmountMinor,
                    intent.Currency,
                    intent.Reason,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (UnknownExternalSideEffect.Matches(exception))
        {
            await TryPersistRefundOutcomeAsync(
                    intent,
                    PaymentRefundStateKeys.ProviderUnknown,
                    null,
                    exception.Message,
                    keepOrderRefunding: true,
                    cancellationToken)
                .ConfigureAwait(false);
            return UnknownRefund(exception.Message);
        }

        var refundStateKey = providerResult.Succeeded
            ? MapProviderRefundStatus(providerResult.StatusKey)
            : PaymentRefundStateKeys.Failed;
        try
        {
            var persisted = await TryPersistRefundOutcomeAsync(
                    intent,
                    refundStateKey,
                    providerResult.ProviderRefundId,
                    providerResult.FailMessage,
                    keepOrderRefunding: providerResult.Succeeded,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!persisted)
            {
                return UnknownRefund("The payment refund could not be updated after provider invocation.");
            }
        }
        catch (Exception exception)
        {
            return UnknownRefund(exception.Message);
        }

        if (!providerResult.Succeeded)
        {
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.RefundProviderFailed,
                providerResult.FailMessage ?? "WeChat refund request failed.",
                ErrorType.Validation));
        }

        return await queries.GetByIdAsync(intent.RefundId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在短事务中插入退款意图并领取订单，或恢复崩溃留下的可重试意图。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="request">退款请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的退款意图；并发冲突时回滚且不调用渠道。</returns>
    private async Task<Result<PreparedPaymentRefund>> PersistRefundIntentAsync(
        Guid orderId,
        CreatePaymentRefundRequest request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 128)
        {
            return ValidationFailure("Refund reason is required and must be at most 128 characters.");
        }

        var order = await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(
                PaymentOrderSql.FindById,
                PaymentSqlParameters.Create(("OrderId", orderId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.OrderNotFound,
                "The payment order was not found.",
                ErrorType.NotFound));
        }

        var merchantConfig = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", order.MerchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null || !merchantConfig.IsEnabled)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "Merchant configuration is unavailable.",
                ErrorType.Validation));
        }

        if (order.TradeStateKey == PaymentTradeStateKeys.Refunding)
        {
            return await RecoverRefundIntentAsync(order, merchantConfig, cancellationToken)
                .ConfigureAwait(false);
        }

        if (order.TradeStateKey != PaymentTradeStateKeys.Succeeded)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.OrderStateInvalid,
                "Only succeeded orders can be refunded.",
                ErrorType.Validation));
        }

        var refundAmount = request.AmountMinor ?? order.AmountMinor;
        if (refundAmount <= 0 || refundAmount > order.AmountMinor)
        {
            return ValidationFailure("Refund amount must be positive and not exceed the order amount.");
        }

        var now = clock.UtcNow;
        var refundId = idGenerator.NewId();
        var outRefundNo = BuildOutRefundNo(refundId, now);
        await commandExecutor.ExecuteAsync(
                PaymentRefundSql.Insert,
                PaymentSqlParameters.Create(
                    ("Id", refundId),
                    ("TenantId", order.TenantId),
                    ("OrderId", order.Id),
                    ("MerchantConfigId", order.MerchantConfigId),
                    ("OutTradeNo", order.OutTradeNo),
                    ("OutRefundNo", outRefundNo),
                    ("RefundStateKey", PaymentRefundStateKeys.Created),
                    ("AmountMinor", refundAmount),
                    ("Currency", order.Currency),
                    ("Reason", reason),
                    ("ProviderRefundId", null),
                    ("FailMessage", null),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("CompletedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        var claimed = await commandExecutor.ExecuteAsync(
                PaymentOrderSql.ClaimTradeState,
                PaymentSqlParameters.Create(
                    ("OrderId", order.Id),
                    ("TradeStateKey", PaymentTradeStateKeys.Refunding),
                    ("FailMessage", null),
                    ("UpdatedAtUtc", now),
                    ("Version", order.Version),
                    ("ExpectedTradeStateKey", PaymentTradeStateKeys.Succeeded)),
                cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.RefundInProgress,
                "Another refund request already claimed this order.",
                ErrorType.Conflict));
        }

        return Result<PreparedPaymentRefund>.Success(new PreparedPaymentRefund(
            refundId,
            order.Id,
            order.Version + 1,
            1,
            merchantConfig,
            order.OutTradeNo,
            outRefundNo,
            refundAmount,
            order.AmountMinor,
            order.Currency,
            reason,
            order.ProviderTransactionId,
            order.PaidAtUtc));
    }

    /// <summary>恢复退款中订单上尚未完成的意图；租约未过期时拒绝并发调用。</summary>
    /// <param name="order">当前为退款中的订单。</param>
    /// <param name="merchantConfig">订单所属商户配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可重试的退款意图或冲突。</returns>
    private async Task<Result<PreparedPaymentRefund>> RecoverRefundIntentAsync(
        PaymentOrderRecord order,
        PaymentMerchantConfigRecord merchantConfig,
        CancellationToken cancellationToken)
    {
        var recoverable = await queryExecutor.QueryAsync<PaymentRefundRecord>(
                PaymentRefundSql.ListRecoverableByOrderId,
                PaymentSqlParameters.Create(("OrderId", order.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var refund = recoverable.FirstOrDefault();
        if (refund is null)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.OrderStateInvalid,
                "The order is already being refunded.",
                ErrorType.Validation));
        }

        if (refund.RefundStateKey == PaymentRefundStateKeys.Created
            && UnknownExternalSideEffect.HasActiveLease(refund.UpdatedAtUtc ?? refund.CreatedAtUtc, clock.UtcNow))
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.RefundInProgress,
                "A refund request is already invoking the payment provider.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var claimed = await commandExecutor.ExecuteAsync(
                PaymentRefundSql.ClaimInvocation,
                PaymentSqlParameters.Create(
                    ("RefundId", refund.Id),
                    ("UpdatedAtUtc", now),
                    ("Version", refund.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            return Result<PreparedPaymentRefund>.Failure(new Error(
                PaymentErrorCodes.RefundInProgress,
                "Another refund request already claimed this refund intent.",
                ErrorType.Conflict));
        }

        return Result<PreparedPaymentRefund>.Success(new PreparedPaymentRefund(
            refund.Id,
            order.Id,
            order.Version,
            refund.Version + 1,
            merchantConfig,
            refund.OutTradeNo,
            refund.OutRefundNo,
            refund.AmountMinor,
            order.AmountMinor,
            refund.Currency,
            refund.Reason,
            order.ProviderTransactionId,
            order.PaidAtUtc));
    }

    /// <summary>用独立短事务回写退款结果，并在明确失败时把订单从退款中释放回成功。</summary>
    /// <param name="intent">已提交的退款意图。</param>
    /// <param name="refundStateKey">退款状态。</param>
    /// <param name="providerRefundId">渠道退款标识。</param>
    /// <param name="failMessage">失败或未知摘要。</param>
    /// <param name="keepOrderRefunding">渠道已受理时保持订单退款中或已退款。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款行更新成功时返回 true。</returns>
    private Task<bool> TryPersistRefundOutcomeAsync(
        PreparedPaymentRefund intent,
        string refundStateKey,
        string? providerRefundId,
        string? failMessage,
        bool keepOrderRefunding,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(
            async token =>
            {
                var now = clock.UtcNow;
                DateTimeOffset? completedAtUtc = refundStateKey == PaymentRefundStateKeys.Succeeded
                    ? now
                    : null;
                var affected = await commandExecutor.ExecuteAsync(
                        PaymentRefundSql.UpdateProviderResult,
                        PaymentSqlParameters.Create(
                            ("RefundId", intent.RefundId),
                            ("RefundStateKey", refundStateKey),
                            ("ProviderRefundId", providerRefundId),
                            ("FailMessage", failMessage),
                            ("UpdatedAtUtc", now),
                            ("CompletedAtUtc", completedAtUtc),
                            ("Version", intent.RefundVersion)),
                        token)
                    .ConfigureAwait(false);
                if (affected == 0)
                {
                    return false;
                }

                var orderState = refundStateKey == PaymentRefundStateKeys.Succeeded
                    ? PaymentTradeStateKeys.Refunded
                    : keepOrderRefunding
                        ? PaymentTradeStateKeys.Refunding
                        : PaymentTradeStateKeys.Succeeded;
                await commandExecutor.ExecuteAsync(
                        PaymentOrderSql.ClaimTradeState,
                        PaymentSqlParameters.Create(
                            ("OrderId", intent.OrderId),
                            ("TradeStateKey", orderState),
                            ("FailMessage", keepOrderRefunding ? null : failMessage),
                            ("UpdatedAtUtc", now),
                            ("Version", intent.OrderVersion),
                            ("ExpectedTradeStateKey", PaymentTradeStateKeys.Refunding)),
                        token)
                    .ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <summary>将微信退款状态映射到本地稳定状态键。</summary>
    /// <param name="statusKey">渠道状态。</param>
    /// <returns>本地退款状态键。</returns>
    private static string MapProviderRefundStatus(string? statusKey) =>
        statusKey switch
        {
            "SUCCESS" => PaymentRefundStateKeys.Succeeded,
            "PROCESSING" => PaymentRefundStateKeys.Processing,
            "CLOSED" => PaymentRefundStateKeys.Closed,
            "ABNORMAL" => PaymentRefundStateKeys.Failed,
            _ => PaymentRefundStateKeys.Processing,
        };

    /// <summary>完整保留退款唯一标识，使同一时刻的不同退款不会共用渠道编号。</summary>
    /// <param name="refundId">已生成且持久化关联的退款唯一标识。</param>
    /// <param name="createdAtUtc">退款创建时间；不再参与截断编号，保留调用兼容性。</param>
    /// <returns>固定 32 位的可重放退款编号。</returns>
    private static string BuildOutRefundNo(Guid refundId, DateTimeOffset createdAtUtc) =>
        refundId.ToString("N");

    /// <summary>构造退款校验失败结果。</summary>
    /// <param name="message">校验消息。</param>
    /// <returns>稳定校验错误。</returns>
    private static Result<PreparedPaymentRefund> ValidationFailure(string message) =>
        Result<PreparedPaymentRefund>.Failure(new Error(
            PaymentErrorCodes.RefundInvalid,
            message,
            ErrorType.Validation));

    /// <summary>构造退款渠道结果未知错误。</summary>
    /// <param name="message">未知原因摘要。</param>
    /// <returns>未知状态错误。</returns>
    private static Result<PaymentRefundResponse> UnknownRefund(string message) =>
        Result<PaymentRefundResponse>.Failure(new Error(
            PaymentErrorCodes.RefundProviderUnknown,
            message,
            ErrorType.Conflict));

    /// <summary>已提交的退款意图，包含渠道幂等键和订单领取后的版本。</summary>
    /// <param name="RefundId">退款标识。</param>
    /// <param name="OrderId">订单标识。</param>
    /// <param name="OrderVersion">领取退款中之后的订单版本。</param>
    /// <param name="RefundVersion">意图提交后的退款版本。</param>
    /// <param name="MerchantConfig">商户配置。</param>
    /// <param name="OutTradeNo">商户订单号。</param>
    /// <param name="OutRefundNo">商户退款单号。</param>
    /// <param name="AmountMinor">退款金额。</param>
    /// <param name="OrderAmountMinor">原订单金额。</param>
    /// <param name="Currency">货币代码。</param>
    /// <param name="Reason">退款原因。</param>
    /// <param name="ProviderTransactionId">渠道交易标识。</param>
    /// <param name="PaidAtUtc">原支付成功时间。</param>
    private sealed record PreparedPaymentRefund(
        Guid RefundId,
        Guid OrderId,
        int OrderVersion,
        int RefundVersion,
        PaymentMerchantConfigRecord MerchantConfig,
        string OutTradeNo,
        string OutRefundNo,
        long AmountMinor,
        long OrderAmountMinor,
        string Currency,
        string Reason,
        string? ProviderTransactionId,
        DateTimeOffset? PaidAtUtc);
}
