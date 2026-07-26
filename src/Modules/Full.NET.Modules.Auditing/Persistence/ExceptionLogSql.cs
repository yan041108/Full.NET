using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

internal static class ExceptionLogSql
{
    public static readonly SqlStatement Insert = new(
        "auditing.insert_exception_log",
        """
        INSERT INTO fn_auditing_exception_log
            (Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
             HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint)
        VALUES
            (@Id, @OccurredAtUtc, @ExceptionType, @Message, @StackTrace,
             @HttpMethod, @RequestPath, @UserId, @TenantId, @TraceId, @ClientIpFingerprint)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountFilteredSqlServer = new(
        "auditing.count_exception_logs.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_auditing_exception_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ExceptionTypeContains IS NULL OR CHARINDEX(@ExceptionTypeContains, ExceptionType) > 0)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountFilteredMySql = new(
        "auditing.count_exception_logs.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_exception_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ExceptionTypeContains IS NULL OR INSTR(ExceptionType, @ExceptionTypeContains) > 0)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredSqlServer = new(
        "auditing.list_exception_logs.sql_server",
        """
        SELECT Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ExceptionTypeContains IS NULL OR CHARINDEX(@ExceptionTypeContains, ExceptionType) > 0)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredMySql = new(
        "auditing.list_exception_logs.mysql",
        """
        SELECT Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ExceptionTypeContains IS NULL OR INSTR(ExceptionType, @ExceptionTypeContains) > 0)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "auditing.exception_log.find_by_id",
        """
        SELECT Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        WHERE Id = @ExceptionLogId
        """,
        SqlDataScope.HostOnly);
}
