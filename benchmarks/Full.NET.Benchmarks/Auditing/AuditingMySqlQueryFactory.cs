namespace Full.NET.Benchmarks.Auditing;

public enum AuditingMySqlQueryStrategy
{
    CurrentOptimizer = 0,
    ForceOccurredAtIndex = 1,
    LateMaterialization = 2,
}

public sealed record AuditingMySqlQuery(
    string CountSql,
    string ListSql,
    string PageSql);

public static class AuditingMySqlQueryFactory
{
    public static readonly IReadOnlyList<AuditingMySqlQueryStrategy> Strategies =
    [
        AuditingMySqlQueryStrategy.CurrentOptimizer,
        AuditingMySqlQueryStrategy.ForceOccurredAtIndex,
    ];

    private static readonly IReadOnlyList<AuditingMySqlQueryStrategy>
        LateMaterializationStrategies =
        [
            AuditingMySqlQueryStrategy.CurrentOptimizer,
            AuditingMySqlQueryStrategy.LateMaterialization,
        ];

    private const string ForcedListSql =
        """
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
        FROM fn_auditing_access_log
        FORCE INDEX (IX_fn_auditing_access_log_OccurredAtUtc_Id)
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """;

    private const string LateMaterializationListSql =
        """
        SELECT access_log.Id,
               access_log.OccurredAtUtc,
               access_log.HttpMethod,
               access_log.RequestPath,
               access_log.StatusCode,
               access_log.DurationMs,
               access_log.UserId,
               access_log.TenantId,
               access_log.TraceId,
               access_log.ClientIpFingerprint,
               access_log.IsAuthenticated
        FROM fn_auditing_access_log AS access_log
        INNER JOIN
        (
            SELECT Id, OccurredAtUtc
            FROM fn_auditing_access_log
            WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
              AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
              AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
              AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
              AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
            ORDER BY OccurredAtUtc DESC, Id DESC
            LIMIT @PageSize OFFSET @Offset
        ) AS page_keys
          ON page_keys.Id = access_log.Id
        ORDER BY page_keys.OccurredAtUtc DESC, page_keys.Id DESC
        """;

    public static IReadOnlyList<AuditingMySqlQueryStrategy> GetStrategies(
        AuditingQueryBenchmarkMode mode) =>
        mode switch
        {
            AuditingQueryBenchmarkMode.MySqlIndexAb => Strategies,
            AuditingQueryBenchmarkMode.MySqlLateMaterializationAb =>
                LateMaterializationStrategies,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "不支持的 MySQL A/B 基准模式。"),
        };

    public static AuditingMySqlQuery Create(AuditingMySqlQueryStrategy strategy)
    {
        var listSql = strategy switch
        {
            AuditingMySqlQueryStrategy.CurrentOptimizer => AuditingQuerySql.MySqlList,
            AuditingMySqlQueryStrategy.ForceOccurredAtIndex => ForcedListSql,
            AuditingMySqlQueryStrategy.LateMaterialization =>
                LateMaterializationListSql,
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                "不支持的 MySQL 审计查询策略。"),
        };
        return new AuditingMySqlQuery(
            AuditingQuerySql.MySqlCount,
            listSql,
            $"{AuditingQuerySql.MySqlCount.TrimEnd()};{Environment.NewLine}{listSql}");
    }

    public static string GetName(AuditingMySqlQueryStrategy strategy) =>
        strategy switch
        {
            AuditingMySqlQueryStrategy.CurrentOptimizer => "current_optimizer",
            AuditingMySqlQueryStrategy.ForceOccurredAtIndex => "force_occurred_at_index",
            AuditingMySqlQueryStrategy.LateMaterialization => "late_materialization",
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                "不支持的 MySQL 审计查询策略。"),
        };
}
