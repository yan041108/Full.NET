using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

internal sealed class DapperOutboxWriter(
    ICommandExecutor commandExecutor,
    IIntegrationEventSerializer serializer,
    IIdGenerator idGenerator,
    ICurrentTenant currentTenant,
    IClock clock) : IOutboxWriter
{
    private static readonly SqlStatement InsertStatement = new(
        "outbox.insert",
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAtUtc, 0)
        """,
        SqlDataScope.Global);

    public async Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The schema version must be at least 1.");
        }

        var message = new OutboxMessage(
            idGenerator.NewId(),
            eventType,
            schemaVersion,
            serializer.ContentType,
            currentTenant.Id,
            Activity.Current?.TraceId.ToString(),
            serializer.Serialize(payload),
            clock.UtcNow);

        var affectedRows = await commandExecutor
            .ExecuteAsync(InsertStatement, message, cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Outbox insert affected {affectedRows} rows instead of one.");
        }
    }

    public Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Metadata-aware outbox writes require append-only messaging outbox (migration 091).");
    }
}
