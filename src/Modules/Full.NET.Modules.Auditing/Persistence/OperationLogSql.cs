using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

internal static class OperationLogSql
{
    public static readonly SqlStatement Insert = new(
        "auditing.insert_operation_log",
        """
        INSERT INTO fn_auditing_operation_log
            (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
             Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode)
        VALUES
            (@Id, @OccurredAtUtc, @ActionKey, @HttpMethod, @RequestPath, @StatusCode, @DurationMs,
             @Succeeded, @UserId, @TenantId, @TraceId, @ClientIpFingerprint, @PermissionCode)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountFilteredSqlServer = new(
        "auditing.count_operation_logs.sql_server",
        """
        SELECT COUNT(1)
        FROM fn_auditing_operation_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountFilteredMySql = new(
        "auditing.count_operation_logs.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_operation_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredSqlServer = new(
        "auditing.list_operation_logs.sql_server",
        """
        SELECT Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR CHARINDEX(@PathContains, RequestPath) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListFilteredMySql = new(
        "auditing.list_operation_logs.mysql",
        """
        SELECT Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@HttpMethod IS NULL OR HttpMethod = @HttpMethod)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@PathContains IS NULL OR INSTR(RequestPath, @PathContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindById = new(
        "auditing.operation_log.find_by_id",
        """
        SELECT Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        WHERE Id = @OperationLogId
        """,
        SqlDataScope.HostOnly);
}
