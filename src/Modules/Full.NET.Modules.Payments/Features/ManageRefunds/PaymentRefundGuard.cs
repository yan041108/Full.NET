using Full.NET.Abstractions.Results;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>创建和查询退款共用渠道准入与原退款归属校验。</summary>
internal static class PaymentRefundGuard
{
    internal static Error? ValidateChannel(PaymentOrderRecord order, PaymentMerchantConfigRecord merchant)
    {
        if (!string.Equals(order.ChannelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal))
            return new Error(PaymentErrorCodes.RefundChannelUnsupported,
                "Refunds are currently supported only for WeChat Native orders.", ErrorType.BusinessRule);

        if (merchant.Id != order.MerchantConfigId || merchant.TenantId != order.TenantId
            || !string.Equals(merchant.ChannelKey, order.ChannelKey, StringComparison.Ordinal))
            return new Error(PaymentErrorCodes.RefundMerchantMismatch,
                "The merchant configuration no longer matches the original order tenant and channel.", ErrorType.Conflict);

        return null;
    }

    /// <summary>确认退款来自同一笔资金意图，且原金额与商户退款单号可用于恢复。</summary>
    internal static bool MatchesOriginalOrder(PaymentOrderRecord order, PaymentRefundRecord refund) =>
        refund.OrderId == order.Id
        && refund.TenantId == order.TenantId
        && refund.MerchantConfigId == order.MerchantConfigId
        && string.Equals(refund.OutTradeNo, order.OutTradeNo, StringComparison.Ordinal)
        && string.Equals(refund.Currency, order.Currency, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(refund.OutRefundNo)
        && refund.AmountMinor > 0
        && refund.AmountMinor <= order.AmountMinor;
}
