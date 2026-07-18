namespace Full.NET.Seeding.Dapper;

internal interface ISeedExecutionLeaseProvider
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}

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
