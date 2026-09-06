namespace Full.NET.Modules.Payments.Contracts;

/// <summary>支付订单列表项。</summary>
/// <param name="Id">订单稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="MerchantConfigId">关联商户配置标识。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="OutTradeNo">商户订单号。</param>
/// <param name="TradeStateKey">交易状态键。</param>
/// <param name="AmountMinor">订单金额（最小货币单位）。</param>
/// <param name="Currency">货币代码。</param>
/// <param name="Subject">商品标题。</param>
/// <param name="Description">商品描述。</param>
/// <param name="CodeUrl">微信 Native 支付二维码链接。</param>
/// <param name="ProviderTransactionId">渠道交易标识。</param>
/// <param name="FailMessage">失败摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="PaidAtUtc">支付成功时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentOrderListItem(
    Guid Id,
    Guid TenantId,
    Guid MerchantConfigId,
    string ChannelKey,
    string OutTradeNo,
    string TradeStateKey,
    long AmountMinor,
    string Currency,
    string Subject,
    string? Description,
    string? CodeUrl,
    string? ProviderTransactionId,
    string? FailMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PaidAtUtc,
    int Version);

/// <summary>支付订单详情。</summary>
/// <param name="Id">订单稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="MerchantConfigId">关联商户配置标识。</param>
/// <param name="ChannelKey">支付渠道键。</param>
/// <param name="OutTradeNo">商户订单号。</param>
/// <param name="TradeStateKey">交易状态键。</param>
/// <param name="AmountMinor">订单金额（最小货币单位）。</param>
/// <param name="Currency">货币代码。</param>
/// <param name="Subject">商品标题。</param>
/// <param name="Description">商品描述。</param>
/// <param name="CodeUrl">微信 Native 支付二维码链接。</param>
/// <param name="ProviderTransactionId">渠道交易标识。</param>
/// <param name="FailMessage">失败摘要。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="PaidAtUtc">支付成功时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record PaymentOrderResponse(
    Guid Id,
    Guid TenantId,
    Guid MerchantConfigId,
    string ChannelKey,
    string OutTradeNo,
    string TradeStateKey,
    long AmountMinor,
    string Currency,
    string Subject,
    string? Description,
    string? CodeUrl,
    string? ProviderTransactionId,
    string? FailMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PaidAtUtc,
    int Version);

/// <summary>创建支付订单请求。</summary>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="MerchantConfigId">商户配置标识；为空时使用租户默认配置。</param>
/// <param name="AmountMinor">订单金额（最小货币单位）。</param>
/// <param name="Currency">货币代码。</param>
/// <param name="Subject">商品标题。</param>
/// <param name="Description">商品描述。</param>
public sealed record CreatePaymentOrderRequest(
    Guid TenantId,
    Guid? MerchantConfigId,
    long AmountMinor,
    string Currency,
    string Subject,
    string? Description);
