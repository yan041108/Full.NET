using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Persistence;

internal static class OutboundCallLogSql
{
    public static readonly SqlStatement Insert = new(
        "auditing.insert_outbound_call_log",
        """
        INSERT INTO fn_auditing_outbound_call
            (Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
             StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
             TenantId, UserId)
        VALUES
            (@Id, @OccurredAtUtc, @ProviderKey, @OperationKey, @DestinationHostCategory,
             @StatusCode, @Succeeded, @DurationMs, @RetryCount, @TraceId, @SafeErrorCode,
             @TenantId, @UserId)
        """,
        SqlDataScope.Global);

    private const string CountSqlServerPrefix =
        """
        SELECT COUNT(1)
        FROM fn_auditing_outbound_call
        """;

    public static readonly SqlStatement CountFilteredMySql = new(
        "auditing.count_outbound_call_logs.mysql",
        """
        SELECT COUNT(1)
        FROM fn_auditing_outbound_call
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ProviderKey IS NULL OR ProviderKey = @ProviderKey)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@OperationContains IS NULL OR INSTR(OperationKey, @OperationContains) > 0)
        """,
        SqlDataScope.HostOnly);

    private const string ListSqlServerPrefix =
        """
        SELECT Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
               StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
               TenantId, UserId
        FROM fn_auditing_outbound_call
        """;

    private const string ListSqlServerSuffix =
        """
        ORDER BY OccurredAtUtc DESC, Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly SqlStatement ListFilteredMySql = new(
        "auditing.list_outbound_call_logs.mysql",
        """
        SELECT Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
               StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
               TenantId, UserId
        FROM fn_auditing_outbound_call
        WHERE (@FromUtc IS NULL OR OccurredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR OccurredAtUtc <= @ToUtc)
          AND (@ProviderKey IS NULL OR ProviderKey = @ProviderKey)
          AND (@Succeeded IS NULL OR Succeeded = @Succeeded)
          AND (@OperationContains IS NULL OR INSTR(OperationKey, @OperationContains) > 0)
        ORDER BY OccurredAtUtc DESC, Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement[] PageFilteredSqlServerVariants =
        AuditingSqlServerPageStatementBuilder.CreateVariants(
            "auditing.page_outbound_call_logs.sql_server",
            CountSqlServerPrefix,
            ListSqlServerPrefix,
            ListSqlServerSuffix,
            [
                "OccurredAtUtc >= @FromUtc",
                "OccurredAtUtc <= @ToUtc",
                "ProviderKey = @ProviderKey",
                "Succeeded = @Succeeded",
                "CHARINDEX(@OperationContains, OperationKey) > 0",
            ]);

    public static readonly SqlStatement PageFilteredMySql = new(
        "auditing.page_outbound_call_logs.my_sql",
        $"{CountFilteredMySql.Text.TrimEnd()};{Environment.NewLine}{ListFilteredMySql.Text}",
        SqlDataScope.HostOnly);

    public static SqlStatement CreatePageFilteredSqlServer(
        bool hasFromUtc,
        bool hasToUtc,
        bool hasProviderKey,
        bool hasSucceeded,
        bool hasOperationContains)
    {
        var shape = (hasFromUtc ? 1 : 0)
            | (hasToUtc ? 1 << 1 : 0)
            | (hasProviderKey ? 1 << 2 : 0)
            | (hasSucceeded ? 1 << 3 : 0)
            | (hasOperationContains ? 1 << 4 : 0);
        return PageFilteredSqlServerVariants[shape];
    }

    public static readonly SqlStatement FindById = new(
        "auditing.outbound_call_log.find_by_id",
        """
        SELECT Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
               StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
               TenantId, UserId
        FROM fn_auditing_outbound_call
        WHERE Id = @OutboundCallLogId
        """,
        SqlDataScope.HostOnly);
}
