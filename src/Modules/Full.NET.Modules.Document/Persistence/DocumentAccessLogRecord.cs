namespace Full.NET.Modules.Document.Persistence;

/// <summary>文档访问日志行投影。</summary>
internal sealed class DocumentAccessLogRecord
{
    public Guid Id { get; init; }

    public Guid DocumentItemId { get; init; }

    public string DocumentTitle { get; init; } = string.Empty;

    public string AccessTypeKey { get; init; } = string.Empty;

    public string SourceKey { get; init; } = string.Empty;

    public Guid? ActorUserId { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public string? ClientIpFingerprint { get; init; }
}
