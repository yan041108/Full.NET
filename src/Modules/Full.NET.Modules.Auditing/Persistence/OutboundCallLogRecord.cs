/// <summary>Auditing 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
namespace Full.NET.Modules.Auditing.Persistence;

internal sealed record OutboundCallLogRecord
{
    public string? CorrelationJson { get; init; }

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
