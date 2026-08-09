using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

internal class DapperOutboxWriter(
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

    public virtual async Task AddAsync<TEvent>(
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

    public virtual Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        // 为什么忽略 metadata：
        // 切流前业务模块可以用 metadata overload 调用 legacy writer（在 DapperRoutedOutboxWriter
        // 中 owner 仍为 LegacyPolling 时走到这里），此时直接退化为无 metadata 版本写入，
        // 以便在切流当天仅通过 owner 切换生效而无需改业务代码。
        // 切流后的 CdcKafka 流由 append-only writer 单独处理 metadata。
        return AddAsync(eventType, schemaVersion, payload, cancellationToken);
    }
}
