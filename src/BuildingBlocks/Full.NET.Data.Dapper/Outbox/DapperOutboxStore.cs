using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Outbox;

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
        var parameters = new
        {
            MessageTypes = messageTypes
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            SchemaVersion = schemaVersion
        };
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

    public Task<OutboxBacklogSnapshot> ReadStreamBacklogAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        var parameters = new
        {
            MessageType = eventType,
            SchemaVersion = schemaVersion,
            Now = clock.UtcNow,
        };
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
            new { MessageType = eventType, SchemaVersion = schemaVersion },
            cancellationToken);
    }

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
                new
                {
                    Ids = distinctMessageIds,
                    LockId = lockId,
                    LockedUntil = clock.UtcNow.Add(lease),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            throw new OutboxLeaseLostException(lockId);
        }
    }

    public async Task MarkProcessedAsync(
        Guid id,
        Guid lockId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor
            .ExecuteAsync(
                OutboxSql.MarkProcessed,
                new { Id = id, LockId = lockId, Now = clock.UtcNow },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleRow(affectedRows, id, lockId);
    }

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
                new
                {
                    Id = id,
                    LockId = lockId,
                    Error = storedError,
                    NextAttemptAt = nextAttemptAt
                },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleRow(affectedRows, id, lockId);
    }

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
                new
                {
                    Id = id,
                    LockId = lockId,
                    Error = storedError,
                    DeadLetterReasonCode = deadLetterReasonCode,
                    DeadLetteredAt = deadLetteredAt
                },
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
                new { Now = clock.UtcNow },
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
                new { Now = clock.UtcNow },
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
                        new
                        {
                            Ids = ids.ToArray(),
                            parameters.LockId,
                            parameters.LockedUntil,
                        },
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
                new { CutoffUtc = cutoffUtc, BatchSize = batchSize },
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
                        new { CutoffUtc = cutoffUtc, BatchSize = batchSize },
                        transactionToken)
                    .ConfigureAwait(false);
                if (ids.Count == 0)
                {
                    return 0;
                }

                var claimedIds = ids.ToArray();
                var affectedRows = await commandExecutor.ExecuteAsync(
                        OutboxSql.DeleteProcessedIdsMySql,
                        new { Ids = claimedIds, CutoffUtc = cutoffUtc },
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

    private sealed record OutboxAcquireParameters(
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

    private sealed class OutboxRow
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

    private sealed class SqlServerBacklogRow
    {
        public long PendingCount { get; init; }
        public DateTimeOffset? OldestOccurredAtUtc { get; init; }
        public long DueRetryCount { get; init; }
        public long ActiveLeaseCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTimeOffset? OldestDeadLetteredAtUtc { get; init; }
    }

    private sealed class MySqlBacklogRow
    {
        public long PendingCount { get; init; }
        public DateTime? OldestOccurredAtUtc { get; init; }
        public long DueRetryCount { get; init; }
        public long ActiveLeaseCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTime? OldestDeadLetteredAtUtc { get; init; }
    }

    private sealed class SqlServerVersionRetirementRow
    {
        public long PendingCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTimeOffset? OldestUnprocessedOccurredAtUtc { get; init; }
    }

    private sealed class MySqlVersionRetirementRow
    {
        public long PendingCount { get; init; }
        public long DeadLetterCount { get; init; }
        public DateTime? OldestUnprocessedOccurredAtUtc { get; init; }
    }

    private sealed class MySqlOutboxRow
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
