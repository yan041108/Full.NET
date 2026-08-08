using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

internal sealed class DapperAppendOnlyOutboxWriter(
    ICommandExecutor commandExecutor,
    IIntegrationEventSerializer serializer,
    IIdGenerator idGenerator,
    ICurrentTenant currentTenant,
    IClock clock) : IOutboxWriter
{
    private static readonly SqlStatement InsertStatement = new(
        "messaging.outbox.append",
        """
        INSERT INTO fn_messaging_outbox_event
            (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
             CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @PartitionKey,
             @CorrelationId, @CausationId, @TraceParent, @Producer, @Payload, @OccurredAtUtc)
        """,
        SqlDataScope.Global);

    public Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Append-only messaging outbox requires IntegrationEventMetadata; use the metadata overload.");

    public async Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(eventType)
            || eventType.Length > MessagingNames.MessageTypeMaxLength
            || !MessagingNames.MessageTypePattern.IsMatch(eventType))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.MessageTypeInvalid,
                nameof(eventType));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The schema version must be at least 1.");
        }

        var message = new AppendOnlyOutboxMessage(
            idGenerator.NewId(),
            eventType,
            schemaVersion,
            serializer.ContentType,
            currentTenant.Id,
            metadata.PartitionKey,
            metadata.CorrelationId,
            metadata.CausationId,
            ResolveTraceParent(),
            metadata.Producer,
            serializer.Serialize(payload),
            clock.UtcNow);

        var affectedRows = await commandExecutor
            .ExecuteAsync(InsertStatement, message, cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Append-only outbox insert affected {affectedRows} rows instead of one.");
        }
    }

    /// <summary>
    /// 仅写入符合 W3C traceparent 格式的当前 Activity 标识；非法格式忽略以免污染 CDC Header。
    /// </summary>
    private static string? ResolveTraceParent()
    {
        var activityId = Activity.Current?.Id;
        if (activityId is null
            || activityId.Length > MessagingNames.TraceParentMaxLength
            || !MessagingNames.TraceParentPattern.IsMatch(activityId))
        {
            return null;
        }

        return activityId;
    }
}