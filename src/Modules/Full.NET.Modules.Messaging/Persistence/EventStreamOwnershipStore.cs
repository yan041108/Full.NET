using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>
/// 事件流所有权的持久化存储，提供读取、CAS Upsert 与回退两阶段准备/解除能力。
/// </summary>
/// <remarks>
/// <see cref="UpsertAsync"/> 的 UPDATE 分支以 <c>CurrentOwner = PreviousOwner</c> 作为 CAS 守卫，
/// 影响行数为 0 表示并发期间所有权已被其他事务变更，抛出
/// <see cref="EventStreamOwnershipConcurrencyException"/> 供调用方翻译为冲突错误。
/// 回退准备与解除同样以 owner 与 rollback state 为 CAS 守卫，保证同一事件流只有一个进行中的回退代次。
/// </remarks>
internal sealed class EventStreamOwnershipStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IOptions<DatabaseOptions> databaseOptions) : IEventStreamOwnershipStore
{
    /// <summary>
    /// 按事件类型与版本查找当前持久化的所有权记录；不存在时返回 null。
    /// </summary>
    public async Task<EventStreamOwnershipRecord?> FindAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<EventStreamOwnershipPersistenceRow>(
                EventStreamOwnershipSql.FindByStream,
                CreateStreamKeyParameters(messageType, schemaVersion),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : EventStreamOwnershipMapper.ToRecord(row);
    }

    public async Task<IReadOnlyList<EventStreamOwnershipRecord>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<EventStreamOwnershipPersistenceRow>(
                EventStreamOwnershipSql.ListAll,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(EventStreamOwnershipMapper.ToRecord).ToArray();
    }

    /// <summary>
    /// 插入或按 CAS 更新事件流所有权；并发冲突时抛出 <see cref="EventStreamOwnershipConcurrencyException"/>。
    /// </summary>
    /// <remarks>
    /// 已有记录时执行以 <c>CurrentOwner = PreviousOwner</c> 为守卫的 UPDATE，影响行数为 0 即并发冲突；
    /// 调用方应捕获该异常并翻译为可重试的 conflict 错误，不得静默重试或覆盖。
    /// </remarks>
    public async Task UpsertAsync(
        EventStreamOwnershipRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var persistenceRow = EventStreamOwnershipMapper.ToPersistenceRow(record);
        var existing = await FindAsync(record.MessageType, record.SchemaVersion, cancellationToken)
            .ConfigureAwait(false);
        int affected;
        if (existing is null)
        {
            affected = await commandExecutor.ExecuteAsync(
                EventStreamOwnershipSql.Insert,
                CreatePersistenceParameters(persistenceRow),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            affected = await commandExecutor.ExecuteAsync(
                EventStreamOwnershipSql.Update,
                CreatePersistenceParameters(persistenceRow),
                cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                // SQL UPDATE 使用基于 PreviousOwner 的 CAS，匹配失败意味着在事务读取
                // currentOwner 之后，另一事务已经改变了所有权。调用方应捕获并翻译成
                // conflict 错误。
                throw new EventStreamOwnershipConcurrencyException(
                    record.MessageType,
                    record.SchemaVersion,
                    record.PreviousOwner,
                    existing.CurrentOwner);
            }
        }
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Stream ownership upsert affected {affected} rows instead of 1.");
        }
    }

    internal async Task<bool> TryBeginRollbackPreparationAsync(
        string messageType,
        int schemaVersion,
        Guid rollbackGeneration,
        DateTimeOffset preparedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var affected = await commandExecutor.ExecuteAsync(
            EventStreamOwnershipSql.BeginRollbackPreparation,
            new Dictionary<string, object?>
            {
                ["MessageType"] = messageType,
                ["SchemaVersion"] = schemaVersion,
                ["RollbackGeneration"] = rollbackGeneration,
                ["RollbackPreparedAtUtc"] = preparedAtUtc,
            },
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    internal Task<RollbackPreparationRecord?> FindRollbackPreparationAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        queryExecutor.QuerySingleOrDefaultAsync<RollbackPreparationRecord>(
            EventStreamOwnershipSql.FindRollbackPreparation,
            CreateStreamKeyParameters(messageType, schemaVersion),
            cancellationToken);

    internal async Task<bool> TryAbortRollbackPreparationAsync(
        string messageType,
        int schemaVersion,
        Guid rollbackGeneration,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var affected = await commandExecutor.ExecuteAsync(
            EventStreamOwnershipSql.AbortRollbackPreparation,
            new Dictionary<string, object?>
            {
                ["MessageType"] = messageType,
                ["SchemaVersion"] = schemaVersion,
                ["RollbackGeneration"] = rollbackGeneration,
                ["UpdatedAtUtc"] = updatedAtUtc,
            },
            cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    internal Task<OutboxStreamCutoffRecord?> FindLastAppendOnlyOutboxEventAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        FindLastOutboxEventAsync(
            messageType,
            schemaVersion,
            cancellationToken);

    private async Task<OutboxStreamCutoffRecord?> FindLastOutboxEventAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => EventStreamOwnershipSql.FindLastAppendOnlyOutboxEventByStreamSqlServer,
            DatabaseProvider.MySql => EventStreamOwnershipSql.FindLastAppendOnlyOutboxEventByStreamMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };
        return await queryExecutor
            .QuerySingleOrDefaultAsync<OutboxStreamCutoffRecord>(
                statement,
                CreateStreamKeyParameters(messageType, schemaVersion),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object?> CreateStreamKeyParameters(
        string messageType,
        int schemaVersion) =>
        new Dictionary<string, object?>
        {
            ["MessageType"] = messageType,
            ["SchemaVersion"] = schemaVersion,
        };

    private static IReadOnlyDictionary<string, object?> CreatePersistenceParameters(
        EventStreamOwnershipPersistenceRow row) =>
        new Dictionary<string, object?>
        {
            ["MessageType"] = row.MessageType,
            ["SchemaVersion"] = row.SchemaVersion,
            ["TopicCode"] = row.TopicCode,
            ["CurrentOwner"] = row.CurrentOwner,
            ["PreviousOwner"] = row.PreviousOwner,
            ["CutoffEventId"] = row.CutoffEventId,
            ["CutoffOccurredAtUtc"] = row.CutoffOccurredAtUtc,
            ["CdcSourcePositionJson"] = row.CdcSourcePositionJson,
            ["OperatorUserId"] = row.OperatorUserId,
            ["Reason"] = row.Reason,
            ["RollbackBoundaryEventId"] = row.RollbackBoundaryEventId,
            ["RollbackOccurredAtUtc"] = row.RollbackOccurredAtUtc,
            ["RollbackState"] = row.RollbackState,
            ["RollbackGeneration"] = row.RollbackGeneration,
            ["RollbackPreparedAtUtc"] = row.RollbackPreparedAtUtc,
            ["CreatedAtUtc"] = row.CreatedAtUtc,
            ["UpdatedAtUtc"] = row.UpdatedAtUtc,
        };
}
