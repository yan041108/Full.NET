namespace Full.NET.Modules.Payments.Contracts;

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>支付退款列表项。</summary>
/// <param name="Id">退款稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="OrderId">关联支付订单标识。</param>
/// <param name="OutTradeNo">商户订单号。</param>
/// <param name="OutRefundNo">商户退款单号。</param>
/// <param name="RefundStateKey">退款状态键。</param>
/// <param name="AmountMinor">退款金额（最小货币单位）。</param>
/// <param name="Currency">货币代码。</param>
/// <param name="Reason">退款原因。</param>
/// <param name="ProviderRefundId">渠道退款标识。</param>
/// <param name="FailMessage">失败摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="CompletedAtUtc">完成时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentRefundListItem(
    Guid Id,
    Guid TenantId,
    Guid OrderId,
    string OutTradeNo,
    string OutRefundNo,
    string RefundStateKey,
    long AmountMinor,
    string Currency,
    string Reason,
    string? ProviderRefundId,
    string? FailMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int Version);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>支付退款详情。</summary>
/// <param name="Id">退款稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="OrderId">关联支付订单标识。</param>
/// <param name="MerchantConfigId">关联商户配置标识。</param>
/// <param name="OutTradeNo">商户订单号。</param>
/// <param name="OutRefundNo">商户退款单号。</param>
/// <param name="RefundStateKey">退款状态键。</param>
/// <param name="AmountMinor">退款金额（最小货币单位）。</param>
/// <param name="Currency">货币代码。</param>
/// <param name="Reason">退款原因。</param>
/// <param name="ProviderRefundId">渠道退款标识。</param>
/// <param name="FailMessage">失败摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="CompletedAtUtc">完成时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentRefundResponse(
    Guid Id,
    Guid TenantId,
    Guid OrderId,
    Guid MerchantConfigId,
    string OutTradeNo,
    string OutRefundNo,
    string RefundStateKey,
    long AmountMinor,
    string Currency,
    string Reason,
    string? ProviderRefundId,
    string? FailMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int Version);

/// <summary>创建支付退款请求。</summary>
/// <param name="AmountMinor">退款金额（最小货币单位）；为空表示全额退款。</param>
/// <param name="Reason">退款原因。</param>
public sealed record CreatePaymentRefundRequest(
    long? AmountMinor,
    string Reason);

/// <summary>微信支付通知受理响应（微信协议格式）。</summary>
/// <param name="Code">结果码。</param>
/// <param name="Message">结果描述。</param>
public sealed record WeChatPayNotifyAckResponse(string Code, string Message);
