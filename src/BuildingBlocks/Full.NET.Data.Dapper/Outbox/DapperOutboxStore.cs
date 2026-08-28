using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using DapperSqlParameters = Full.NET.Data.Dapper.DapperSqlParameters;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 传统 Outbox 存储，实现 <see cref="IOutboxStore"/>（消息领取/续租/完成/死信）、
/// <see cref="IOutboxBacklogReader"/>（积压快照）与 <see cref="IOutboxRetentionStore"/>（已处理批量清理）。
/// 仅作用于 <c>fn_outbox_message</c> 表（LegacyPolling 模式），CdcKafka 模式的 append-only 表不经过本类。
/// </summary>
/// <remarks>
/// <para><b>基于 Lease 的并发领取模型：</b>
/// AcquireAsync 分配唯一 LockId 与 LockedUntil 时间戳，将候选行标记为"被当前 Worker 持有"；
/// 其他 Worker 会跳过已被持有的行（LockedUntil > Now），避免重复投递。
/// MySQL 领取采用"先 SELECT ... FOR UPDATE SKIP LOCKED 取主键 → UPDATE 声明所有权 → SELECT 读回"
/// 三步事务，以降低 InnoDB 二级索引上的 gap lock 争用。</para>
/// <para><b>状态机不变量：</b>
/// Pending（Attempts 递增、NextAttemptAt 控制重试）→ Processing（Lease 持有）
/// → Processed（软标记，后续由 Retention 批量 DELETE）/ Failed（含 NextAttemptAt）→ DeadLetter（终态，人工介入）。
/// 每次状态迁移均以 (Id, LockId) 复合条件更新，影响行数不为 1 时抛出
/// <see cref="OutboxConcurrencyException"/>，防止 Lease 过期后被两个 Worker 同时操作。</para>
/// <para><b>Provider 差异：</b>SQL Server 使用单条 UPDATE ... OUTPUT 原子领取；
/// MySQL 因 SKIP LOCKED 语义与 UPDATE 限制分三步事务。
/// 所有读取快照操作对 DATETIME 列统一做 DateTimeKind.Utc 标注。</para>
/// <para><b>错误截断：</b>MarkFailed / MarkDeadLetter 写入的错误信息被截断至 2000 字符以内，防止列溢出。</para>
/// </remarks>
internal sealed class DapperOutboxStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IIdGenerator idGenerator,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
    : IOutboxStore, IOutboxBacklogReader, IOutboxRetentionStore
{
    private const int MaximumErrorLength = 2000;
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

    /// <summary>
    /// 读取全局 Outbox 积压快照（待处理、到期重试、活动 Lease、死信数量与最旧时间戳）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按 Provider 实现的快照结果。</returns>
    public Task<OutboxBacklogSnapshot> ReadBacklogAsync(
        CancellationToken cancellationToken) =>
        _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => ReadSqlServerBacklogAsync(
                cancellationToken),
            DatabaseProvider.MySql => ReadMySqlBacklogAsync(cancellationToken),
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };

    /// <summary>
    /// 读取指定 MessageType 集合与 SchemaVersion 的版本退役快照，用于判断旧版本消息是否全部处理完毕。
    /// </summary>
    /// <param name="messageTypes">非空事件类型集合（内部自动去重）。</param>
    /// <param name="schemaVersion">目标 Schema 版本号（从 1 开始）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待处理 + 死信数量与最旧未处理时间戳。</returns>
    /// <exception cref="ArgumentException">当 messageTypes 为空或含空字符串时抛出。</exception>
    public Task<OutboxVersionRetirementSnapshot> ReadVersionRetirementAsync(
        IReadOnlyCollection<string> messageTypes,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageTypes);
        if (messageTypes.Count == 0
            || messageTypes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-empty Outbox message type is required.",
                nameof(messageTypes));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        var parameters = DapperSqlParameters.Create(
            ("MessageTypes", messageTypes.Distinct(StringComparer.Ordinal).ToArray()),
            ("SchemaVersion", schemaVersion));
        return _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer =>
                ReadSqlServerVersionRetirementAsync(
                    parameters,
                    cancellationToken),
            DatabaseProvider.MySql =>
                ReadMySqlVersionRetirementAsync(
                    parameters,
                    cancellationToken),
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };
    }

    /// <summary>
    /// 读取单条事件流（Event Type + SchemaVersion）粒度的积压快照。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该流的积压与死信统计。</returns>
    public Task<OutboxBacklogSnapshot> ReadStreamBacklogAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        var parameters = DapperSqlParameters.Create(
            ("MessageType", eventType),
            ("SchemaVersion", schemaVersion),
            ("Now", clock.UtcNow));
        return _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => ReadSqlServerStreamBacklogAsync(
                parameters,
                cancellationToken),
            DatabaseProvider.MySql => ReadMySqlStreamBacklogAsync(
                parameters,
                cancellationToken),
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };
    }

    /// <summary>
    /// 读取指定事件流最后一条已产生的事件位置快照，用于 Cutoff 判定与顺序校验。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最后一条事件的快照；若流内暂无事件则返回 null。</returns>
    public Task<OutboxStreamCutoffSnapshot?> ReadLastStreamEventAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        var statement = _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => OutboxSql.FindLastStreamEventSqlServer,
            DatabaseProvider.MySql => OutboxSql.FindLastStreamEventMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };
        return queryExecutor.QuerySingleOrDefaultAsync<OutboxStreamCutoffSnapshot>(
            statement,
            DapperSqlParameters.Create(
                ("MessageType", eventType),
                ("SchemaVersion", schemaVersion)),
            cancellationToken);
    }

    /// <summary>
    /// 领取一批待投递的 Outbox 消息，通过 LockId + LockedUntil 声明临时所有权（Lease-based Claim）。
    /// </summary>
    /// <param name="batchSize">单次最大领取数量；必须大于 0。</param>
    /// <param name="lease">Lease 有效期；超时后其他 Worker 可重新领取。必须大于 Zero。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已成功声明所有权的消息信封列表（可能为空）。每个信封携带本批次唯一 LockId。</returns>
    /// <remarks>
    /// SQL Server 通过 UPDATE ... OUTPUT 原子完成；MySQL 通过显式事务 + SKIP LOCKED 主键声明降低争用。
    /// </remarks>
    public Task<IReadOnlyList<OutboxEnvelope>> AcquireAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        if (lease <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lease),
                lease,
                "The Outbox lease must be greater than zero.");
        }

        var now = clock.UtcNow;
        var parameters = new OutboxAcquireParameters(
            batchSize,
            idGenerator.NewId(),
            now,
            now.Add(lease));

        return _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => AcquireSqlServerAsync(
                parameters,
                cancellationToken),
            DatabaseProvider.MySql => AcquireMySqlAsync(
                parameters,
                cancellationToken),
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };
    }

    /// <summary>
    /// 按时间批量物理删除已标记为 Processed 的 Outbox 行（Retention 清理）。
    /// </summary>
    /// <param name="cutoffUtc">处理时间早于此 UTC 时间戳的 Processed 行将被清理。</param>
    /// <param name="batchSize">单次 DELETE 最大行数；防止长时间锁表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除的行数（介于 0 与 batchSize 之间）。</returns>
    public Task<int> DeleteProcessedBatchAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        return _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => DeleteProcessedSqlServerBatchAsync(
                cutoffUtc,
                batchSize,
                cancellationToken),
            DatabaseProvider.MySql => DeleteProcessedMySqlBatchAsync(
                cutoffUtc,
                batchSize,
                cancellationToken),
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported.")
        };
    }

    /// <summary>
    /// 续租当前 Worker 持有的 Outbox Lease，防止长耗时投递过程中 Lease 超时导致所有权丢失。
    /// </summary>
    /// <param name="messageIds">需要续租的消息 Id 集合（非空、无 Empty、内部去重）。</param>
    /// <param name="lockId">领取时分配的 LockId，必须匹配否则视为已丢失。</param>
    /// <param name="lease">新增的 Lease 时长（必须大于 Zero）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="OutboxLeaseLostException">当影响行数为 0 时抛出，表示至少有一条消息的 Lease 已被其他 Worker 抢占或已迁移至其他状态。</exception>
    public async Task RenewLeaseAsync(
        IReadOnlyCollection<Guid> messageIds,
        Guid lockId,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count == 0 || messageIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "The Outbox message identifiers must not be empty.",
                nameof(messageIds));
        }

        if (lockId == Guid.Empty)
        {
            throw new ArgumentException(
                "The Outbox lock identifier must not be empty.",
                nameof(lockId));
        }

        if (lease <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lease),
                lease,
                "The Outbox lease must be greater than zero.");
        }

        var distinctMessageIds = messageIds.Distinct().ToArray();
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                OutboxSql.RenewLease,
                DapperSqlParameters.Create(
                    ("Ids", distinctMessageIds),
                    ("LockId", lockId),
                    ("LockedUntil", clock.UtcNow.Add(lease))),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            throw new OutboxLeaseLostException(lockId);
        }
    }

    /// <summary>
    /// 将单条 Outbox 消息标记为 Processed（成功投递完毕），软标记后由 Retention Job 物理删除。
    /// </summary>
    /// <param name="id">消息 Id。</param>
    /// <param name="lockId">领取时分配的 LockId，复合条件防止并发冲突。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="OutboxConcurrencyException">当影响行数不为 1 时抛出（Lease 丢失或重复标记）。</exception>
    public async Task MarkProcessedAsync(
        Guid id,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                OutboxSql.MarkProcessed,
                DapperSqlParameters.Create(
                    ("Id", id),
                    ("LockId", lockId),
                    ("Now", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleRow(affectedRows, id, lockId);
    }

    /// <summary>
    /// 将 Outbox 消息标记为 Failed（投递失败但仍可重试），写入错误信息并设定下次重试时间。
    /// </summary>
    /// <param name="id">消息 Id。</param>
    /// <param name="lockId">领取时分配的 LockId，复合条件防止并发冲突。</param>
    /// <param name="error">错误详情；超过 2000 字符会被截断以避免列溢出。</param>
    /// <param name="nextAttemptAt">下次可重试的 UTC 时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="OutboxConcurrencyException">当影响行数不为 1 时抛出。</exception>
    public async Task MarkFailedAsync(
        Guid id,
        Guid lockId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        var storedError = error.Length <= MaximumErrorLength
            ? error
            : error[..MaximumErrorLength];
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                OutboxSql.MarkFailed,
                DapperSqlParameters.Create(
                    ("Id", id),
                    ("LockId", lockId),
                    ("Error", storedError),
                    ("NextAttemptAt", nextAttemptAt)),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleRow(affectedRows, id, lockId);
    }

    /// <summary>
    /// 将 Outbox 消息标记为 DeadLetter（终态，超出重试次数或遇到不可恢复错误），不再自动重试。
    /// </summary>
    /// <param name="id">消息 Id。</param>
    /// <param name="lockId">领取时分配的 LockId，复合条件防止并发冲突。</param>
    /// <param name="error">最终错误详情；超过 2000 字符会被截断。</param>
    /// <param name="deadLetterReasonCode">死信原因码（领域枚举字符串），用于运营分类统计。</param>
    /// <param name="deadLetteredAt">进入死信的 UTC 时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="OutboxConcurrencyException">当影响行数不为 1 时抛出。</exception>
    public async Task MarkDeadLetterAsync(
        Guid id,
        Guid lockId,
        string error,
        string deadLetterReasonCode,
        DateTimeOffset deadLetteredAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(deadLetterReasonCode);
        var storedError = error.Length <= MaximumErrorLength
            ? error
            : error[..MaximumErrorLength];
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                OutboxSql.MarkDeadLetter,
                DapperSqlParameters.Create(
                    ("Id", id),
                    ("LockId", lockId),
                    ("Error", storedError),
                    ("DeadLetterReasonCode", deadLetterReasonCode),
                    ("DeadLetteredAt", deadLetteredAt)),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleRow(affectedRows, id, lockId);
    }

    private async Task<IReadOnlyList<OutboxEnvelope>> AcquireSqlServerAsync(
        object parameters,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor
            .QueryAsync<OutboxRow>(
                OutboxSql.AcquireSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    private async Task<OutboxBacklogSnapshot> ReadSqlServerBacklogAsync(
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<SqlServerBacklogRow>(
                OutboxSql.ReadBacklogSqlServer,
                DapperSqlParameters.Create(("Now", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return new OutboxBacklogSnapshot(
            row?.PendingCount ?? 0,
            row?.OldestOccurredAtUtc)
        {
            DueRetryCount = row?.DueRetryCount ?? 0,
            ActiveLeaseCount = row?.ActiveLeaseCount ?? 0,
            DeadLetterCount = row?.DeadLetterCount ?? 0,
            OldestDeadLetteredAtUtc = row?.OldestDeadLetteredAtUtc,
        };
    }

    private async Task<OutboxBacklogSnapshot> ReadMySqlBacklogAsync(
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<MySqlBacklogRow>(
                OutboxSql.ReadBacklogMySql,
                DapperSqlParameters.Create(("Now", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset? oldestOccurredAtUtc = row?.OldestOccurredAtUtc is { } value
            ? new DateTimeOffset(
                DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : null;
        DateTimeOffset? oldestDeadLetteredAtUtc =
            row?.OldestDeadLetteredAtUtc is { } deadLetteredAt
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(deadLetteredAt, DateTimeKind.Utc))
                : null;
        return new OutboxBacklogSnapshot(
            row?.PendingCount ?? 0,
            oldestOccurredAtUtc)
        {
            DueRetryCount = row?.DueRetryCount ?? 0,
            ActiveLeaseCount = row?.ActiveLeaseCount ?? 0,
            DeadLetterCount = row?.DeadLetterCount ?? 0,
            OldestDeadLetteredAtUtc = oldestDeadLetteredAtUtc,
        };
    }

    private async Task<OutboxBacklogSnapshot> ReadSqlServerStreamBacklogAsync(
        object parameters,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<SqlServerBacklogRow>(
                OutboxSql.ReadStreamBacklogSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new OutboxBacklogSnapshot(
            row?.PendingCount ?? 0,
            row?.OldestOccurredAtUtc)
        {
            DueRetryCount = row?.DueRetryCount ?? 0,
            ActiveLeaseCount = row?.ActiveLeaseCount ?? 0,
            DeadLetterCount = row?.DeadLetterCount ?? 0,
            OldestDeadLetteredAtUtc = row?.OldestDeadLetteredAtUtc,
        };
    }

    private async Task<OutboxBacklogSnapshot> ReadMySqlStreamBacklogAsync(
        object parameters,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<MySqlBacklogRow>(
                OutboxSql.ReadStreamBacklogMySql,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset? oldestOccurredAtUtc = row?.OldestOccurredAtUtc is { } value
            ? new DateTimeOffset(
                DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : null;
        DateTimeOffset? oldestDeadLetteredAtUtc =
            row?.OldestDeadLetteredAtUtc is { } deadLetteredAt
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(deadLetteredAt, DateTimeKind.Utc))
                : null;
        return new OutboxBacklogSnapshot(
            row?.PendingCount ?? 0,
            oldestOccurredAtUtc)
        {
            DueRetryCount = row?.DueRetryCount ?? 0,
            ActiveLeaseCount = row?.ActiveLeaseCount ?? 0,
            DeadLetterCount = row?.DeadLetterCount ?? 0,
            OldestDeadLetteredAtUtc = oldestDeadLetteredAtUtc,
        };
    }

    private async Task<OutboxVersionRetirementSnapshot>
        ReadSqlServerVersionRetirementAsync(
            object parameters,
            CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<SqlServerVersionRetirementRow>(
                OutboxSql.ReadVersionRetirementSqlServer,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        return new OutboxVersionRetirementSnapshot(
            row?.PendingCount ?? 0,
            row?.DeadLetterCount ?? 0,
            row?.OldestUnprocessedOccurredAtUtc);
    }

    private async Task<OutboxVersionRetirementSnapshot>
        ReadMySqlVersionRetirementAsync(
            object parameters,
            CancellationToken cancellationToken)
    {
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<MySqlVersionRetirementRow>(
                OutboxSql.ReadVersionRetirementMySql,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset? oldestUnprocessedOccurredAtUtc =
            row?.OldestUnprocessedOccurredAtUtc is { } value
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : null;
        return new OutboxVersionRetirementSnapshot(
            row?.PendingCount ?? 0,
            row?.DeadLetterCount ?? 0,
            oldestUnprocessedOccurredAtUtc);
    }

    private Task<IReadOnlyList<OutboxEnvelope>> AcquireMySqlAsync(
        OutboxAcquireParameters parameters,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync<IReadOnlyList<OutboxEnvelope>>(
            async token =>
            {
                // 先跳过终态更新持有的行锁，再按已锁定主键领取，避免待处理索引上的长时间等待。
                var ids = await queryExecutor
                    .QueryAsync<Guid>(
                        OutboxSql.SelectClaimableIdsMySql,
                        parameters,
                        token)
                    .ConfigureAwait(false);
                if (ids.Count == 0)
                {
                    return Array.Empty<OutboxEnvelope>();
                }

                await commandExecutor
                    .ExecuteAsync(
                        OutboxSql.ClaimByIdsMySql,
                        DapperSqlParameters.Create(
                            ("Ids", ids.ToArray()),
                            ("LockId", parameters.LockId),
                            ("LockedUntil", parameters.LockedUntil)),
                        token)
                    .ConfigureAwait(false);
                var rows = await queryExecutor
                    .QueryAsync<MySqlOutboxRow>(
                        OutboxSql.SelectMySqlLease,
                        parameters,
                        token)
                    .ConfigureAwait(false);
                return rows.Select(Map).ToArray();
            },
            cancellationToken);

    private async Task<int> DeleteProcessedSqlServerBatchAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                OutboxSql.DeleteProcessedSqlServer,
                DapperSqlParameters.Create(
                    ("CutoffUtc", cutoffUtc),
                    ("BatchSize", batchSize)),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureAffectedRowsWithinBatch(affectedRows, batchSize);
        return affectedRows;
    }

    private Task<int> DeleteProcessedMySqlBatchAsync(
        DateTimeOffset cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync(
            async transactionToken =>
            {
                var ids = await queryExecutor.QueryAsync<Guid>(
                        OutboxSql.SelectProcessedIdsMySql,
                        DapperSqlParameters.Create(
                            ("CutoffUtc", cutoffUtc),
                            ("BatchSize", batchSize)),
                        transactionToken)
                    .ConfigureAwait(false);
                if (ids.Count == 0)
                {
                    return 0;
                }

                var claimedIds = ids.ToArray();
                var affectedRows = await commandExecutor.ExecuteAsync(
                        OutboxSql.DeleteProcessedIdsMySql,
                        DapperSqlParameters.Create(
                            ("Ids", claimedIds),
                            ("CutoffUtc", cutoffUtc)),
                        transactionToken)
                    .ConfigureAwait(false);
                if (affectedRows != claimedIds.Length)
                {
                    throw new InvalidOperationException(
                        "Outbox retention did not delete every claimed MySQL row.");
                }

                return affectedRows;
            },
            cancellationToken);

    /// <summary>领取命令参数；internal 以便 Native AOT 绑定器在基础设施注册中可见。</summary>
    internal sealed record OutboxAcquireParameters(
        int BatchSize,
        Guid LockId,
        DateTimeOffset Now,
        DateTimeOffset LockedUntil);

    private static OutboxEnvelope Map(MySqlOutboxRow row) => new(
        row.Id,
        row.LockId,
        row.MessageType,
        row.SchemaVersion,
        row.ContentType,
        row.TenantId,
        row.TraceId,
        row.Payload,
        row.Attempts,
        new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc)));

    private static OutboxEnvelope Map(OutboxRow row) => new(
        row.Id,
        row.LockId,
        row.MessageType,
        row.SchemaVersion,
        row.ContentType,
        row.TenantId,
        row.TraceId,
        row.Payload,
        row.Attempts,
        row.OccurredAtUtc);

    private static void EnsureSingleRow(int affectedRows, Guid id, Guid lockId)
    {
        if (affectedRows != 1)
        {
            throw new OutboxConcurrencyException(id, lockId);
        }
    }

    private static void EnsureAffectedRowsWithinBatch(
        int affectedRows,
        int batchSize)
    {
        if (affectedRows < 0 || affectedRows > batchSize)
        {
            throw new InvalidOperationException(
                "Outbox retention affected rows outside the configured batch.");
        }
    }

    /// <summary>SQL Server 领取结果行；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class OutboxRow
    {
        public Guid Id { get; init; }
        public Guid LockId { get; init; }
        public string MessageType { get; init; } = string.Empty;
        public int SchemaVersion { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public Guid? TenantId { get; init; }
        public string? TraceId { get; init; }
        public byte[] Payload { get; init; } = [];
        public int Attempts { get; init; }
        public DateTimeOffset OccurredAtUtc { get; init; }
    }

    /// <summary>SQL Server 积压聚合行；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class SqlServerBacklogRow
    {
        public long PendingCount { get; init; }
        public DateTimeOffset? OldestOccurredAtUtc { get; init; }
        public long DueRetryCount { get; init; }
        public long ActiveLeaseCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTimeOffset? OldestDeadLetteredAtUtc { get; init; }
    }

    /// <summary>MySQL 积压聚合行；时间列以 DATETIME 返回，internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class MySqlBacklogRow
    {
        public long PendingCount { get; init; }
        public DateTime? OldestOccurredAtUtc { get; init; }
        public long DueRetryCount { get; init; }
        public long ActiveLeaseCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTime? OldestDeadLetteredAtUtc { get; init; }
    }

    /// <summary>SQL Server 版本退役聚合行；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class SqlServerVersionRetirementRow
    {
        public long PendingCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTimeOffset? OldestUnprocessedOccurredAtUtc { get; init; }
    }

    /// <summary>MySQL 版本退役聚合行；internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class MySqlVersionRetirementRow
    {
        public long PendingCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTime? OldestUnprocessedOccurredAtUtc { get; init; }
    }

    /// <summary>MySQL 领取结果行；OccurredAtUtc 为 DATETIME，internal 以便 Native AOT 物化器注册可见。</summary>
    internal sealed class MySqlOutboxRow
    {
        public Guid Id { get; init; }
        public Guid LockId { get; init; }
        public string MessageType { get; init; } = string.Empty;
        public int SchemaVersion { get; init; }
        public string ContentType { get; init; } = string.Empty;
        public Guid? TenantId { get; init; }
        public string? TraceId { get; init; }
        public byte[] Payload { get; init; } = [];
        public int Attempts { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }
}
