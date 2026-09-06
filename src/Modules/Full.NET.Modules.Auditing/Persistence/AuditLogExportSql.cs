using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>审计日志受控导出 SQL；固定要求时间范围并限制最大行数。</summary>
internal static class AuditLogExportSql
{
    public static readonly SqlStatement CountAccessSqlServer = new(
        "auditing.export.count_access.sqlserver",
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAccessSqlServer = new(
        "auditing.export.list_access.sqlserver",
        """
        SELECT TOP (@MaxRows)
               Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
               UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAccessMySql = new(
        "auditing.export.count_access.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAccessMySql = new(
        "auditing.export.list_access.mysql",
        """
        SELECT Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
               UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated
        FROM fn_auditing_access_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@StatusCode IS NULL OR StatusCode = @StatusCode)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @MaxRows
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountOperationSqlServer = new(
        "auditing.export.count_operation.sqlserver",
        """
        SELECT COUNT(1)
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOperationSqlServer = new(
        "auditing.export.list_operation.sqlserver",
        """
        SELECT TOP (@MaxRows)
               Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountOperationMySql = new(
        "auditing.export.count_operation.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListOperationMySql = new(
        "auditing.export.list_operation.mysql",
        """
        SELECT Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @MaxRows
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountExceptionSqlServer = new(
        "auditing.export.count_exception.sqlserver",
        """
        SELECT COUNT(1)
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@ExceptionTypeContains IS NULL OR CHARINDEX(@ExceptionTypeContains, ExceptionType) > 0)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListExceptionSqlServer = new(
        "auditing.export.list_exception.sqlserver",
        """
        SELECT TOP (@MaxRows)
               Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@ExceptionTypeContains IS NULL OR CHARINDEX(@ExceptionTypeContains, ExceptionType) > 0)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountExceptionMySql = new(
        "auditing.export.count_exception.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@ExceptionTypeContains IS NULL OR INSTR(ExceptionType, @ExceptionTypeContains) > 0)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListExceptionMySql = new(
        "auditing.export.list_exception.mysql",
        """
        SELECT Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        WHERE OccurredAtUtc >= @FromUtc
          AND OccurredAtUtc <= @ToUtc
          AND (@ExceptionTypeContains IS NULL OR INSTR(ExceptionType, @ExceptionTypeContains) > 0)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @MaxRows
        """,
        SqlDataScope.HostOnly);
}
