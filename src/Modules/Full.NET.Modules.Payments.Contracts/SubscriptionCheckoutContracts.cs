namespace Full.NET.Modules.Payments.Contracts;

/// <summary>订阅下单结果；资金状态与权益履约状态分开呈现。</summary>
/// <param name="SubscriptionId">原始订阅。</param>
/// <param name="Order">实际资金订单；未知结果仍保持未知状态，不能当作付款成功。</param>
/// <param name="FulfillmentStateKey">持久化履约阶段。</param>
/// <param name="ErrorCode">最近稳定失败码。</param>
public sealed record SubscriptionCheckoutResponse(Guid SubscriptionId, PaymentOrderResponse Order,
    string FulfillmentStateKey, string? ErrorCode);

/// <summary>资金订单对应的订阅恢复状态；不回显密钥或回调正文。</summary>
/// <param name="SubscriptionId">原订阅。</param>
/// <param name="BeneficiaryTenantId">权益受益租户。</param>
/// <param name="PaymentOrderId">实际资金订单。</param>
/// <param name="StateKey">恢复阶段。</param>
/// <param name="ErrorCode">稳定失败码。</param>
/// <param name="Attempt">恢复次数。</param>
/// <param name="UpdatedAtUtc">最近变更时间。</param>
public sealed record SubscriptionFulfillmentStatusResponse(Guid SubscriptionId, Guid BeneficiaryTenantId,
    Guid? PaymentOrderId, string StateKey, string? ErrorCode, int Attempt, DateTimeOffset UpdatedAtUtc);
