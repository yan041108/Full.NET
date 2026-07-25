using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

internal static class AccessLogSql
{
    public static readonly SqlStatement Insert = new(
        "auditing.insert_access_log",
        """
        INSERT INTO fn_auditing_access_log
            (Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
             UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated)
        VALUES
            (@Id, @OccurredAtUtc, @HttpMethod, @RequestPath, @StatusCode, @DurationMs,
             @UserId, @TenantId, @TraceId, @ClientIpFingerprint, @IsAuthenticated)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountFilteredSqlServer = new(
        "auditing.count_access_logs.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountFilteredMySql = new(
        "auditing.count_access_logs.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredSqlServer = new(
        "auditing.list_access_logs.sql_server",
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
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredMySql = new(
        "auditing.list_access_logs.mysql",
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
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "auditing.access_log.find_by_id",
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
        WHERE Id = @AccessLogId
        """,
        SqlDataScope.HostOnly);
}
