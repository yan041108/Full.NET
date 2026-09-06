namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付退款持久化行。</summary>
internal sealed class PaymentRefundRecord
{
    /// <summary>退款标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识。</summary>
    public Guid TenantId { get; init; }

    /// <summary>关联订单标识。</summary>
    public Guid OrderId { get; init; }

    /// <summary>关联商户配置标识。</summary>
    public Guid MerchantConfigId { get; init; }

    /// <summary>商户订单号。</summary>
    public string OutTradeNo { get; init; } = string.Empty;

    /// <summary>商户退款单号。</summary>
    public string OutRefundNo { get; init; } = string.Empty;

    /// <summary>退款状态键。</summary>
    public string RefundStateKey { get; init; } = string.Empty;

    /// <summary>退款金额（最小货币单位）。</summary>
    public long AmountMinor { get; init; }

    /// <summary>货币代码。</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>退款原因。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>渠道退款标识。</summary>
    public string? ProviderRefundId { get; init; }

    /// <summary>失败摘要。</summary>
    public string? FailMessage { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>完成时间。</summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
