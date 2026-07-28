namespace Full.NET.Benchmarks.Auditing;

public static class AuditingQuerySql
{
    public const string SqlServerCount =
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """;

    public const string MySqlCount =
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """;

    public const string SqlServerList =
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
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public const string MySqlList =
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
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """;

    public static readonly string SqlServerPage =
        $"{SqlServerCount.TrimEnd()};{Environment.NewLine}{SqlServerList}";

    public static readonly string MySqlPage =
        $"{MySqlCount.TrimEnd()};{Environment.NewLine}{MySqlList}";
}
