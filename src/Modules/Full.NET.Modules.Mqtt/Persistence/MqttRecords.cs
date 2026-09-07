namespace Full.NET.Modules.Mqtt.Persistence;

internal sealed class MqttClientRecord
{
    public Guid Id { get; init; }

    public string ClientKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? TenantId { get; init; }

    public bool IsEnabled { get; init; }

    public int SortOrder { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

/// <summary>消息发布事实及用于幂等比较的原正文摘要。</summary>
internal sealed class MqttMessageRecord
{
    public Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? ClientId { get; init; }

    public string? ClientKey { get; init; }

    public string Topic { get; init; } = string.Empty;

    public int PayloadSizeBytes { get; init; }

    /// <summary>原 UTF-8 正文的 SHA-256 十六进制摘要；历史未保存正文的记录保持空值。</summary>
    public string? PayloadDigest { get; init; }

    public int Qos { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? IdempotencyKey { get; init; }

    public string? SummaryMessage { get; init; }

    public DateTimeOffset? PublishedAtUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }
}
