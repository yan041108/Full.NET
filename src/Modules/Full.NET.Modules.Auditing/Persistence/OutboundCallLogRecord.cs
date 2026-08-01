namespace Full.NET.Modules.Auditing.Persistence;

internal sealed record OutboundCallLogRecord
{
    public Guid Id { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public string ProviderKey { get; init; } = string.Empty;

    public string OperationKey { get; init; } = string.Empty;

    public string DestinationHostCategory { get; init; } = string.Empty;

    public int StatusCode { get; init; }

    public bool Succeeded { get; init; }

    public int DurationMs { get; init; }

    public int RetryCount { get; init; }

    public string? TraceId { get; init; }

    public string? SafeErrorCode { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? UserId { get; init; }
}
