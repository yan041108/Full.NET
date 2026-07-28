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
        SqlDataScope.Global);

    private const string CountSqlServerPrefix =
        """
        SELECT COUNT(1)
        FROM fn_auditing_access_log
        """;

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

    private const string ListSqlServerPrefix =
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
        """;

    private const string CursorListSqlServerPrefix =
        """
        SELECT TOP (@FetchSize)
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
        FROM fn_auditing_access_log
        """;

    private const string CursorListSqlServerSuffix =
        """
        ORDER BY OccurredAtUtc DESC, Id DESC
        """;

    private const string ListSqlServerSuffix =
        """
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

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

    private static readonly SqlStatement[] PageFilteredSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateVariants(
            "auditing.page_access_logs.sql_server",
            CountSqlServerPrefix,
            ListSqlServerPrefix,
            ListSqlServerSuffix,
            [
                "OccurredAtUtc >= @FromUtc",
                "OccurredAtUtc <= @ToUtc",
                "HttpMethod = @HttpMethod",
                "StatusCode = @StatusCode",
                "CHARINDEX(@PathContains, RequestPath) > 0",
            ]);

    private static readonly string[] CursorOptionalPredicatesSqlServer =
    [
        "OccurredAtUtc >= @FromUtc",
        "OccurredAtUtc <= @ToUtc",
        "HttpMethod = @HttpMethod",
        "StatusCode = @StatusCode",
        "CHARINDEX(@PathContains, RequestPath) > 0",
    ];

    private static readonly SqlStatement[] CursorListFirstSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateListVariants(
            "auditing.cursor_access_logs.sql_server.first",
            CursorListSqlServerPrefix,
            CursorListSqlServerSuffix,
            CursorOptionalPredicatesSqlServer);

    private static readonly SqlStatement[] CursorListAfterSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateListVariants(
            "auditing.cursor_access_logs.sql_server.after",
            CursorListSqlServerPrefix,
            CursorListSqlServerSuffix,
            CursorOptionalPredicatesSqlServer,
            [
                """
                (OccurredAtUtc < @CursorOccurredAtUtc
                   OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))
                """,
            ]);

    public static readonly SqlStatement PageFilteredMySql = new(
        "auditing.page_access_logs.my_sql",
        $"{CountFilteredMySql.Text.TrimEnd()};{Environment.NewLine}{ListFilteredMySql.Text}",
        SqlDataScope.HostOnly);

    public static SqlStatement CreatePageFilteredSqlServer(
        bool hasFromUtc,
        bool hasToUtc,
        bool hasHttpMethod,
        bool hasStatusCode,
        bool hasPathContains)
    {
        var shape = (hasFromUtc ? 1 : 0)
            | (hasToUtc ? 1 << 1 : 0)
            | (hasHttpMethod ? 1 << 2 : 0)
            | (hasStatusCode ? 1 << 3 : 0)
            | (hasPathContains ? 1 << 4 : 0);
        return PageFilteredSqlServerVariants[shape];
    }

    public static SqlStatement CreateCursorListSqlServer(
        bool hasCursor,
        bool hasFromUtc,
        bool hasToUtc,
        bool hasHttpMethod,
        bool hasStatusCode,
        bool hasPathContains)
    {
        var shape = (hasFromUtc ? 1 : 0)
            | (hasToUtc ? 1 << 1 : 0)
            | (hasHttpMethod ? 1 << 2 : 0)
            | (hasStatusCode ? 1 << 3 : 0)
            | (hasPathContains ? 1 << 4 : 0);
        return hasCursor
            ? CursorListAfterSqlServerVariants[shape]
            : CursorListFirstSqlServerVariants[shape];
    }

    public static readonly SqlStatement CursorListFirstMySql = new(
        "auditing.cursor_access_logs.my_sql.first",
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
        LIMIT @FetchSize
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CursorListAfterMySql = new(
        "auditing.cursor_access_logs.my_sql.after",
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
          AND (OccurredAtUtc < @CursorOccurredAtUtc
               OR (OccurredAtUtc = @CursorOccurredAtUtc AND Id < @CursorId))
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @FetchSize
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
