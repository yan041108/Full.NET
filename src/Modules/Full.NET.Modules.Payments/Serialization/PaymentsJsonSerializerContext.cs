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
internal partial class PaymentsJsonSerializerContext : JsonSerializerContext;
