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

    private const string CountSqlServerPrefix =
        """
        SELECT COUNT(1)
        FROM fn_auditing_operation_log
        """;

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

    private const string ListSqlServerPrefix =
        """
        SELECT Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode, DurationMs,
               Succeeded, UserId, TenantId, TraceId, ClientIpFingerprint, PermissionCode
        FROM fn_auditing_operation_log
        """;

    private const string ListSqlServerSuffix =
        """
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

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

    private static readonly SqlStatement[] PageFilteredSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateVariants(
            "auditing.page_operation_logs.sql_server",
            CountSqlServerPrefix,
            ListSqlServerPrefix,
            ListSqlServerSuffix,
            [
                "OccurredAtUtc >= @FromUtc",
                "OccurredAtUtc <= @ToUtc",
                "HttpMethod = @HttpMethod",
                "Succeeded = @Succeeded",
                "CHARINDEX(@PathContains, RequestPath) > 0",
            ]);

    public static readonly SqlStatement PageFilteredMySql = new(
        "auditing.page_operation_logs.my_sql",
        $"{CountFilteredMySql.Text.TrimEnd()};{Environment.NewLine}{ListFilteredMySql.Text}",
        SqlDataScope.HostOnly);

    public static SqlStatement CreatePageFilteredSqlServer(
        bool hasFromUtc,
        bool hasToUtc,
        bool hasHttpMethod,
        bool hasSucceeded,
        bool hasPathContains)
    {
        var shape = (hasFromUtc ? 1 : 0)
            | (hasToUtc ? 1 << 1 : 0)
            | (hasHttpMethod ? 1 << 2 : 0)
            | (hasSucceeded ? 1 << 3 : 0)
            | (hasPathContains ? 1 << 4 : 0);
        return PageFilteredSqlServerVariants[shape];
    }

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
