using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>支付退款创建与渠道调用。</summary>
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
    WeChatNativePayClient weChatNativePayClient,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>为指定订单创建退款并调用微信退款 API。</summary>
    /// <param name="orderId">订单标识。</param>
    /// <param name="request">退款请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退款详情或稳定业务错误。</returns>
    public Task<Result<PaymentRefundResponse>> CreateForOrderAsync(
        Guid orderId,
        CreatePaymentRefundRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateForOrderCoreAsync(orderId, request, token),
            cancellationToken);

    private async Task<Result<PaymentRefundResponse>> CreateForOrderCoreAsync(
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
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.OrderNotFound,
                "The payment order was not found.",
                ErrorType.NotFound));
        }

        if (order.TradeStateKey != PaymentTradeStateKeys.Succeeded)
        {
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.OrderStateInvalid,
                "Only succeeded orders can be refunded.",
                ErrorType.Validation));
        }

        var refundAmount = request.AmountMinor ?? order.AmountMinor;
        if (refundAmount <= 0 || refundAmount > order.AmountMinor)
        {
            return ValidationFailure("Refund amount must be positive and not exceed the order amount.");
        }

        var merchantConfig = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", order.MerchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null || !merchantConfig.IsEnabled)
        {
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "Merchant configuration is unavailable.",
                ErrorType.Validation));
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
                    ("UpdatedAtUtc", null),
                    ("CompletedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        var providerResult = await weChatNativePayClient.CreateDomesticRefundAsync(
                merchantConfig,
                order.OutTradeNo,
                outRefundNo,
                refundAmount,
                order.AmountMinor,
                order.Currency,
                reason,
                cancellationToken)
            .ConfigureAwait(false);

        var refundStateKey = providerResult.Succeeded
            ? MapProviderRefundStatus(providerResult.StatusKey)
            : PaymentRefundStateKeys.Failed;
        var updatedAtUtc = clock.UtcNow;
        DateTimeOffset? completedAtUtc = refundStateKey == PaymentRefundStateKeys.Succeeded
            ? updatedAtUtc
            : null;
        var affected = await commandExecutor.ExecuteAsync(
                PaymentRefundSql.UpdateProviderResult,
                PaymentSqlParameters.Create(
                    ("RefundId", refundId),
                    ("RefundStateKey", refundStateKey),
                    ("ProviderRefundId", providerResult.ProviderRefundId),
                    ("FailMessage", providerResult.FailMessage),
                    ("UpdatedAtUtc", updatedAtUtc),
                    ("CompletedAtUtc", completedAtUtc),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.RefundInvalid,
                "The payment refund could not be updated after provider invocation.",
                ErrorType.Conflict));
        }

        if (providerResult.Succeeded && refundStateKey == PaymentRefundStateKeys.Succeeded)
        {
            await commandExecutor.ExecuteAsync(
                    PaymentOrderSql.UpdateTradeState,
                    PaymentSqlParameters.Create(
                        ("OrderId", order.Id),
                        ("TradeStateKey", PaymentTradeStateKeys.Refunded),
                        ("ProviderTransactionId", order.ProviderTransactionId),
                        ("FailMessage", null),
                        ("UpdatedAtUtc", updatedAtUtc),
                        ("PaidAtUtc", order.PaidAtUtc),
                        ("Version", order.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (providerResult.Succeeded)
        {
            await commandExecutor.ExecuteAsync(
                    PaymentOrderSql.UpdateTradeState,
                    PaymentSqlParameters.Create(
                        ("OrderId", order.Id),
                        ("TradeStateKey", PaymentTradeStateKeys.Refunding),
                        ("ProviderTransactionId", order.ProviderTransactionId),
                        ("FailMessage", null),
                        ("UpdatedAtUtc", updatedAtUtc),
                        ("PaidAtUtc", order.PaidAtUtc),
                        ("Version", order.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!providerResult.Succeeded)
        {
            return Result<PaymentRefundResponse>.Failure(new Error(
                PaymentErrorCodes.RefundProviderFailed,
                providerResult.FailMessage ?? "WeChat refund request failed.",
                ErrorType.Validation));
        }

        return await queries.GetByIdAsync(refundId, cancellationToken).ConfigureAwait(false);
    }

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

    private static Result<PaymentRefundResponse> ValidationFailure(string message) =>
        Result<PaymentRefundResponse>.Failure(new Error(
            PaymentErrorCodes.RefundInvalid,
            message,
            ErrorType.Validation));
}
