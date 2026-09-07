using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;

namespace Full.NET.Modules.Payments.Features.ReceiveWeChatNotify;

/// <summary>处理微信支付结果通知：验签、解密、幂等与订单状态更新。</summary>
/// <param name="queryExecutor">支付模块查询执行器。</param>
/// <param name="commandExecutor">支付模块写入执行器。</param>
/// <param name="transaction">订单与回执的本地事务。</param>
/// <param name="secretProtector">商户密钥保护器。</param>
/// <param name="certificateResolver">平台验签公钥解析器。</param>
/// <param name="clock">回执业务时钟。</param>
/// <param name="idGenerator">回执唯一标识生成器。</param>
internal sealed partial class PaymentWeChatNotifyService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentSecretProtector secretProtector,
    IWeChatPayPlatformCertificateResolver certificateResolver,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const int MaxPayloadSummaryLength = 480;

    /// <summary>处理微信 Native 支付通知。</summary>
    /// <param name="merchantConfigId">商户配置标识（通知 URL 路径参数）。</param>
    /// <param name="requestPath">请求路径，用于验签。</param>
    /// <param name="timestamp">Wechatpay-Timestamp。</param>
    /// <param name="nonce">Wechatpay-Nonce。</param>
    /// <param name="signature">Wechatpay-Signature。</param>
    /// <param name="platformSerial">Wechatpay-Serial。</param>
    /// <param name="rawBody">原始请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>微信协议 ACK 响应。</returns>
    public async Task<WeChatPayNotifyAckResponse> HandleAsync(
        Guid merchantConfigId,
        string requestPath,
        string timestamp,
        string nonce,
        string signature,
        string platformSerial,
        string rawBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await transaction.ExecuteAsync(
                token => HandleCoreAsync(merchantConfigId, requestPath, timestamp, nonce, signature,
                    platformSerial, rawBody, token), cancellationToken).ConfigureAwait(false);
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 必须先让整个本地事务回滚；竞争失败不等于已成功处理，要求平台重试后读取胜者回执。
            return FailAck("Concurrent notification receipt; retry the notification.");
        }
    }

    /// <summary>仅把已验签、商户与订单归属一致的通知应用到允许的状态转换。</summary>
    /// <param name="merchantConfigId">验签商户配置标识。</param>
    /// <param name="requestPath">接收回调的路径。</param>
    /// <param name="timestamp">原始通知时间戳。</param>
    /// <param name="nonce">原始通知随机串。</param>
    /// <param name="signature">通知签名。</param>
    /// <param name="platformSerial">平台公钥序列号。</param>
    /// <param name="rawBody">未改写的通知正文。</param>
    /// <param name="cancellationToken">操作取消令牌。</param>
    /// <returns>协议应答，不将拒绝回执伪装成处理成功。</returns>
    private async Task<WeChatPayNotifyAckResponse> HandleCoreAsync(
        Guid merchantConfigId,
        string requestPath,
        string timestamp,
        string nonce,
        string signature,
        string platformSerial,
        string rawBody,
        CancellationToken cancellationToken)
    {
        var merchantConfig = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", merchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (merchantConfig is null || !merchantConfig.IsEnabled)
        {
            return FailAck("Merchant configuration is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(merchantConfig.ApiV3KeyProtected))
        {
            return FailAck("Merchant API v3 key is not configured.");
        }

        var publicKeyPem = await certificateResolver.ResolvePublicKeyPemAsync(
                merchantConfig,
                platformSerial,
                cancellationToken)
            .ConfigureAwait(false);
        if (publicKeyPem is null
            || !WeChatPaySignatureVerifier.Verify(
                publicKeyPem,
                timestamp,
                nonce,
                rawBody,
                signature,
                requestPath))
        {
            return FailAck("Notify signature verification failed.");
        }

        var envelope = JsonSerializer.Deserialize(rawBody, NotifyJsonContext.Default.WeChatNotifyEnvelope);
        if (envelope?.Resource is null
            || string.IsNullOrWhiteSpace(envelope.Id)
            || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            return FailAck("Notify envelope is invalid.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<PaymentNotifyReceiptRecord>(
                PaymentNotifyReceiptSql.FindByProviderNotifyId,
                PaymentSqlParameters.Create(
                    ("MerchantConfigId", merchantConfigId),
                    ("ProviderNotifyId", envelope.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.ProcessStatusKey is PaymentNotifyProcessStatusKeys.Processed
                or PaymentNotifyProcessStatusKeys.IgnoredDuplicate
                ? SuccessAck()
                : FailAck("The notification was previously rejected.");
        }

        var apiV3Key = secretProtector.UnprotectApiV3Key(merchantConfig.ApiV3KeyProtected);
        string decryptedPayload;
        try
        {
            decryptedPayload = WeChatPayResourceDecryptor.Decrypt(
                apiV3Key,
                envelope.Resource.AssociatedData ?? string.Empty,
                envelope.Resource.Nonce ?? string.Empty,
                envelope.Resource.Ciphertext ?? string.Empty);
        }
        catch
        {
            return FailAck("Notify resource decryption failed.");
        }

        var transactionPayload = JsonSerializer.Deserialize(
            decryptedPayload,
            NotifyJsonContext.Default.WeChatTransactionNotifyPayload);
        if (transactionPayload is null
            || string.IsNullOrWhiteSpace(transactionPayload.OutTradeNo))
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    null,
                    PaymentNotifyProcessStatusKeys.Rejected,
                    "invalid transaction payload",
                    cancellationToken)
                .ConfigureAwait(false);
            return FailAck("Transaction payload is invalid.");
        }

        if (!string.Equals(transactionPayload.MchId, merchantConfig.MerchantId, StringComparison.Ordinal)
            || !string.Equals(transactionPayload.AppId, merchantConfig.AppId, StringComparison.Ordinal))
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    transactionPayload.OutTradeNo,
                    PaymentNotifyProcessStatusKeys.Rejected,
                    "merchant mismatch",
                    cancellationToken)
                .ConfigureAwait(false);
            return FailAck("Merchant or app id mismatch.");
        }

        var order = await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(
                PaymentOrderSql.FindByOutTradeNo,
                PaymentSqlParameters.Create(("OutTradeNo", transactionPayload.OutTradeNo)),
                cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    transactionPayload.OutTradeNo,
                    PaymentNotifyProcessStatusKeys.Rejected,
                    "order not found",
                    cancellationToken)
                .ConfigureAwait(false);
            return FailAck("Payment order was not found.");
        }

        // 验签身份必须与被更新的订单绑定，Host 商户配置可以服务租户，但租户专属配置不得串租户。
        if (order.MerchantConfigId != merchantConfigId
            || merchantConfig.TenantId is { } merchantTenantId && order.TenantId != merchantTenantId
            || order.ChannelKey != PaymentChannelKeys.WeChatNative
            || merchantConfig.ChannelKey != PaymentChannelKeys.WeChatNative
            || !string.Equals(order.Currency, transactionPayload.Amount?.Currency, StringComparison.Ordinal))
        {
            await InsertReceiptAsync(merchantConfigId, envelope.Id, envelope.EventType,
                transactionPayload.OutTradeNo, PaymentNotifyProcessStatusKeys.Rejected,
                "order binding mismatch", cancellationToken).ConfigureAwait(false);
            return FailAck("Payment order binding mismatch.");
        }

        var notifyAmount = transactionPayload.Amount?.Total ?? 0;
        if (notifyAmount != order.AmountMinor)
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    transactionPayload.OutTradeNo,
                    PaymentNotifyProcessStatusKeys.Rejected,
                    "amount mismatch",
                    cancellationToken)
                .ConfigureAwait(false);
            return FailAck("Payment amount mismatch.");
        }

        var mappedState = MapTradeState(transactionPayload.TradeState);
        if (mappedState is null)
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    transactionPayload.OutTradeNo,
                    PaymentNotifyProcessStatusKeys.Rejected,
                    $"unsupported trade state {transactionPayload.TradeState}",
                    cancellationToken)
                .ConfigureAwait(false);
            return FailAck("Unsupported trade state.");
        }

        // 支付成功后的退款生命周期不可被迟到支付通知倒退；其它终态也只能通过显式对账纠正。
        // created 与 provider_unknown 都还没有确认支付，允许通知推进到渠道权威状态。
        var wasPaid = order.TradeStateKey is PaymentTradeStateKeys.Succeeded
            or PaymentTradeStateKeys.Refunding or PaymentTradeStateKeys.Refunded;
        if (wasPaid && mappedState != PaymentTradeStateKeys.Succeeded
            || !wasPaid && order.TradeStateKey != mappedState
                && order.TradeStateKey is not (PaymentTradeStateKeys.Created
                    or PaymentTradeStateKeys.AwaitingPayment
                    or PaymentTradeStateKeys.ProviderUnknown))
        {
            await InsertReceiptAsync(merchantConfigId, envelope.Id, envelope.EventType,
                transactionPayload.OutTradeNo, PaymentNotifyProcessStatusKeys.Rejected,
                "terminal state conflict", cancellationToken).ConfigureAwait(false);
            return FailAck("Payment order terminal state conflict.");
        }

        if (!wasPaid && order.TradeStateKey != mappedState)
        {
            var now = clock.UtcNow;
            var paidAtUtc = mappedState == PaymentTradeStateKeys.Succeeded ? now : order.PaidAtUtc;
            var affected = await commandExecutor.ExecuteAsync(
                    PaymentOrderSql.UpdateTradeState,
                    PaymentSqlParameters.Create(
                        ("OrderId", order.Id),
                        ("TradeStateKey", mappedState),
                        ("ProviderTransactionId", transactionPayload.TransactionId),
                        ("FailMessage", null),
                        ("UpdatedAtUtc", now),
                        ("PaidAtUtc", paidAtUtc),
                        ("Version", order.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 0)
            {
                return FailAck("Payment order concurrency conflict.");
            }
        }

        await InsertReceiptAsync(
            merchantConfigId, envelope.Id, envelope.EventType, transactionPayload.OutTradeNo,
            PaymentNotifyProcessStatusKeys.Processed, BuildPayloadSummary(transactionPayload), cancellationToken)
            .ConfigureAwait(false);

        return SuccessAck();
    }

    private async Task InsertReceiptAsync(
        Guid merchantConfigId,
        string providerNotifyId,
        string eventTypeKey,
        string? outTradeNo,
        string processStatusKey,
        string payloadSummary,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                PaymentNotifyReceiptSql.Insert,
                PaymentSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("MerchantConfigId", merchantConfigId),
                    ("ProviderNotifyId", providerNotifyId),
                    ("EventTypeKey", eventTypeKey),
                    ("OutTradeNo", outTradeNo),
                    ("ProcessStatusKey", processStatusKey),
                    ("PayloadSummary", TrimSummary(payloadSummary)),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? MapTradeState(string? tradeState) =>
        tradeState switch
        {
            "SUCCESS" => PaymentTradeStateKeys.Succeeded,
            "NOTPAY" => PaymentTradeStateKeys.AwaitingPayment,
            "CLOSED" => PaymentTradeStateKeys.Closed,
            "PAYERROR" => PaymentTradeStateKeys.Failed,
            _ => null,
        };

    private static string BuildPayloadSummary(WeChatTransactionNotifyPayload payload) =>
        $"outTradeNo={payload.OutTradeNo};tradeState={payload.TradeState};transactionId={payload.TransactionId}";

    private static string TrimSummary(string summary) =>
        summary.Length <= MaxPayloadSummaryLength
            ? summary
            : summary[..MaxPayloadSummaryLength];

    private static WeChatPayNotifyAckResponse SuccessAck() =>
        new("SUCCESS", "成功");

    private static WeChatPayNotifyAckResponse FailAck(string message) =>
        new("FAIL", message);

    private sealed record WeChatNotifyEnvelope(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("event_type")] string? EventType,
        [property: JsonPropertyName("resource")] WeChatNotifyResource? Resource);

    private sealed record WeChatNotifyResource(
        [property: JsonPropertyName("algorithm")] string? Algorithm,
        [property: JsonPropertyName("ciphertext")] string? Ciphertext,
        [property: JsonPropertyName("associated_data")] string? AssociatedData,
        [property: JsonPropertyName("nonce")] string? Nonce);

    private sealed record WeChatTransactionNotifyPayload(
        [property: JsonPropertyName("mchid")] string? MchId,
        [property: JsonPropertyName("appid")] string? AppId,
        [property: JsonPropertyName("out_trade_no")] string? OutTradeNo,
        [property: JsonPropertyName("transaction_id")] string? TransactionId,
        [property: JsonPropertyName("trade_state")] string? TradeState,
        [property: JsonPropertyName("amount")] WeChatNotifyAmount? Amount);

    /// <summary>通知中的订单金额及币种，必须同时匹配本地订单。</summary>
    /// <param name="Total">最小货币单位金额。</param>
    /// <param name="Currency">支付币种。</param>
    private sealed record WeChatNotifyAmount(
        [property: JsonPropertyName("total")] long Total,
        [property: JsonPropertyName("currency")] string? Currency);

    /// <summary>通知解析的闭合元数据，避免在 Native AOT 下回退到反射序列化。</summary>
    [JsonSerializable(typeof(WeChatNotifyEnvelope))]
    [JsonSerializable(typeof(WeChatTransactionNotifyPayload))]
    private partial class NotifyJsonContext : JsonSerializerContext;
}
