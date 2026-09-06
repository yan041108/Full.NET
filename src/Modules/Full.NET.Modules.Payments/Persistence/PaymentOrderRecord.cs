namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付订单持久化行。</summary>
internal sealed class PaymentOrderRecord
{
    /// <summary>订单标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识。</summary>
    public Guid TenantId { get; init; }

    /// <summary>关联商户配置标识。</summary>
    public Guid MerchantConfigId { get; init; }

    /// <summary>支付渠道键。</summary>
    public string ChannelKey { get; init; } = string.Empty;

    /// <summary>商户订单号。</summary>
    public string OutTradeNo { get; init; } = string.Empty;

    /// <summary>交易状态键。</summary>
    public string TradeStateKey { get; init; } = string.Empty;

    /// <summary>订单金额（最小货币单位）。</summary>
    public long AmountMinor { get; init; }

    /// <summary>货币代码。</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>商品标题。</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>商品描述。</summary>
    public string? Description { get; init; }

    /// <summary>微信 Native 支付二维码链接。</summary>
    public string? CodeUrl { get; init; }

    /// <summary>渠道交易标识。</summary>
    public string? ProviderTransactionId { get; init; }

    /// <summary>失败摘要。</summary>
    public string? FailMessage { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>支付成功时间。</summary>
    public DateTimeOffset? PaidAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
