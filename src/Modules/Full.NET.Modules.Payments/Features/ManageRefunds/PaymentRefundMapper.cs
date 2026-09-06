using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>支付退款响应映射。</summary>
internal static class PaymentRefundMapper
{
    /// <summary>映射列表项。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static PaymentRefundListItem MapListItem(PaymentRefundRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.OrderId,
            row.OutTradeNo,
            row.OutRefundNo,
            row.RefundStateKey,
            row.AmountMinor,
            row.Currency,
            row.Reason,
            row.ProviderRefundId,
            row.FailMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.CompletedAtUtc,
            row.Version);

    /// <summary>映射详情。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static PaymentRefundResponse MapDetail(PaymentRefundRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.OrderId,
            row.MerchantConfigId,
            row.OutTradeNo,
            row.OutRefundNo,
            row.RefundStateKey,
            row.AmountMinor,
            row.Currency,
            row.Reason,
            row.ProviderRefundId,
            row.FailMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.CompletedAtUtc,
            row.Version);
}
