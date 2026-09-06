using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付通知幂等记录 SQL。</summary>
internal static class PaymentNotifyReceiptSql
{
    public static readonly SqlStatement Insert = new(
        "payments.insert_notify_receipt",
        """
        INSERT INTO fn_payment_notify_receipt
            (Id, MerchantConfigId, ProviderNotifyId, EventTypeKey, OutTradeNo,
             ProcessStatusKey, PayloadSummary, CreatedAtUtc)
        VALUES
            (@Id, @MerchantConfigId, @ProviderNotifyId, @EventTypeKey, @OutTradeNo,
             @ProcessStatusKey, @PayloadSummary, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByProviderNotifyId = new(
        "payments.find_notify_receipt_by_provider_notify_id",
        """
        SELECT receipts.Id,
               receipts.MerchantConfigId,
               receipts.ProviderNotifyId,
               receipts.EventTypeKey,
               receipts.OutTradeNo,
               receipts.ProcessStatusKey,
               receipts.PayloadSummary,
               receipts.CreatedAtUtc
        FROM fn_payment_notify_receipt AS receipts
        WHERE receipts.MerchantConfigId = @MerchantConfigId
          AND receipts.ProviderNotifyId = @ProviderNotifyId
        """,
        SqlDataScope.HostOnly);
}
