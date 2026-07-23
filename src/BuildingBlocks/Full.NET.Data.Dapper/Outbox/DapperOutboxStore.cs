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
    IOptions<DatabaseOptions> databaseOptions) : IOutboxStore
{
    private const int MaximumErrorLength = 2000;
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

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
        var parameters = new
        {
            BatchSize = batchSize,
            LockId = idGenerator.NewId(),
            Now = now,
            LockedUntil = now.Add(lease)
        };

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

    private Task<IReadOnlyList<OutboxEnvelope>> AcquireMySqlAsync(
        object parameters,
        CancellationToken cancellationToken) =>
        transaction.ExecuteAsync<IReadOnlyList<OutboxEnvelope>>(
            async token =>
            {
                await commandExecutor
                    .ExecuteAsync(OutboxSql.AcquireMySql, parameters, token)
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
