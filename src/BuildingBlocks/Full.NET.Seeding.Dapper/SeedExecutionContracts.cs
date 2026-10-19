namespace Full.NET.Seeding.Dapper;

/// <summary>跨进程播种互斥租约；获取失败映射为稳定种子错误码。</summary>
internal interface ISeedExecutionLeaseProvider
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}

/// <summary>播种运行与贡献项审计持久化；与业务模块事务隔离。</summary>
internal interface ISeedExecutionStore
{
    Task StartRunAsync(SeedRunAuditStart audit, CancellationToken cancellationToken);

    Task CompleteRunAsync(SeedRunAuditCompletion audit, CancellationToken cancellationToken);

    Task StartItemAsync(SeedRunItemAuditStart audit, CancellationToken cancellationToken);

    Task CompleteItemAsync(
        SeedRunItemAuditCompletion audit,
        CancellationToken cancellationToken);
}

internal sealed record SeedRunAuditStart(
    Guid RunId,
    string Profile,
    string EnvironmentName,
    string ApplicationVersion,
    string CorrelationId,
    DateTimeOffset StartedAtUtc);

internal sealed record SeedRunAuditCompletion(
    Guid RunId,
    string Status,
    string? ErrorCode,
    DateTimeOffset CompletedAtUtc);

internal sealed record SeedRunItemAuditStart(
    Guid RunId,
    string Contributor,
    int ContributorVersion,
    DateTimeOffset StartedAtUtc);

internal sealed record SeedRunItemAuditCompletion(
    Guid RunId,
    string Contributor,
    string Status,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    string? ErrorCode,
    DateTimeOffset CompletedAtUtc);

internal static class SeedExecutionStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

internal sealed class SeedExecutionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
