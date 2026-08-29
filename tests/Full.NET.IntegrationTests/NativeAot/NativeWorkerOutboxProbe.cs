using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>为原生 Worker 写入确定性 Legacy Outbox 探针，并按消息标识读取终态。</summary>
internal static class NativeWorkerOutboxProbe
{
    private const string InsertSql =
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId,
             Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, NULL, @TraceId,
             @Payload, @OccurredAtUtc, 0)
        """;

    private const string SelectStatesSql =
        """
        SELECT Id,
               Attempts,
               CASE WHEN ProcessedAtUtc IS NULL THEN 0 ELSE 1 END AS IsProcessed,
               CASE WHEN DeadLetteredAtUtc IS NULL THEN 0 ELSE 1 END AS IsDeadLettered,
               DeadLetterReasonCode,
               CASE WHEN LockId IS NULL AND LockedUntilUtc IS NULL
                    THEN 1 ELSE 0 END AS IsLeaseReleased,
               CASE WHEN NextAttemptAtUtc IS NULL THEN 1 ELSE 0 END AS IsRetryCleared
        FROM fn_outbox_message
        WHERE Id IN @Ids
        """;

    public static async Task<NativeWorkerOutboxMessages> EnqueueAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var serializer = new MemoryPackIntegrationEventSerializer();
        var validId = Guid.CreateVersion7();
        var invalidId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var route = NotificationRealtimeEventTypes.AnnouncementPublished;
        var contentType = serializer.ContentType;
        var validPayload = serializer.Serialize(
            new AnnouncementPublishedIntegrationEvent(
                Guid.CreateVersion7(),
                "Native Worker Outbox Probe"));
        var invalidPayload = validPayload[..^1];
        Assert.ThrowsExactly<InvalidDataException>(() =>
            serializer.Deserialize<AnnouncementPublishedIntegrationEvent>(invalidPayload));

        await using var connection = CreateConnection(provider, connectionString);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertSql,
                    CreateParameters(
                        provider,
                        validId,
                        route,
                        contentType,
                        validPayload,
                        now),
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition(
                    InsertSql,
                    CreateParameters(
                        provider,
                        invalidId,
                        route,
                        contentType,
                        invalidPayload,
                        now.AddMilliseconds(1)),
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return new NativeWorkerOutboxMessages(validId, invalidId);
    }

    public static async Task<NativeWorkerOutboxTerminalStates> WaitForTerminalStatesAsync(
        DatabaseProvider provider,
        string connectionString,
        NativeWorkerOutboxMessages messages,
        TimeSpan timeout,
        string logFilePath,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var states = await ReadStatesAsync(
                    provider,
                    connectionString,
                    messages,
                    cancellationToken)
                .ConfigureAwait(false);
            if (states.Valid.IsTerminal && states.Invalid.IsTerminal)
            {
                return states;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内写入两个 Outbox 终态。日志：{logFilePath}");
    }

    private static async Task<NativeWorkerOutboxTerminalStates> ReadStatesAsync(
        DatabaseProvider provider,
        string connectionString,
        NativeWorkerOutboxMessages messages,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(provider, connectionString);
        var rows = (await connection.QueryAsync<NativeWorkerOutboxState>(
                new CommandDefinition(
                    SelectStatesSql,
                    new { Ids = new[] { messages.ValidId, messages.InvalidId } },
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false)).ToDictionary(row => row.Id);
        if (!rows.TryGetValue(messages.ValidId, out var valid)
            || !rows.TryGetValue(messages.InvalidId, out var invalid))
        {
            throw new InvalidOperationException("Native Worker Outbox 探针消息在终态检查前丢失。");
        }

        return new NativeWorkerOutboxTerminalStates(valid, invalid);
    }

    private static object CreateParameters(
        DatabaseProvider provider,
        Guid id,
        string messageType,
        string contentType,
        byte[] payload,
        DateTimeOffset occurredAtUtc) => new
        {
            Id = id,
            MessageType = messageType,
            SchemaVersion = 1,
            ContentType = contentType,
            TraceId = $"{id:N}"[..32],
            Payload = payload,
            OccurredAtUtc = provider == DatabaseProvider.MySql
                ? (object)occurredAtUtc.UtcDateTime
                : occurredAtUtc,
        };

    private static DbConnection CreateConnection(
        DatabaseProvider provider,
        string connectionString) => provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };
}

internal sealed record NativeWorkerOutboxMessages(Guid ValidId, Guid InvalidId);

internal sealed record NativeWorkerOutboxTerminalStates(
    NativeWorkerOutboxState Valid,
    NativeWorkerOutboxState Invalid);

internal sealed class NativeWorkerOutboxState
{
    public Guid Id { get; init; }

    public int Attempts { get; init; }

    public long IsProcessed { get; init; }

    public long IsDeadLettered { get; init; }

    public string? DeadLetterReasonCode { get; init; }

    public long IsLeaseReleased { get; init; }

    public long IsRetryCleared { get; init; }

    public bool IsTerminal => IsProcessed == 1 || IsDeadLettered == 1;
}
