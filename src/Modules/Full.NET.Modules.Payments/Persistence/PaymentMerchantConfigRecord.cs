namespace Full.NET.Modules.Payments.Persistence;

/// <summary>支付商户配置持久化行。</summary>
internal sealed class PaymentMerchantConfigRecord
{
    /// <summary>配置标识。</summary>
    public Guid Id { get; init; }

    /// <summary>租户标识；为空表示 Host 级配置。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>显示名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>支付渠道键。</summary>
    public string ChannelKey { get; init; } = string.Empty;

    /// <summary>微信应用标识。</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>微信商户号。</summary>
    public string MerchantId { get; init; } = string.Empty;

    /// <summary>商户 API 证书序列号。</summary>
    public string CertificateSerialNo { get; init; } = string.Empty;

    /// <summary>支付结果通知地址。</summary>
    public string NotifyUrl { get; init; } = string.Empty;

    /// <summary>受保护的 API v3 密钥。</summary>
    public string? ApiV3KeyProtected { get; init; }

    /// <summary>受保护的商户私钥。</summary>
    public string? PrivateKeyProtected { get; init; }

    /// <summary>是否为默认配置。</summary>
    public bool IsDefault { get; init; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>更新时间。</summary>
    public DateTimeOffset? UpdatedAtUtc { get; init; }

    /// <summary>乐观并发版本。</summary>
    public int Version { get; init; }
}
