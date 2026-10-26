using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>Legacy 运维实现；所有 SQL 使用 Host 作用域，回队不改消息内容或 Attempts。</summary>
internal sealed class DapperOutboxDeadLetterStore(IQueryExecutor query, ICommandExecutor command,
    IOptions<DatabaseOptions> database) : IOutboxDeadLetterStore
{
    public async Task<PagedResult<OutboxDeadLetter>> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(filter.Page, 1, 1_000_000);
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var parameters = Parameters(filter, page, size);
        var total = await query.QuerySingleOrDefaultAsync<long>(OutboxDeadLetterSql.Count, parameters, cancellationToken);
        var rows = await query.QueryAsync<OutboxDeadLetter>(database.Value.Provider == DatabaseProvider.SqlServer
            ? OutboxDeadLetterSql.ListSqlServer : OutboxDeadLetterSql.ListMySql, parameters, cancellationToken);
        return new PagedResult<OutboxDeadLetter>(rows.ToArray(), page, size, total);
    }

    public Task<OutboxDeadLetter?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        query.QuerySingleOrDefaultAsync<OutboxDeadLetter>(OutboxDeadLetterSql.Find,
            new Dictionary<string, object?> { ["Id"] = id }, cancellationToken);

    public async Task<bool> TryRequeueAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        await command.ExecuteAsync(OutboxDeadLetterSql.Requeue,
            new Dictionary<string, object?> { ["Id"] = id, ["Now"] = now }, cancellationToken) == 1;

    private static Dictionary<string, object?> Parameters(DeadLetterFilter filter, int page, int size) => new()
    {
        ["MessageType"] = filter.MessageType, ["SchemaVersion"] = filter.SchemaVersion,
        ["ErrorCode"] = filter.ErrorCode, ["FromUtc"] = filter.FromUtc, ["ToUtc"] = filter.ToUtc,
        ["Offset"] = (page - 1) * size, ["PageSize"] = size,
    };
}

/// <summary>精确筛选与条件回队 SQL，参数化并保留完整消息及历史计数。</summary>
internal static class OutboxDeadLetterSql
{
    private const string Columns = "Id, MessageType, SchemaVersion, TenantId, Attempts, DeadLetterReasonCode AS ErrorCode, OccurredAtUtc, DeadLetteredAtUtc AS FailedAtUtc";
    private const string Filter = """
        WHERE ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NOT NULL
          AND (@MessageType IS NULL OR MessageType = @MessageType)
          AND (@SchemaVersion IS NULL OR SchemaVersion = @SchemaVersion)
          AND (@ErrorCode IS NULL OR DeadLetterReasonCode = @ErrorCode)
          AND (@FromUtc IS NULL OR DeadLetteredAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR DeadLetteredAtUtc <= @ToUtc)
        """;

    public static readonly SqlStatement Count = new("messaging.legacy_dead_letters.count",
        "SELECT COUNT(*) FROM fn_outbox_message " + Filter, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListSqlServer = new("messaging.legacy_dead_letters.list.sql_server",
        "SELECT " + Columns + " FROM fn_outbox_message " + Filter +
        " ORDER BY DeadLetteredAtUtc DESC, Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListMySql = new("messaging.legacy_dead_letters.list.my_sql",
        "SELECT " + Columns + " FROM fn_outbox_message " + Filter +
        " ORDER BY DeadLetteredAtUtc DESC, Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    public static readonly SqlStatement Find = new("messaging.legacy_dead_letters.find",
        "SELECT " + Columns + " FROM fn_outbox_message WHERE Id = @Id AND ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NOT NULL", SqlDataScope.HostOnly);
    public static readonly SqlStatement Requeue = new("messaging.legacy_dead_letters.requeue", """
        UPDATE fn_outbox_message
        SET DeadLetteredAtUtc = NULL, DeadLetterReasonCode = NULL, NextAttemptAtUtc = @Now,
            LockId = NULL, LockedUntilUtc = NULL, Error = NULL
        WHERE Id = @Id AND ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NOT NULL
        """, SqlDataScope.HostOnly);
}
