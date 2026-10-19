using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Payments.Contracts;

namespace Full.NET.Modules.Payments.Serialization;

/// <summary>Payments 模块 JSON 源生成上下文。</summary>
[JsonSerializable(typeof(PaymentMerchantConfigListItem))]
[JsonSerializable(typeof(PaymentMerchantConfigResponse))]
[JsonSerializable(typeof(CreatePaymentMerchantConfigRequest))]
[JsonSerializable(typeof(UpdatePaymentMerchantConfigRequest))]
[JsonSerializable(typeof(PagedResult<PaymentMerchantConfigListItem>))]
[JsonSerializable(typeof(PaymentOrderListItem))]
[JsonSerializable(typeof(PaymentOrderResponse))]
[JsonSerializable(typeof(CreatePaymentOrderRequest))]
[JsonSerializable(typeof(PagedResult<PaymentOrderListItem>))]
[JsonSerializable(typeof(PaymentRefundListItem))]
[JsonSerializable(typeof(PaymentRefundResponse))]
[JsonSerializable(typeof(CreatePaymentRefundRequest))]
[JsonSerializable(typeof(PagedResult<PaymentRefundListItem>))]
[JsonSerializable(typeof(WeChatPayNotifyAckResponse))]
/// <summary>Payments 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class PaymentsJsonSerializerContext : JsonSerializerContext;
