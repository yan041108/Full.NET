namespace Full.NET.Benchmarks.Auditing;

public enum AuditingCursorQueryStrategy
{
    OffsetEndpoint = 0,
    CursorEndpoint = 1,
}

public sealed record AuditingCursorQuery(
    string? CountSql,
    string ListSql);

public static class AuditingCursorQueryFactory
{
    public static AuditingCursorQuery Create(
        string provider,
        AuditingCursorQueryStrategy strategy) =>
        (provider, strategy) switch
        {
            ("sqlserver", AuditingCursorQueryStrategy.OffsetEndpoint) =>
                new AuditingCursorQuery(SqlServerCount, SqlServerOffsetList),
            ("sqlserver", AuditingCursorQueryStrategy.CursorEndpoint) =>
                new AuditingCursorQuery(null, SqlServerCursorList),
            ("mysql", AuditingCursorQueryStrategy.OffsetEndpoint) =>
                new AuditingCursorQuery(MySqlCount, MySqlOffsetList),
            ("mysql", AuditingCursorQueryStrategy.CursorEndpoint) =>
                new AuditingCursorQuery(null, MySqlCursorList),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "游标 A/B 仅支持 sqlserver 与 mysql。"),
        };

    private const string Projection =
        """
        Id,
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
        """;

    private const string SqlServerCount =
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        """;

    private static readonly string SqlServerOffsetList =
        $"""
        SELECT {Projection}
        FROM fn_auditing_access_log
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    private static readonly string SqlServerCursorList =
        $"""
        SELECT TOP (@FetchSize)
               {Projection}
        FROM fn_auditing_access_log
        WHERE (OccurredAtUtc < @CursorOccurredAtUtc
           OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))
        ORDER BY OccurredAtUtc DESC, Id DESC
        """;

    private static readonly string MySqlCursorList =
        $"""
        SELECT {Projection}
        FROM fn_auditing_access_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
          AND (OccurredAtUtc < @CursorOccurredAtUtc
               OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @FetchSize
        """;

    private static string MySqlCount => AuditingQuerySql.MySqlCount;

    private static string MySqlOffsetList => AuditingQuerySql.MySqlList;
}
