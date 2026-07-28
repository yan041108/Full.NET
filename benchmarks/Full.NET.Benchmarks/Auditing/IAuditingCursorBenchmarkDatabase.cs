namespace Full.NET.Benchmarks.Auditing;

internal sealed record AuditingCursorBoundary(
    DateTimeOffset OccurredAtUtc,
    Guid Id);

internal interface IAuditingCursorBenchmarkDatabase
{
    Task<AuditingCursorBoundary> FindDeepCursorBoundaryAsync(
        int offset,
        CancellationToken cancellationToken);

    Task<AuditingQueryPageResult> ExecuteCursorComparisonAsync(
        AuditingCursorQueryStrategy strategy,
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize,
        int totalRows,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> CaptureCursorComparisonPlansAsync(
        AuditingCursorQueryStrategy strategy,
        AuditingCursorBoundary boundary,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);
}
