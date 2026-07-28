namespace Full.NET.Benchmarks.Auditing;

public enum AuditingSqlServerQueryStrategy
{
    CurrentOptional = 0,
    BranchSpecific = 1,
    Recompile = 2,
}

public sealed record AuditingSqlServerQuery(
    string CountSql,
    string ListSql,
    string PageSql);

public sealed record AuditingSqlServerAbSequence(
    string Name,
    IReadOnlyList<AuditingQueryScenario> Scenarios);

public static class AuditingSqlServerAbSequences
{
    public static IReadOnlyList<AuditingSqlServerAbSequence> Create(
        IReadOnlyList<AuditingQueryScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        var firstPage = scenarios.Single(scenario => scenario.Name == "first_page");
        var containsBounded = scenarios.Single(
            scenario => scenario.Name == "contains_bounded");

        return
        [
            new("broad_first", [firstPage, containsBounded]),
            new("bounded_first", [containsBounded, firstPage]),
        ];
    }
}

public static class AuditingSqlServerQueryFactory
{
    public static readonly IReadOnlyList<AuditingSqlServerQueryStrategy> Strategies =
    [
        AuditingSqlServerQueryStrategy.CurrentOptional,
        AuditingSqlServerQueryStrategy.BranchSpecific,
        AuditingSqlServerQueryStrategy.Recompile,
    ];

    public static AuditingSqlServerQuery Create(
        AuditingSqlServerQueryStrategy strategy,
        AuditingQueryScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return strategy switch
        {
            AuditingSqlServerQueryStrategy.CurrentOptional => CreateCurrent(),
            AuditingSqlServerQueryStrategy.BranchSpecific => CreateBranchSpecific(scenario),
            AuditingSqlServerQueryStrategy.Recompile => CreateRecompile(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                "未知 SQL Server 审计查询策略。"),
        };
    }

    public static string GetName(AuditingSqlServerQueryStrategy strategy) =>
        strategy switch
        {
            AuditingSqlServerQueryStrategy.CurrentOptional => "current_optional",
            AuditingSqlServerQueryStrategy.BranchSpecific => "branch_specific",
            AuditingSqlServerQueryStrategy.Recompile => "recompile",
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                "未知 SQL Server 审计查询策略。"),
        };

    private static AuditingSqlServerQuery CreateCurrent() =>
        new(
            AuditingQuerySql.SqlServerCount,
            AuditingQuerySql.SqlServerList,
            AuditingQuerySql.SqlServerPage);

    private static AuditingSqlServerQuery CreateRecompile()
    {
        var count = AppendRecompile(AuditingQuerySql.SqlServerCount);
        var list = AppendRecompile(AuditingQuerySql.SqlServerList);
        return new(count, list, Combine(count, list));
    }

    private static AuditingSqlServerQuery CreateBranchSpecific(
        AuditingQueryScenario scenario)
    {
        var predicates = new List<string>();
        if (scenario.FromUtc is not null)
        {
            predicates.Add("OccurredAtUtc >= @FromUtc");
        }

        if (scenario.ToUtc is not null)
        {
            predicates.Add("OccurredAtUtc <= @ToUtc");
        }

        if (scenario.HttpMethod is not null)
        {
            predicates.Add("HttpMethod = @HttpMethod");
        }

        if (scenario.StatusCode is not null)
        {
            predicates.Add("StatusCode = @StatusCode");
        }

        if (scenario.PathContains is not null)
        {
            predicates.Add("CHARINDEX(@PathContains, RequestPath) > 0");
        }

        var whereClause = predicates.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}WHERE {string.Join(
                $"{Environment.NewLine}  AND ",
                predicates)}";
        var count =
            $"""
             SELECT COUNT(1)
             FROM fn_auditing_access_log{whereClause}
             """;
        var list =
            $"""
             SELECT Id,
                    OccurredAtUtc,
                    HttpMethod,
                    RequestPath,
                    StatusCode,
                    DurationMs,
                    UserId,
                    TenantId,
                    TraceId,
                    ClientIpFingerprint,
                    IsAuthenticated
             FROM fn_auditing_access_log{whereClause}
             ORDER BY OccurredAtUtc DESC, Id DESC
             OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
             """;
        return new(count, list, Combine(count, list));
    }

    private static string AppendRecompile(string statement) =>
        $"{statement.TrimEnd()}{Environment.NewLine}OPTION (RECOMPILE)";

    private static string Combine(string count, string list) =>
        $"{count.TrimEnd()};{Environment.NewLine}{list}";
}
