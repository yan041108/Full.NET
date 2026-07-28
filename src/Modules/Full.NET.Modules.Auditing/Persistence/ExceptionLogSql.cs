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

    private const string CountSqlServerPrefix =
        """
        SELECT COUNT(1)
        FROM fn_auditing_exception_log
        """;

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

    private const string ListSqlServerPrefix =
        """
        SELECT Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
               HttpMethod, RequestPath, UserId, TenantId, TraceId, ClientIpFingerprint
        FROM fn_auditing_exception_log
        """;

    private const string ListSqlServerSuffix =
        """
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

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

    private static readonly SqlStatement[] PageFilteredSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateVariants(
            "auditing.page_exception_logs.sql_server",
            CountSqlServerPrefix,
            ListSqlServerPrefix,
            ListSqlServerSuffix,
            [
                "OccurredAtUtc >= @FromUtc",
                "OccurredAtUtc <= @ToUtc",
                "CHARINDEX(@ExceptionTypeContains, ExceptionType) > 0",
                "CHARINDEX(@PathContains, RequestPath) > 0",
            ]);

    public static readonly SqlStatement PageFilteredMySql = new(
        "auditing.page_exception_logs.my_sql",
        $"{CountFilteredMySql.Text.TrimEnd()};{Environment.NewLine}{ListFilteredMySql.Text}",
        SqlDataScope.HostOnly);

    public static SqlStatement CreatePageFilteredSqlServer(
        bool hasFromUtc,
        bool hasToUtc,
        bool hasExceptionTypeContains,
        bool hasPathContains)
    {
        var shape = (hasFromUtc ? 1 : 0)
            | (hasToUtc ? 1 << 1 : 0)
            | (hasExceptionTypeContains ? 1 << 2 : 0)
            | (hasPathContains ? 1 << 3 : 0);
        return PageFilteredSqlServerVariants[shape];
    }

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
