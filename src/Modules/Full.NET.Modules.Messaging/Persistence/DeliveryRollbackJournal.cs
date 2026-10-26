using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>模块拥有的回退日志，状态与所有权在同一命令事务中提交。</summary>
internal sealed class DeliveryRollbackJournal(IQueryExecutor query, ICommandExecutor command, IClock clock, IOptions<DatabaseOptions> databaseOptions)
    : IEventDeliveryRollbackJournal
{
    public Task<EventDeliveryRollbackOperation?> FindAsync(Guid generation, CancellationToken cancellationToken = default) =>
        query.QuerySingleOrDefaultAsync<EventDeliveryRollbackOperation>(DeliveryRollbackSql.Find,
            new Dictionary<string, object?> { ["Id"] = generation }, cancellationToken);

    internal Task<int> CreateAsync(Guid generation, string eventType, int schemaVersion, Guid operatorUserId, string reason, CancellationToken cancellationToken) =>
        command.ExecuteAsync(DeliveryRollbackSql.Insert, new Dictionary<string, object?>
        {
            ["Id"] = generation, ["EventType"] = eventType, ["SchemaVersion"] = schemaVersion, ["NowUtc"] = clock.UtcNow,
            ["OperatorUserId"] = operatorUserId, ["Reason"] = reason,
        }, cancellationToken);

    internal Task<EventDeliveryRollbackOperation?> FindLatestAsync(string eventType, int schemaVersion, CancellationToken cancellationToken) =>
        query.QuerySingleOrDefaultAsync<EventDeliveryRollbackOperation>(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
            ? DeliveryRollbackSql.FindLatestSqlServer : DeliveryRollbackSql.FindLatestMySql,
            new Dictionary<string, object?> { ["EventType"] = eventType, ["SchemaVersion"] = schemaVersion }, cancellationToken);

    public async Task<bool> BindResourcesAsync(Guid generation, string resourcesJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcesJson);
        return await command.ExecuteAsync(DeliveryRollbackSql.BindResources, new Dictionary<string, object?>
        {
            ["Id"] = generation, ["ResourcesJson"] = resourcesJson, ["NowUtc"] = clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> TransitionAsync(Guid generation, EventDeliveryRollbackPhase expected,
        EventDeliveryRollbackPhase next, CancellationToken cancellationToken = default) =>
        await command.ExecuteAsync(DeliveryRollbackSql.Transition, new Dictionary<string, object?>
        {
            ["Id"] = generation, ["Expected"] = (int)expected, ["Next"] = (int)next, ["NowUtc"] = clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false) == 1;
}

/// <summary>Host 运维日志 SQL，不访问租户业务表。</summary>
internal static class DeliveryRollbackSql
{
    internal static readonly SqlStatement Find = new("messaging.rollback_operation.find", """
        SELECT Id, EventType, SchemaVersion, Phase, ResourcesJson, CreatedAtUtc, UpdatedAtUtc, HasPauseIntent, OperatorUserId, Reason
        FROM fn_messaging_rollback_operation WHERE Id = @Id
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindLatestSqlServer = new("messaging.rollback_operation.find_latest.sql_server", """
        SELECT TOP (1) Id, EventType, SchemaVersion, Phase, ResourcesJson, CreatedAtUtc, UpdatedAtUtc, HasPauseIntent, OperatorUserId, Reason
        FROM fn_messaging_rollback_operation WHERE EventType = @EventType AND SchemaVersion = @SchemaVersion ORDER BY CreatedAtUtc DESC, Id DESC
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindLatestMySql = new("messaging.rollback_operation.find_latest.my_sql", """
        SELECT Id, EventType, SchemaVersion, Phase, ResourcesJson, CreatedAtUtc, UpdatedAtUtc, HasPauseIntent, OperatorUserId, Reason
        FROM fn_messaging_rollback_operation WHERE EventType = @EventType AND SchemaVersion = @SchemaVersion ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 1
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("messaging.rollback_operation.insert", """
        INSERT INTO fn_messaging_rollback_operation
            (Id, EventType, SchemaVersion, Phase, ResourcesJson, CreatedAtUtc, UpdatedAtUtc, HasPauseIntent, OperatorUserId, Reason)
        VALUES (@Id, @EventType, @SchemaVersion, 0, NULL, @NowUtc, @NowUtc, 0, @OperatorUserId, @Reason)
        """, SqlDataScope.Global);
    internal static readonly SqlStatement BindResources = new("messaging.rollback_operation.bind_resources", """
        UPDATE fn_messaging_rollback_operation SET ResourcesJson = @ResourcesJson, UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND Phase = 0 AND ResourcesJson IS NULL
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Transition = new("messaging.rollback_operation.transition", """
        UPDATE fn_messaging_rollback_operation SET Phase = @Next, HasPauseIntent = CASE WHEN @Next = 1 THEN 1 ELSE HasPauseIntent END, UpdatedAtUtc = @NowUtc
        WHERE Id = @Id AND Phase = @Expected
        """, SqlDataScope.Global);
}
