using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>Kafka DLQ 查询投影；按 Broker 位置幂等持久化后才允许投影 Worker 确认 Offset。</summary>
internal sealed class KafkaDeadLetterStore(IQueryExecutor query, ICommandExecutor command,
    ICommandTransaction transaction, IOptions<DatabaseOptions> database) : IIntegrationEventDeadLetterSink
{
    public Task CaptureAsync(IntegrationEventDeadLetterCapture capture, CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(async token =>
        {
            await command.ExecuteAsync(database.Value.Provider == DatabaseProvider.SqlServer
                ? KafkaDeadLetterSql.InsertSqlServer : KafkaDeadLetterSql.InsertMySql, CaptureParameters(capture), token);
            return true;
        }, cancellationToken);

    public Task<KafkaDeadLetterRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        query.QuerySingleOrDefaultAsync<KafkaDeadLetterRow>(KafkaDeadLetterSql.Find,
            new Dictionary<string, object?> { ["Id"] = id }, cancellationToken);

    public async Task<PagedResult<KafkaDeadLetterRow>> ListAsync(DeadLetterFilter filter, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(filter.Page, 1, 1_000_000);
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var parameters = new Dictionary<string, object?>
        {
            ["ConsumerName"] = filter.ConsumerName, ["MessageType"] = filter.MessageType,
            ["SchemaVersion"] = filter.SchemaVersion, ["ErrorCode"] = filter.ErrorCode,
            ["FromUtc"] = filter.FromUtc, ["ToUtc"] = filter.ToUtc,
            ["Offset"] = (page - 1) * size, ["PageSize"] = size,
        };
        var total = await query.QuerySingleOrDefaultAsync<long>(KafkaDeadLetterSql.Count, parameters, cancellationToken);
        var rows = await query.QueryAsync<KafkaDeadLetterRow>(database.Value.Provider == DatabaseProvider.SqlServer
            ? KafkaDeadLetterSql.ListSqlServer : KafkaDeadLetterSql.ListMySql, parameters, cancellationToken);
        return new PagedResult<KafkaDeadLetterRow>(rows.ToArray(), page, size, total);
    }

    public Task<int> MarkReplayedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken) =>
        command.ExecuteAsync(KafkaDeadLetterSql.MarkReplayed,
            new Dictionary<string, object?> { ["Id"] = id, ["Now"] = now }, cancellationToken);

    private static Dictionary<string, object?> CaptureParameters(IntegrationEventDeadLetterCapture c) => new()
    {
        ["Id"] = c.Id, ["ConsumerName"] = c.ConsumerName, ["DlqTopic"] = c.DlqTopic,
        ["DlqPartition"] = c.DlqPartition, ["DlqOffset"] = c.DlqOffset, ["SourceTopic"] = c.SourceTopic,
        ["SourcePartition"] = c.SourcePartition, ["SourceOffset"] = c.SourceOffset,
        ["MessageId"] = c.MessageId, ["MessageType"] = c.MessageType, ["SchemaVersion"] = c.SchemaVersion,
        ["TenantId"] = c.TenantId, ["Attempts"] = c.Attempts, ["ErrorCode"] = c.ErrorCode,
        ["FirstFailedAtUtc"] = c.FirstFailedAtUtc, ["LastFailedAtUtc"] = c.LastFailedAtUtc,
        ["CanReplay"] = c.CanReplay, ["SnapshotJson"] = c.SnapshotJson,
    };
}

/// <summary>持久投影；原始快照仅供受控重放，不直接序列化成 HTTP 响应。</summary>
internal sealed record KafkaDeadLetterRow(Guid Id, string ConsumerName, string DlqTopic, int DlqPartition,
    long DlqOffset, string SourceTopic, int SourcePartition, long SourceOffset, Guid? MessageId,
    string? MessageType, int? SchemaVersion, Guid? TenantId, int Attempts, string ErrorCode,
    DateTimeOffset FirstFailedAtUtc, DateTimeOffset LastFailedAtUtc, bool CanReplay,
    DateTimeOffset? ReplayedAtUtc, string SnapshotJson);

/// <summary>投影写入使用原始 Broker 位置唯一约束，不覆盖已重放状态和首次快照。</summary>
internal static class KafkaDeadLetterSql
{
    private const string Columns = "Id, ConsumerName, DlqTopic, DlqPartition, DlqOffset, SourceTopic, SourcePartition, SourceOffset, MessageId, MessageType, SchemaVersion, TenantId, Attempts, ErrorCode, FirstFailedAtUtc, LastFailedAtUtc, CanReplay, ReplayedAtUtc";
    private const string Values = "@Id, @ConsumerName, @DlqTopic, @DlqPartition, @DlqOffset, @SourceTopic, @SourcePartition, @SourceOffset, @MessageId, @MessageType, @SchemaVersion, @TenantId, @Attempts, @ErrorCode, @FirstFailedAtUtc, @LastFailedAtUtc, @CanReplay, NULL, @SnapshotJson";
    private const string Filter = """
        WHERE (@ConsumerName IS NULL OR ConsumerName = @ConsumerName)
          AND (@MessageType IS NULL OR MessageType = @MessageType)
          AND (@SchemaVersion IS NULL OR SchemaVersion = @SchemaVersion)
          AND (@ErrorCode IS NULL OR ErrorCode = @ErrorCode)
          AND (@FromUtc IS NULL OR LastFailedAtUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR LastFailedAtUtc <= @ToUtc)
        """;

    public static readonly SqlStatement InsertSqlServer = new("messaging.kafka_dead_letters.insert.sql_server",
        "IF NOT EXISTS (SELECT 1 FROM fn_messaging_dead_letter WITH (UPDLOCK, HOLDLOCK) WHERE DlqTopic = @DlqTopic AND DlqPartition = @DlqPartition AND DlqOffset = @DlqOffset) INSERT INTO fn_messaging_dead_letter (" + Columns + ", SnapshotJson) VALUES (" + Values + ")", SqlDataScope.HostOnly);
    public static readonly SqlStatement InsertMySql = new("messaging.kafka_dead_letters.insert.my_sql",
        "INSERT INTO fn_messaging_dead_letter (" + Columns + ", SnapshotJson) VALUES (" + Values + ") ON DUPLICATE KEY UPDATE Id = Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement Find = new("messaging.kafka_dead_letters.find",
        "SELECT " + Columns + ", SnapshotJson FROM fn_messaging_dead_letter WHERE Id = @Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement Count = new("messaging.kafka_dead_letters.count",
        "SELECT COUNT(*) FROM fn_messaging_dead_letter " + Filter, SqlDataScope.HostOnly);
    public static readonly SqlStatement ListSqlServer = new("messaging.kafka_dead_letters.list.sql_server",
        "SELECT " + Columns + ", '' AS SnapshotJson FROM fn_messaging_dead_letter " + Filter + " ORDER BY LastFailedAtUtc DESC, Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    public static readonly SqlStatement ListMySql = new("messaging.kafka_dead_letters.list.my_sql",
        "SELECT " + Columns + ", '' AS SnapshotJson FROM fn_messaging_dead_letter " + Filter + " ORDER BY LastFailedAtUtc DESC, Id LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    public static readonly SqlStatement MarkReplayed = new("messaging.kafka_dead_letters.mark_replayed", """
        UPDATE fn_messaging_dead_letter SET ReplayedAtUtc = @Now WHERE Id = @Id AND ReplayedAtUtc IS NULL
        """, SqlDataScope.HostOnly);
}
