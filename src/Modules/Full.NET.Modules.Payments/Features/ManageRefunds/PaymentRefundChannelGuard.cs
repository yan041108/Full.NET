using Full.NET.Abstractions.Results;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;

namespace Full.NET.Modules.Payments.Features.ManageRefunds;

/// <summary>创建和查询退款共用渠道准入；不能把其他渠道或租户的商户凭据传给微信。</summary>
internal static class PaymentRefundChannelGuard
{
    internal static Error? Validate(PaymentOrderRecord order, PaymentMerchantConfigRecord merchant)
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
}
