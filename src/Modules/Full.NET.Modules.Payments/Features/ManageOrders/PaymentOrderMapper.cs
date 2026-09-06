using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>支付订单响应映射。</summary>
internal static class PaymentOrderMapper
{
    /// <summary>映射列表项。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>列表响应。</returns>
    public static PaymentOrderListItem MapListItem(PaymentOrderRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.MerchantConfigId,
            row.ChannelKey,
            row.OutTradeNo,
            row.TradeStateKey,
            row.AmountMinor,
            row.Currency,
            row.Subject,
            row.Description,
            row.CodeUrl,
            row.ProviderTransactionId,
            row.FailMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.PaidAtUtc,
            row.Version);

    /// <summary>映射详情。</summary>
    /// <param name="row">持久化行。</param>
    /// <returns>详情响应。</returns>
    public static PaymentOrderResponse MapDetail(PaymentOrderRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.MerchantConfigId,
            row.ChannelKey,
            row.OutTradeNo,
            row.TradeStateKey,
            row.AmountMinor,
            row.Currency,
            row.Subject,
            row.Description,
            row.CodeUrl,
            row.ProviderTransactionId,
            row.FailMessage,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.PaidAtUtc,
            row.Version);
}
