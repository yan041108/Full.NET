using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>Host 重放操作的持久模型；请求和检查点只在受控服务中解析，不直接返回原始 JSON。</summary>
internal sealed record KafkaReplayOperationRow(Guid Id, Guid RequestId, string EventType, int SchemaVersion, string TopicCode,
    string ConsumerName, string RequestJson, string? SnapshotJson, string State, int AttemptCount, Guid? LeaseToken,
    DateTimeOffset? LeaseUntilUtc, int? InFlightPartition, long? InFlightOffset, long ProcessedCount, long AlreadyProcessedCount,
    long RejectedCount, string? LastReasonCode, Guid OperatorUserId, string OperatorDisplayName, string Reason,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>模块仅访问自有重放表；进度提交独立于 Inbox 事务，以执行代次和预期意图拒绝过期写入。</summary>
internal sealed class KafkaReplayOperationStore(IQueryExecutor query, ICommandExecutor command, IOptions<DatabaseOptions> options)
{
    internal Task<KafkaReplayOperationRow?> FindAsync(Guid id, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<KafkaReplayOperationRow>(KafkaReplayOperationSql.Find,
            new Dictionary<string, object?> { ["Id"] = id }, token);

    internal Task<KafkaReplayOperationRow?> FindRequestAsync(Guid requestId, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<KafkaReplayOperationRow>(KafkaReplayOperationSql.FindRequest,
            new Dictionary<string, object?> { ["RequestId"] = requestId }, token);

    internal Task<IReadOnlyList<KafkaReplayOperationRow>> ListAsync(int offset, int pageSize, CancellationToken token) =>
        query.QueryAsync<KafkaReplayOperationRow>(options.Value.Provider == DatabaseProvider.SqlServer
            ? KafkaReplayOperationSql.ListSqlServer : KafkaReplayOperationSql.ListMySql,
            new Dictionary<string, object?> { ["Offset"] = offset, ["PageSize"] = pageSize }, token);

    internal Task<long> CountAsync(CancellationToken token) => query.QuerySingleOrDefaultAsync<long>(KafkaReplayOperationSql.Count, cancellationToken: token);

    internal Task<int> InsertAsync(KafkaReplayOperationRow row, CancellationToken token) => command.ExecuteAsync(KafkaReplayOperationSql.Insert,
        Parameters(row), token);

    internal Task<int> BeginAsync(KafkaReplayOperationRow row, Guid leaseToken, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken token) =>
        command.ExecuteAsync(KafkaReplayOperationSql.Begin, new Dictionary<string, object?>
        {
            ["Id"] = row.Id, ["ExpectedState"] = row.State, ["ExpectedAttempt"] = row.AttemptCount,
            ["LeaseToken"] = leaseToken, ["LeaseUntilUtc"] = leaseUntil, ["NowUtc"] = now,
        }, token);

    internal Task<int> SaveProgressAsync(KafkaReplayOperationRow previous, KafkaReplayOperationRow next, DateTimeOffset now,
        bool beforeMessage, CancellationToken token)
    {
        var parameters = Parameters(next);
        parameters["NowUtc"] = now;
        parameters["ExpectedPartition"] = previous.InFlightPartition;
        parameters["ExpectedOffset"] = previous.InFlightOffset;
        parameters["BeforeMessage"] = beforeMessage ? 1 : 0;
        return command.ExecuteAsync(KafkaReplayOperationSql.Progress, parameters, token);
    }

    internal Task<int> FinishAsync(KafkaReplayOperationRow row, string state, string? reasonCode, DateTimeOffset now,
        bool requireLiveLease, CancellationToken token) => command.ExecuteAsync(KafkaReplayOperationSql.Finish,
        new Dictionary<string, object?>
        {
            ["Id"] = row.Id, ["LeaseToken"] = row.LeaseToken, ["ExpectedState"] = row.State, ["State"] = state,
            ["LastReasonCode"] = reasonCode, ["NowUtc"] = now, ["RequireLiveLease"] = requireLiveLease ? 1 : 0,
        }, token);

    internal Task<int> CancelAsync(KafkaReplayOperationRow row, string state, DateTimeOffset now, CancellationToken token) =>
        command.ExecuteAsync(KafkaReplayOperationSql.Cancel, new Dictionary<string, object?>
        {
            ["Id"] = row.Id, ["ExpectedState"] = row.State, ["ExpectedAttempt"] = row.AttemptCount,
            ["State"] = state, ["NowUtc"] = now,
        }, token);

    private static Dictionary<string, object?> Parameters(KafkaReplayOperationRow row) => new()
    {
        ["Id"] = row.Id, ["RequestId"] = row.RequestId, ["EventType"] = row.EventType, ["SchemaVersion"] = row.SchemaVersion,
        ["TopicCode"] = row.TopicCode, ["ConsumerName"] = row.ConsumerName, ["RequestJson"] = row.RequestJson,
        ["SnapshotJson"] = row.SnapshotJson, ["State"] = row.State, ["AttemptCount"] = row.AttemptCount,
        ["LeaseToken"] = row.LeaseToken, ["LeaseUntilUtc"] = row.LeaseUntilUtc, ["InFlightPartition"] = row.InFlightPartition,
        ["InFlightOffset"] = row.InFlightOffset, ["ProcessedCount"] = row.ProcessedCount, ["AlreadyProcessedCount"] = row.AlreadyProcessedCount,
        ["RejectedCount"] = row.RejectedCount, ["LastReasonCode"] = row.LastReasonCode, ["OperatorUserId"] = row.OperatorUserId,
        ["OperatorDisplayName"] = row.OperatorDisplayName, ["Reason"] = row.Reason,
        ["CreatedAtUtc"] = row.CreatedAtUtc, ["UpdatedAtUtc"] = row.UpdatedAtUtc,
    };
}

/// <summary>固定 SQL 形状；全局仅用于受 Host 权限保护的 Messaging 自有操作日志。</summary>
internal static class KafkaReplayOperationSql
{
    internal const string Columns = "Id, RequestId, EventType, SchemaVersion, TopicCode, ConsumerName, RequestJson, SnapshotJson, State, AttemptCount, LeaseToken, LeaseUntilUtc, InFlightPartition, InFlightOffset, ProcessedCount, AlreadyProcessedCount, RejectedCount, LastReasonCode, OperatorUserId, OperatorDisplayName, Reason, CreatedAtUtc, UpdatedAtUtc";
    internal static readonly SqlStatement Find = new("messaging.replay_operation.find", $"SELECT {Columns} FROM fn_messaging_replay_operation WHERE Id = @Id", SqlDataScope.Global);
    internal static readonly SqlStatement FindRequest = new("messaging.replay_operation.find_request", $"SELECT {Columns} FROM fn_messaging_replay_operation WHERE RequestId = @RequestId", SqlDataScope.Global);
    internal static readonly SqlStatement Count = new("messaging.replay_operation.count", "SELECT COUNT(*) FROM fn_messaging_replay_operation", SqlDataScope.Global);
    internal static readonly SqlStatement ListSqlServer = new("messaging.replay_operation.list.sql_server", $"SELECT {Columns} FROM fn_messaging_replay_operation ORDER BY CreatedAtUtc DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.Global);
    internal static readonly SqlStatement ListMySql = new("messaging.replay_operation.list.my_sql", $"SELECT {Columns} FROM fn_messaging_replay_operation ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("messaging.replay_operation.insert", """
        INSERT INTO fn_messaging_replay_operation
            (Id, RequestId, EventType, SchemaVersion, TopicCode, ConsumerName, RequestJson, SnapshotJson, State, AttemptCount,
             LeaseToken, LeaseUntilUtc, InFlightPartition, InFlightOffset, ProcessedCount, AlreadyProcessedCount, RejectedCount,
             LastReasonCode, OperatorUserId, OperatorDisplayName, Reason, CreatedAtUtc, UpdatedAtUtc)
        VALUES (@Id, @RequestId, @EventType, @SchemaVersion, @TopicCode, @ConsumerName, @RequestJson, NULL, 'prepared', 0,
                NULL, NULL, NULL, NULL, 0, 0, 0, NULL, @OperatorUserId, @OperatorDisplayName, @Reason, @CreatedAtUtc, @UpdatedAtUtc)
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Begin = new("messaging.replay_operation.begin", """
        UPDATE fn_messaging_replay_operation
        SET State = 'running', AttemptCount = AttemptCount + 1, LeaseToken = @LeaseToken, LeaseUntilUtc = @LeaseUntilUtc,
            LastReasonCode = NULL, UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND State = @ExpectedState AND AttemptCount = @ExpectedAttempt AND State <> 'completed'
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Progress = new("messaging.replay_operation.progress", """
        UPDATE fn_messaging_replay_operation
        SET SnapshotJson = @SnapshotJson, InFlightPartition = @InFlightPartition, InFlightOffset = @InFlightOffset,
            ProcessedCount = @ProcessedCount, AlreadyProcessedCount = @AlreadyProcessedCount, RejectedCount = @RejectedCount,
            UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND LeaseToken = @LeaseToken AND LeaseUntilUtc > @NowUtc
          AND State IN ('running', 'cancel_requested') AND (@BeforeMessage = 0 OR State = 'running')
          AND ((InFlightOffset IS NULL AND @ExpectedOffset IS NULL)
               OR (InFlightPartition = @ExpectedPartition AND InFlightOffset = @ExpectedOffset))
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Finish = new("messaging.replay_operation.finish", """
        UPDATE fn_messaging_replay_operation
        SET State = @State, LastReasonCode = @LastReasonCode, LeaseUntilUtc = NULL, UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND LeaseToken = @LeaseToken AND State = @ExpectedState
          AND State IN ('running', 'cancel_requested') AND (@RequireLiveLease = 0 OR LeaseUntilUtc > @NowUtc)
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Cancel = new("messaging.replay_operation.cancel", """
        UPDATE fn_messaging_replay_operation SET State = @State, LastReasonCode = 'operator_cancelled', UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND State = @ExpectedState AND AttemptCount = @ExpectedAttempt AND State <> 'completed'
        """, SqlDataScope.Global);
}
