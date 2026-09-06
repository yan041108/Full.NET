namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付通知幂等记录行。</summary>
internal sealed class PaymentNotifyReceiptRecord
{
    /// <summary>记录标识。</summary>
    public Guid Id { get; init; }

    /// <summary>商户配置标识。</summary>
    public Guid MerchantConfigId { get; init; }

    /// <summary>渠道通知标识。</summary>
    public string ProviderNotifyId { get; init; } = string.Empty;

    /// <summary>事件类型键。</summary>
    public string EventTypeKey { get; init; } = string.Empty;

    /// <summary>商户订单号。</summary>
    public string? OutTradeNo { get; init; }

    /// <summary>处理状态键。</summary>
    public string ProcessStatusKey { get; init; } = string.Empty;

    /// <summary>脱敏摘要。</summary>
    public string PayloadSummary { get; init; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}
