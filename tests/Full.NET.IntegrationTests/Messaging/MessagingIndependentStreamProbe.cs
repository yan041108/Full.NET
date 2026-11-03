using System.Data.Common;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>隔离库中的第二条真实 CDC 物理探针；不冒充已切流业务或 Native Worker 消费。</summary>
internal sealed class MessagingIndependentStreamProbe : IAsyncDisposable
{
    private const string ProbeType = "fullnet.messaging.isolation.probe";
    private const string OtherType = "fullnet.messaging.isolation.other";
    private readonly DbConnection _connection;
    private readonly KafkaConnectAdminClient _connect;
    private readonly KafkaMessagingOptions _kafka;
    private readonly string _connector;
    private readonly string _topic;
    private readonly Guid _source;
    private readonly string _evidence;

    private MessagingIndependentStreamProbe(DbConnection connection, KafkaConnectAdminClient connect,
        KafkaMessagingOptions kafka, string connector, string topic, Guid source, string evidence)
    {
        _connection = connection; _connect = connect; _kafka = kafka; _connector = connector;
        _topic = topic; _source = source; _evidence = evidence;
    }

    internal static async Task<MessagingIndependentStreamProbe> StartAsync(DatabaseOptions database, string sourceDatabase,
        DbConnection connection, CdcDebeziumPipelineEnvironment pipeline, KafkaConnectAdminClient connect,
        IAdminClient broker, KafkaMessagingOptions kafka, int databasePort, string sourceEventType)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var connector = "fullnet-messaging-probe-" + suffix;
        var topic = "messaging.multistream-probe-" + suffix + ".v1";
        await broker.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var source = await connection.ExecuteScalarAsync<Guid>(
            "SELECT Id FROM fn_messaging_outbox_event WHERE MessageType=@sourceEventType", new { sourceEventType });
        Assert.AreNotEqual(Guid.Empty, source, "物理探针必须复制本轮真实业务事件的信封。 ");
        var config = await MessagingCutoverRollbackDebeziumTests.ConnectorAsync(database, pipeline, true);
        config["database.port"] = databasePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        config["schema.history.internal.kafka.topic"] += "." + connector;
        var isolated = new Dictionary<string, string>(CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(connector, config), StringComparer.Ordinal);
        MessagingMultiProcessRecoveryTests.ConfigureDedicatedStream(isolated, database.Provider, sourceDatabase, ProbeType, 1, topic, true);
        var probe = new MessagingIndependentStreamProbe(connection, connect, kafka, connector, topic, source,
            Path.Combine(Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!, "independent-stream-probes.ndjson"));
        try
        {
            await connect.RegisterConnectorAsync(connector, isolated);
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connector, TimeSpan.FromSeconds(120)), "第二个专属 Connector 必须真实运行。");
            // 过滤器可能丢弃全部历史事件；先写入并确认匹配事件，才能要求 Connector 提交实际来源位点。
            await probe.VerifyAsync("bootstrap");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
            while (await connect.TryReadConnectorPositionAsync(connector) is null)
            {
                Assert.IsTrue(DateTimeOffset.UtcNow < deadline, "第二个 Connector 必须提交真实初始来源位点。");
                await Task.Delay(250);
            }
            return probe;
        }
        catch
        {
            await connect.DeleteConnectorAsync(connector);
            throw;
        }
    }

    internal async Task VerifyAsync(string stage)
    {
        Assert.IsTrue(await _connect.WaitForConnectorHealthyAsync(_connector, TimeSpan.FromSeconds(30)), "探针 Connector 必须持续健康。");
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers, GroupId = "fullnet.messaging.probe." + Guid.NewGuid().ToString("N"),
            EnableAutoCommit = false,
        }).Build();
        var partition = new TopicPartition(_topic, new Partition(0));
        var start = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High;
        consumer.Assign(new TopicPartitionOffset(partition, start));
        var otherType = Guid.CreateVersion7(); var otherVersion = Guid.CreateVersion7(); var marker = Guid.CreateVersion7();
        await using var transaction = await _connection.BeginTransactionAsync();
        const string copy = """
            INSERT INTO fn_messaging_outbox_event (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            SELECT @id, @type, @version, ContentType, TenantId, PartitionKey,
                CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc
            FROM fn_messaging_outbox_event WHERE Id=@source
            """;
        foreach (var (id, type, version) in new[] { (otherType, OtherType, 1), (otherVersion, ProbeType, 2), (marker, ProbeType, 1) })
        {
            // SQL Server 触发器的 NOCOUNT 会让 ExecuteNonQuery 返回 -1；以同事务内真实行数为准。
            await _connection.ExecuteAsync(copy, new { id, type, version, source = _source }, transaction);
            Assert.AreEqual(1, await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fn_messaging_outbox_event WHERE Id=@id", new { id }, transaction));
        }
        await transaction.CommitAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var record = consumer.Consume(TimeSpan.FromSeconds(1));
            if (record is null) continue;
            Assert.IsTrue(new KafkaEnvelopeReader().TryRead(record, out var envelope, out _), "第二条流的实际信封必须可解析。");
            Assert.IsNotNull(envelope);
            Assert.AreNotEqual(otherType, envelope.EventId, "不同事件类型不得进入探针 Topic。");
            Assert.AreNotEqual(otherVersion, envelope.EventId, "不同 Schema 版本不得进入探针 Topic。");
            Assert.AreEqual(marker, envelope.EventId, "探针 Topic 只允许本轮匹配信封。");
            await File.AppendAllTextAsync(_evidence, JsonSerializer.Serialize(new { Stage = stage, Connector = _connector,
                Topic = _topic, Matched = 1, OtherTypeFiltered = true, OtherVersionFiltered = true, SyntheticProbe = true }) + Environment.NewLine);
            return;
        }
        Assert.Fail("第二个真实 Connector 没有在限定时间内投递匹配事件。");
    }

    public async ValueTask DisposeAsync() => await _connect.DeleteConnectorAsync(_connector);
}
