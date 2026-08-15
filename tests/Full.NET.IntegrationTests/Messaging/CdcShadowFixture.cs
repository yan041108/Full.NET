using System.Text;
using Confluent.Kafka;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// CDC Shadow 端到端测试共享夹具：Kafka、Outbox 写入与比对辅助。
/// </summary>
internal static class CdcShadowFixture
{
    internal const string ShadowTopicPrefix = "fullnet.dev.shadow";

    internal static string GetShadowTopic(string messageType) =>
        $"{ShadowTopicPrefix}.{messageType}";

    internal static async Task<CommittedOutboxEvent> InsertCommittedOutboxEventAsync(
        DatabaseOptions options,
        string partitionKey)
    {
        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingOutboxTestSupport.BuildAppendOnlyServices(configuration);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var metadata = MessagingOutboxTestSupport.CreateMetadata(partitionKey);
        var payload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload(
            $"cdc-shadow-{partitionKey}");

        var commandTransaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();

        await commandTransaction.ExecuteAsync<bool>(
            async cancellationToken =>
            {
                await scope.ServiceProvider.GetRequiredService<IOutboxWriter>()
                    .AddAsync(
                    MessagingOutboxTestSupport.TestEventType,
                    MessagingOutboxTestSupport.TestSchemaVersion,
                    payload,
                    metadata,
                    cancellationToken);
                return true;
            },
            CancellationToken.None);

        await using System.Data.Common.DbConnection connection =
            options.Provider == DatabaseProvider.SqlServer
                ? new SqlConnection(options.ConnectionString)
                : new MySqlConnection(options.ConnectionString);
        var row = await connection.QuerySingleAsync<OutboxRow>(
            """
            SELECT Id, MessageType, SchemaVersion, PartitionKey, Payload, OccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE PartitionKey = @PartitionKey
            """,
            new { PartitionKey = partitionKey });

        return new CommittedOutboxEvent(
            ShadowEventFingerprint.Create(
                row.Id,
                row.MessageType,
                row.SchemaVersion,
                row.PartitionKey,
                row.Payload,
                row.OccurredAtUtc),
            row.Payload,
            row.OccurredAtUtc);
    }

    internal static async Task InsertRolledBackOutboxAttemptAsync(
        DatabaseOptions options,
        string partitionKey)
    {
        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingOutboxTestSupport.BuildAppendOnlyServices(configuration);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var metadata = MessagingOutboxTestSupport.CreateMetadata(partitionKey);
        var payload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload("rolled-back");

        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>()
                .ExecuteAsync<bool>(
                    async cancellationToken =>
                    {
                        await scope.ServiceProvider.GetRequiredService<IOutboxWriter>()
                            .AddAsync(
                                MessagingOutboxTestSupport.TestEventType,
                                MessagingOutboxTestSupport.TestSchemaVersion,
                                payload,
                                metadata,
                                cancellationToken);
                        throw new InvalidOperationException("rollback-outbox-shadow-test");
                    },
                    CancellationToken.None);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == "rollback-outbox-shadow-test")
        {
        }
    }

    internal static async Task<bool> TryEnableSqlServerCdcAsync(string connectionString)
    {
        var result = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString)
            .ConfigureAwait(false);
        return result.Succeeded;
    }

    internal static async Task<bool> WaitForSqlServerCdcInsertAsync(
        string connectionString,
        Guid eventId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        await using var connection = new SqlConnection(connectionString);
        while (DateTime.UtcNow < deadline)
        {
            var count = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(1)
                FROM cdc.fullnet_fn_messaging_outbox_event_CT
                WHERE Id = @Id AND __$operation = 2
                """,
                new { Id = eventId });
            if (count > 0)
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    internal static async Task<int> CountSqlServerCdcInsertsAsync(
        string connectionString,
        Guid eventId)
    {
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM cdc.fullnet_fn_messaging_outbox_event_CT
            WHERE Id = @Id AND __$operation = 2
            """,
            new { Id = eventId });
    }

    internal static async Task<MySqlBinlogStatus> ReadMySqlBinlogStatusAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        var rows = await connection.QueryAsync<(string Name, string Value)>(
            "SHOW VARIABLES WHERE Variable_name IN ('log_bin', 'binlog_format', 'binlog_row_image')");
        var map = rows.ToDictionary(
            row => row.Name,
            row => row.Value,
            StringComparer.OrdinalIgnoreCase);
        return new MySqlBinlogStatus(
            map.GetValueOrDefault("log_bin"),
            map.GetValueOrDefault("binlog_format"),
            map.GetValueOrDefault("binlog_row_image"));
    }

    internal static string CreateUniqueShadowTopic() =>
        $"fullnet.dev.shadow.test.{Guid.NewGuid():N}";

    internal static async Task PublishShadowMessageToTopicAsync(
        KafkaTestEnvironment environment,
        CommittedOutboxEvent committed,
        string topic)
    {
        using var producer = environment.CreateProducer("fullnet.cdc.shadow.publisher");
        var message = CreateShadowKafkaMessage(committed);
        var delivery = await producer.ProduceAsync(topic, message).ConfigureAwait(false);
        if (delivery.Status != PersistenceStatus.Persisted)
        {
            throw new InvalidOperationException(
                $"Shadow publish failed: {delivery.Status}");
        }
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    internal static Message<string, byte[]> CreateShadowKafkaMessage(CommittedOutboxEvent committed)
    {
        var fingerprint = committed.Fingerprint;
        return new Message<string, byte[]>
        {
            Key = fingerprint.PartitionKey,
            Value = committed.Payload,
            Headers =
            [
                new Header(
                    KafkaEnvelopeHeaderNames.EventId,
                    Encoding.UTF8.GetBytes(fingerprint.EventId.ToString("D"))),
                new Header(
                    KafkaEnvelopeHeaderNames.MessageType,
                    Encoding.UTF8.GetBytes(fingerprint.MessageType)),
                new Header(
                    KafkaEnvelopeHeaderNames.SchemaVersion,
                    Encoding.UTF8.GetBytes(fingerprint.SchemaVersion.ToString())),
                new Header(
                    KafkaEnvelopeHeaderNames.ContentType,
                    Encoding.UTF8.GetBytes(MessagingNames.ContentTypeMessagePack)),
                new Header(
                    KafkaEnvelopeHeaderNames.Producer,
                    Encoding.UTF8.GetBytes("fullnet.messaging.tests")),
                new Header(
                    KafkaEnvelopeHeaderNames.OccurredAtUtc,
                    Encoding.UTF8.GetBytes(committed.OccurredAtUtc.ToString("O"))),
            ],
        };
    }

    internal static ShadowEventComparisonResult CompareKafkaShadowMessage(
        CommittedOutboxEvent committed,
        ConsumeResult<string, byte[]> consumeResult,
        long sequence,
        bool duplicateObserved = false)
    {
        var reader = new KafkaEnvelopeReader();
        if (!reader.TryRead(consumeResult, out var envelope, out _)
            || envelope is null)
        {
            throw new InvalidOperationException("Shadow Kafka message envelope is invalid.");
        }

        var observed = ShadowEventFingerprint.FromEnvelope(envelope);
        var position = new ShadowSourcePosition(
            "kafka",
            $"{consumeResult.Topic}:{consumeResult.Partition.Value}",
            sequence);
        var comparator = new ShadowEventComparator();
        return comparator.CompareExpectedToObserved(
            committed.Fingerprint,
            observed,
            position,
            duplicateObserved);
    }

    internal sealed record CommittedOutboxEvent(
        ShadowEventFingerprint Fingerprint,
        byte[] Payload,
        DateTimeOffset OccurredAtUtc);

    internal sealed record MySqlBinlogStatus(
        string? LogBin,
        string? BinlogFormat,
        string? BinlogRowImage)
    {
        public bool IsRowFullEnabled =>
            string.Equals(LogBin, "ON", StringComparison.OrdinalIgnoreCase)
            && string.Equals(BinlogFormat, "ROW", StringComparison.OrdinalIgnoreCase)
            && string.Equals(BinlogRowImage, "FULL", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OutboxRow(
        Guid Id,
        string MessageType,
        int SchemaVersion,
        string PartitionKey,
        byte[] Payload,
        DateTimeOffset OccurredAtUtc);
}
