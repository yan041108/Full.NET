namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingQueryScenario(
    string Name,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? HttpMethod,
    int? StatusCode,
    string? PathContains,
    int Offset,
    int PageSize);

public static class AuditingQueryScenarios
{
    public const string MatchingPath = "/api/v1/settings";
    public const int MaximumContainsWindowDays = 1;

    public static IReadOnlyList<AuditingQueryScenario> Create(
        AuditingQueryBenchmarkOptions options,
        DateTimeOffset referenceUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        var deepOffset = Math.Max(0, options.Rows - options.PageSize);

        return
        [
            new(
                "first_page",
                null,
                null,
                null,
                null,
                null,
                0,
                options.PageSize),
            new(
                "deep_offset",
                null,
                null,
                null,
                null,
                null,
                deepOffset,
                options.PageSize),
            new(
                "contains_unbounded",
                null,
                null,
                null,
                null,
                MatchingPath,
                0,
                options.PageSize),
            new(
                "contains_bounded",
                referenceUtc.AddDays(-MaximumContainsWindowDays),
                referenceUtc,
                null,
                null,
                MatchingPath,
                0,
                options.PageSize),
        ];
    }
}
