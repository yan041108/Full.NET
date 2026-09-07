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

/// <summary>支付订单创建与渠道下单；先提交本地意图，再在事务外调用渠道。</summary>
/// <param name="queryExecutor">当前模块查询执行器。</param>
/// <param name="commandExecutor">当前模块写入执行器。</param>
/// <param name="transaction">本地命令事务。</param>
/// <param name="queries">订单响应查询服务。</param>
/// <param name="weChatNativePayClient">微信渠道客户端。</param>
/// <param name="alipayPagePayClient">支付宝渠道客户端。</param>
    /// <param name="activeTenants">权威租户状态目录；必须在事务外调用。</param>
/// <param name="clock">业务时钟。</param>
/// <param name="idGenerator">业务唯一标识生成器。</param>
/// <param name="databaseOptions">数据库提供程序选项。</param>
internal sealed class PaymentOrderManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentOrderQueryService queries,
    IWeChatNativePayClient weChatNativePayClient,
    IAlipayPagePayClient alipayPagePayClient,
    IIdentityActiveTenantDirectory activeTenants,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>校验活动租户后创建支付订单并调用对应渠道下单。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误；渠道结果未知时返回未知错误且保留已提交意图。</returns>
    public async Task<Result<PaymentOrderResponse>> CreateAsync(
        CreatePaymentOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantExists = await activeTenants.IsActiveTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantExists)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.TenantNotFound,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        var prepared = await transaction.ExecuteResultAsync(
                token => PersistOrderIntentAsync(request, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<PaymentOrderResponse>.Failure(prepared.Error!);
        }

        var intent = prepared.Value!;
        ProviderInvocationResult providerResult;
        try
        {
            // 渠道下单不可回滚，必须发生在意图事务提交之后。
            providerResult = await InvokeProviderAsync(
                    intent.MerchantConfig,
                    intent.OutTradeNo,
                    intent.AmountMinor,
                    intent.Currency,
                    intent.Description,
                    intent.Subject,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (UnknownExternalSideEffect.Matches(exception))
        {
            await TryPersistProviderOutcomeAsync(
                    intent,
                    PaymentTradeStateKeys.ProviderUnknown,
                    null,
                    exception.Message,
                    cancellationToken)
                .ConfigureAwait(false);
            return UnknownOrder(exception.Message);
        }

        var tradeStateKey = providerResult.Succeeded
            ? PaymentTradeStateKeys.AwaitingPayment
            : PaymentTradeStateKeys.Failed;
        try
        {
            var persisted = await TryPersistProviderOutcomeAsync(
                    intent,
                    tradeStateKey,
                    providerResult.PayUrl,
                    providerResult.FailMessage,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!persisted)
            {
                return UnknownOrder("The payment order could not be updated after provider invocation.");
            }
        }
        catch (Exception exception)
        {
            // 渠道已成功或结果未知时，本地回写失败不能撤销外部订单，只能保留已提交意图供对账。
            return UnknownOrder(exception.Message);
        }

        if (!providerResult.Succeeded)
        {
            return Result<PaymentOrderResponse>.Failure(new Error(
                PaymentErrorCodes.OrderProviderFailed,
                providerResult.FailMessage ?? "Payment provider request failed.",
                ErrorType.Validation));
        }

        return await queries.GetByIdAsync(intent.OrderId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在短事务中写入渠道幂等键和 created 意图，不调用外部接口。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的订单意图；校验失败时回滚且不产生渠道副作用。</returns>
    private async Task<Result<PreparedPaymentOrder>> PersistOrderIntentAsync(
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
            return ValidationFailure<PreparedPaymentOrder>(validationMessage);
        }

        var merchantConfig = await ResolveMerchantConfigAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null)
        {
            return Result<PreparedPaymentOrder>.Failure(new Error(
                PaymentErrorCodes.MerchantConfigUnavailable,
                "No enabled payment merchant configuration is available for this tenant.",
                ErrorType.Validation));
        }

        if (!merchantConfig.IsEnabled)
        {
            return Result<PreparedPaymentOrder>.Failure(new Error(
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
            return Result<PreparedPaymentOrder>.Failure(new Error(
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
                    ("UpdatedAtUtc", now),
                    ("PaidAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<PreparedPaymentOrder>.Success(new PreparedPaymentOrder(
            orderId,
            merchantConfig,
            outTradeNo,
            request.AmountMinor,
            request.Currency.Trim().ToUpperInvariant(),
            request.Subject.Trim(),
            description,
            1));
    }

    /// <summary>用独立短事务回写渠道结果；失败时保留 created/unknown 供对账，不抛给调用方伪装成功。</summary>
    /// <param name="intent">已提交的订单意图。</param>
    /// <param name="tradeStateKey">要写入的交易状态。</param>
    /// <param name="payUrl">渠道支付链接。</param>
    /// <param name="failMessage">失败或未知摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功更新至少一行时返回 true。</returns>
    private Task<bool> TryPersistProviderOutcomeAsync(
        PreparedPaymentOrder intent,
        string tradeStateKey,
        string? payUrl,
        string? failMessage,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(
            async token =>
            {
                var affected = await commandExecutor.ExecuteAsync(
                        PaymentOrderSql.UpdateProviderResult,
                        PaymentSqlParameters.Create(
                            ("OrderId", intent.OrderId),
                            ("TradeStateKey", tradeStateKey),
                            ("CodeUrl", payUrl),
                            ("ProviderTransactionId", null),
                            ("FailMessage", failMessage),
                            ("UpdatedAtUtc", clock.UtcNow),
                            ("Version", intent.Version)),
                        token)
                    .ConfigureAwait(false);
                return affected > 0;
            },
            cancellationToken);

    /// <summary>按商户渠道调用微信或支付宝；该方法必须在意图事务提交后执行。</summary>
    /// <param name="merchantConfig">商户配置。</param>
    /// <param name="outTradeNo">商户订单号。</param>
    /// <param name="amountMinor">订单金额。</param>
    /// <param name="currency">货币代码。</param>
    /// <param name="description">商品描述。</param>
    /// <param name="subject">商品标题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>渠道调用结果。</returns>
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

    /// <summary>按请求解析启用中的商户配置。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>商户配置；不存在或不属于该租户时返回 null。</returns>
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

    /// <summary>完整保留业务标识，避免 UUID v7 时间前缀在并发下产生相同渠道编号。</summary>
    /// <param name="orderId">已生成且持久化关联的订单唯一标识。</param>
    /// <param name="createdAtUtc">订单创建时间；不再参与截断编号，保留调用兼容性。</param>
    /// <returns>固定 32 位的可重放订单编号。</returns>
    private static string BuildOutTradeNo(Guid orderId, DateTimeOffset createdAtUtc) =>
        orderId.ToString("N");

    /// <summary>规范化可选文本字段。</summary>
    /// <param name="value">原始文本。</param>
    /// <returns>空白时返回 null。</returns>
    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    /// <summary>构造订单校验失败结果。</summary>
    /// <typeparam name="T">结果值类型。</typeparam>
    /// <param name="message">校验消息。</param>
    /// <returns>稳定校验错误。</returns>
    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            PaymentErrorCodes.OrderInvalid,
            message,
            ErrorType.Validation));

    /// <summary>构造渠道结果未知错误，提示必须通过对账收敛。</summary>
    /// <param name="message">未知原因摘要。</param>
    /// <returns>未知状态错误。</returns>
    private static Result<PaymentOrderResponse> UnknownOrder(string message) =>
        Result<PaymentOrderResponse>.Failure(new Error(
            PaymentErrorCodes.OrderProviderUnknown,
            message,
            ErrorType.Conflict));

    /// <summary>已提交的支付订单意图，包含后续渠道调用所需的稳定幂等键。</summary>
    /// <param name="OrderId">订单标识。</param>
    /// <param name="MerchantConfig">下单使用的商户配置。</param>
    /// <param name="OutTradeNo">商户订单号。</param>
    /// <param name="AmountMinor">订单金额。</param>
    /// <param name="Currency">货币代码。</param>
    /// <param name="Subject">商品标题。</param>
    /// <param name="Description">商品描述。</param>
    /// <param name="Version">意图提交后的乐观版本。</param>
    private sealed record PreparedPaymentOrder(
        Guid OrderId,
        PaymentMerchantConfigRecord MerchantConfig,
        string OutTradeNo,
        long AmountMinor,
        string Currency,
        string Subject,
        string Description,
        int Version);

    /// <summary>渠道下单结果。</summary>
    /// <param name="Succeeded">渠道是否明确成功。</param>
    /// <param name="PayUrl">支付链接。</param>
    /// <param name="FailMessage">明确失败摘要。</param>
    private sealed record ProviderInvocationResult(
        bool Succeeded,
        string? PayUrl,
        string? FailMessage);
}
