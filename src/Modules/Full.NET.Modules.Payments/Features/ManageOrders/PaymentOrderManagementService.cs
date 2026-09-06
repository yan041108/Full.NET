using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>支付订单创建与渠道下单。</summary>
internal sealed class PaymentOrderManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentOrderQueryService queries,
    WeChatNativePayClient weChatNativePayClient,
    AlipayPagePayClient alipayPagePayClient,
    IIdentityActiveTenantDirectory activeTenants,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>创建支付订单并调用对应渠道下单。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<PaymentOrderResponse>> CreateAsync(
        CreatePaymentOrderRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    private async Task<Result<PaymentOrderResponse>> CreateCoreAsync(
        CreatePaymentOrderRequest request,
        CancellationToken cancellationToken)
    {
        var validationMessage = PaymentMerchantFieldValidator.ValidateOrderRequest(
            request.AmountMinor,
            request.Currency,
            request.Subject,
            request.Description,
            request.ChannelKey);
        if (validationMessage is not null)
        {
            return ValidationFailure<PaymentOrderResponse>(validationMessage);
        }

        var tenantExists = await activeTenants.IsActiveTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantExists)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.TenantNotFound,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        var merchantConfig = await ResolveMerchantConfigAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "No enabled payment merchant configuration is available for this tenant.",
                ErrorType.Validation));
        }

        if (!merchantConfig.IsEnabled)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "The selected merchant configuration is not enabled.",
                ErrorType.Validation));
        }

        if (!string.IsNullOrWhiteSpace(request.ChannelKey)
            && !string.Equals(
                merchantConfig.ChannelKey,
                request.ChannelKey.Trim(),
                StringComparison.Ordinal))
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "The selected merchant configuration does not match the requested channel.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var orderId = idGenerator.NewId();
        var outTradeNo = BuildOutTradeNo(orderId, now);
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Subject.Trim()
            : request.Description.Trim();

        await commandExecutor.ExecuteAsync(
                PaymentOrderSql.Insert,
                PaymentSqlParameters.Create(
                    ("Id", orderId),
                    ("TenantId", request.TenantId),
                    ("MerchantConfigId", merchantConfig.Id),
                    ("ChannelKey", merchantConfig.ChannelKey),
                    ("OutTradeNo", outTradeNo),
                    ("TradeStateKey", PaymentTradeStateKeys.Created),
                    ("AmountMinor", request.AmountMinor),
                    ("Currency", request.Currency.Trim().ToUpperInvariant()),
                    ("Subject", request.Subject.Trim()),
                    ("Description", NormalizeOptional(request.Description)),
                    ("CodeUrl", null),
                    ("ProviderTransactionId", null),
                    ("FailMessage", null),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("PaidAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        var providerResult = await InvokeProviderAsync(
                merchantConfig,
                outTradeNo,
                request.AmountMinor,
                request.Currency.Trim().ToUpperInvariant(),
                description,
                request.Subject.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        var tradeStateKey = providerResult.Succeeded
            ? PaymentTradeStateKeys.AwaitingPayment
            : PaymentTradeStateKeys.Failed;
        var updatedAtUtc = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                PaymentOrderSql.UpdateProviderResult,
                PaymentSqlParameters.Create(
                    ("OrderId", orderId),
                    ("TradeStateKey", tradeStateKey),
                    ("CodeUrl", providerResult.PayUrl),
                    ("ProviderTransactionId", null),
                    ("FailMessage", providerResult.FailMessage),
                    ("UpdatedAtUtc", updatedAtUtc),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderInvalid,
                "The payment order could not be updated after provider invocation.",
                ErrorType.Conflict));
        }

        if (!providerResult.Succeeded)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderProviderFailed,
                providerResult.FailMessage ?? "Payment provider request failed.",
                ErrorType.Validation));
        }

        return await queries.GetByIdAsync(orderId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ProviderInvocationResult> InvokeProviderAsync(
        PaymentMerchantConfigRecord merchantConfig,
        string outTradeNo,
        long amountMinor,
        string currency,
        string description,
        string subject,
        CancellationToken cancellationToken)
    {
        if (string.Equals(merchantConfig.ChannelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal))
        {
            var weChatResult = await weChatNativePayClient.CreateNativeOrderAsync(
                    merchantConfig,
                    outTradeNo,
                    amountMinor,
                    currency,
                    description,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ProviderInvocationResult(
                weChatResult.Succeeded,
                weChatResult.CodeUrl,
                weChatResult.FailMessage);
        }

        if (string.Equals(merchantConfig.ChannelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal))
        {
            var alipayResult = await alipayPagePayClient.CreatePagePayUrlAsync(
                    merchantConfig,
                    outTradeNo,
                    amountMinor,
                    currency,
                    subject,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ProviderInvocationResult(
                alipayResult.Succeeded,
                alipayResult.PayUrl,
                alipayResult.FailMessage);
        }

        return new ProviderInvocationResult(
            false,
            null,
            $"Unsupported payment channel: {merchantConfig.ChannelKey}.");
    }

    private async Task<PaymentMerchantConfigRecord?> ResolveMerchantConfigAsync(
        CreatePaymentOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MerchantConfigId is Guid merchantConfigId)
        {
            var explicitConfig = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                    PaymentMerchantConfigSql.FindById,
                    PaymentSqlParameters.Create(("MerchantConfigId", merchantConfigId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (explicitConfig is null || explicitConfig.TenantId != request.TenantId)
            {
                return null;
            }

            return explicitConfig;
        }

        var channelKey = string.IsNullOrWhiteSpace(request.ChannelKey)
            ? PaymentChannelKeys.WeChatNative
            : request.ChannelKey.Trim();

        var defaultStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => PaymentMerchantConfigSql.FindDefaultForTenant,
            DatabaseProvider.MySql => PaymentMerchantConfigSql.FindDefaultForTenantMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

        return await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                defaultStatement,
                PaymentSqlParameters.Create(
                    ("TenantId", request.TenantId),
                    ("ChannelKey", channelKey)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildOutTradeNo(Guid orderId, DateTimeOffset createdAtUtc) =>
        $"FN{createdAtUtc:yyyyMMddHHmmss}{orderId.ToString("N")[..8].ToUpperInvariant()}";

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            PaymentErrorCodes.OrderInvalid,
            message,
            ErrorType.Validation));

    private sealed record ProviderInvocationResult(
        bool Succeeded,
        string? PayUrl,
        string? FailMessage);
}
