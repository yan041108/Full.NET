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
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Modules.Payments.Features.ReceiveWeChatNotify;

/// <summary>处理微信支付结果通知：验签、解密、幂等与订单状态更新。</summary>
internal sealed class PaymentWeChatNotifyService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentSecretProtector secretProtector,
    IWeChatPayPlatformCertificateResolver certificateResolver,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const int MaxPayloadSummaryLength = 480;

    private static readonly JsonSerializerOptions NotifyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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
    public Task<WeChatPayNotifyAckResponse> HandleAsync(
        Guid merchantConfigId,
        string requestPath,
        string timestamp,
        string nonce,
        string signature,
        string platformSerial,
        string rawBody,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => HandleCoreAsync(
                merchantConfigId,
                requestPath,
                timestamp,
                nonce,
                signature,
                platformSerial,
                rawBody,
                token),
            cancellationToken);

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

        var envelope = JsonSerializer.Deserialize<WeChatNotifyEnvelope>(rawBody, NotifyJsonOptions);
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
            return SuccessAck();
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

        var transactionPayload = JsonSerializer.Deserialize<WeChatTransactionNotifyPayload>(
            decryptedPayload,
            NotifyJsonOptions);
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

        // 已成功订单重复通知时只记幂等收据，不再改写状态。
        if (order.TradeStateKey != PaymentTradeStateKeys.Succeeded)
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

        try
        {
            await InsertReceiptAsync(
                    merchantConfigId,
                    envelope.Id,
                    envelope.EventType,
                    transactionPayload.OutTradeNo,
                    PaymentNotifyProcessStatusKeys.Processed,
                    BuildPayloadSummary(transactionPayload),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return SuccessAck();
        }

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

    private static bool IsUniqueViolation(Exception exception) =>
        exception is SqlException { Number: 2601 or 2627 }
        || exception is MySqlException { Number: 1062 };

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

    private sealed record WeChatNotifyAmount(
        [property: JsonPropertyName("total")] long Total);
}
