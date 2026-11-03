using System.Data.Common;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dapper;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modules.Messaging.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    public async Task Kafka_produce_gate_forwards_heartbeat_and_unchanged_target_frames()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        await using var wire = new MessagingKafkaFaultProxy();
        wire.UpstreamPort = (ushort)((IPEndPoint)upstream.LocalEndpoint).Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, wire.Port);
        using var broker = await upstream.AcceptTcpClientAsync();
        var targetId = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
        var gate = wire.ArmSecondProduce("test-connector", "target-topic", targetId);
        var heartbeat = Frame("test-connector", new byte[16], 1);
        var first = Frame("test-connector", targetId, 2, includeHeartbeat: true);
        var second = Frame("test-connector", targetId, 3, includeHeartbeat: true);
        await client.GetStream().WriteAsync(heartbeat);
        await client.GetStream().WriteAsync(first);
        await client.GetStream().WriteAsync(second);
        var received = new byte[heartbeat.Length + first.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await broker.GetStream().ReadExactlyAsync(received, timeout.Token);
        CollectionAssert.AreEqual(heartbeat.Concat(first).ToArray(), received, "心跳及首条业务帧必须原样转发。");
        await gate.Reached.Task.WaitAsync(timeout.Token);
        Assert.IsFalse(broker.GetStream().DataAvailable, "第二条业务帧必须保持阻断。");
        gate.Release.TrySetResult();
        var resumed = new byte[second.Length];
        await broker.GetStream().ReadExactlyAsync(resumed, timeout.Token);
        CollectionAssert.AreEqual(second, resumed, "释放后必须原样转发第二条帧。");

        static byte[] Frame(string clientId, byte[] topicId, byte marker, bool includeHeartbeat = false)
        {
            var name = Encoding.UTF8.GetBytes(clientId);
            const int topicBytes = 16 + 1 + 4 + 1 + 1 + 1 + 1;
            var packet = new byte[4 + 10 + name.Length + 1 + 1 + 6 + 1 + topicBytes * (includeHeartbeat ? 2 : 1) + 1];
            BinaryPrimitives.WriteInt32BigEndian(packet, packet.Length - 4);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(4), 0);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(6), 13);
            BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(12), (short)name.Length);
            name.CopyTo(packet, 14);
            var offset = 14 + name.Length;
            packet[offset++] = 0; // 请求头无扩展字段。
            packet[offset++] = 0; // 非事务性生产者。
            offset += 6; // acks和timeout。
            packet[offset++] = (byte)(includeHeartbeat ? 3 : 2);
            if (includeHeartbeat) WriteTopic(new byte[16]);
            WriteTopic(topicId);
            packet[offset] = 0; // 请求扩展字段为空。
            return packet;

            void WriteTopic(byte[] id)
            {
                id.CopyTo(packet, offset); offset += 16;
                packet[offset++] = 2; // 一个Partition。
                offset += 4; // Partition 0。
                packet[offset++] = 2; packet[offset++] = marker; // 一字节Records。
                packet[offset++] = 0; packet[offset++] = 0; // Partition与Topic扩展字段为空。
            }
        }
    }

    /// <summary>使用真实CDC行、Connector已提交offset和透明Kafka代理检验同事务部分投递。</summary>
    private static async Task VerifyPartialCdcPositionAsync(HttpClient source, DbConnection db, CdcDebeziumPipelineEnvironment pipeline,
        KafkaConnectAdminClient connect, MessagingKafkaFaultProxy wire, string connector, string eventType, string topic, string prefix)
    {
        using var observer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = pipeline.BootstrapServers, GroupId = prefix + ".partial-cdc", EnableAutoCommit = false,
        }).Build();
        var partition = new TopicPartition(topic, new Partition(0));
        observer.Assign(new TopicPartitionOffset(partition, observer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High));
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        var original = await db.ExecuteScalarAsync<Guid>("SELECT TOP (1) Id FROM fn_messaging_outbox_event WHERE MessageType=@type", new { type = eventType });
        const string insert = """
            INSERT INTO fn_messaging_outbox_event (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
                CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
            SELECT @id, @type, @version, ContentType, TenantId, @partitionKey,
                @correlation, CausationId, TraceParent, @producer, Payload, OccurredAtUtc
            FROM fn_messaging_outbox_event WHERE Id=@source
            """;
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = pipeline.BootstrapServers }).Build();
        var metadata = await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]));
        var topicUuid = metadata.TopicDescriptions.Single().TopicId;
        var topicId = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(topicId.AsSpan(0, 8), topicUuid.MostSignificantBits);
        BinaryPrimitives.WriteInt64BigEndian(topicId.AsSpan(8, 8), topicUuid.LeastSignificantBits);
        var connectorConfig = await connect.TryReadConnectorConfigurationAsync(connector);
        Assert.IsNotNull(connectorConfig);
        var heartbeatTopic = connectorConfig["topic.heartbeat.prefix"] + "." + connectorConfig["topic.prefix"];
        using var heartbeat = new ConsumerBuilder<string, byte[]>(new ConsumerConfig { BootstrapServers = pipeline.BootstrapServers,
            GroupId = prefix + ".partial-heartbeat", EnableAutoCommit = false }).Build();
        var heartbeatPartition = new TopicPartition(heartbeatTopic, new Partition(0));
        heartbeat.Assign(new TopicPartitionOffset(heartbeatPartition, heartbeat.QueryWatermarkOffsets(heartbeatPartition, TimeSpan.FromSeconds(10)).High));
        var idleCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event");
        Assert.IsNotNull(await Task.Run(() => heartbeat.Consume(TimeSpan.FromSeconds(45))), "空闲时必须观察到真实Connector心跳记录。");
        Assert.AreEqual(idleCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"), "空闲心跳期间不能依靠新增Outbox事件。");
        var gate = wire.ArmSecondProduce("fullnet-partial-" + connector, topic, topicId);
        // 保留真实业务载荷供消费者按原有幂等路径处理；扩展有界信封字段使两条记录不能合入单一请求。
        var correlation = new string('c', 128);
        var producer = new string('p', 128);
        var partitionKey = new string('k', 256);
        Task<HttpResponseMessage>? rollback = null;
        try
        {
            await using (var transaction = await db.BeginTransactionAsync())
            {
                foreach (var id in new[] { first, second })
                    Assert.AreEqual(1, await db.ExecuteAsync(insert, new { id, type = eventType, version = 1, source = original, correlation, producer, partitionKey }, transaction));
                await transaction.CommitAsync();
            }
            try { await gate.Reached.Task.WaitAsync(TimeSpan.FromMinutes(3)); }
            catch (TimeoutException)
            {
                var arrived = new HashSet<Guid>();
                while (true)
                {
                    var record = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(2)));
                    if (record is null) break;
                    if (new KafkaEnvelopeReader().TryRead(record, out var envelope, out _) && envelope is not null) arrived.Add(envelope.EventId);
                }
                throw new TimeoutException("目标Topic的第二个Produce未到达门禁；匹配请求数=" + Volatile.Read(ref gate.Seen)
                    + "，目标客户端Produce数=" + Volatile.Read(ref gate.ClientRequests) + "，协议版本=" + Volatile.Read(ref gate.ApiVersion)
                    + "，首条已入Broker=" + arrived.Contains(first) + "，第二条已入Broker=" + arrived.Contains(second));
            }
            var firstRecord = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(30)));
            Assert.IsNotNull(firstRecord, "首条真实Produce必须到达目标Broker。");
            Assert.IsTrue(new KafkaEnvelopeReader().TryRead(firstRecord, out var firstEnvelope, out var firstCode), firstCode);
            Assert.AreEqual(first, firstEnvelope!.EventId, "门禁之前必须恰好发送同一事务首条记录。");
            Assert.IsNull(await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(2))), "第二条不能已被合批发送。");

            CdcCapturedRow[] rows = [];
            await WaitKafkaAsync(async () =>
            {
                rows = (await db.QueryAsync<CdcCapturedRow>("SELECT __$start_lsn AS CommitLsn, __$seqval AS Sequence FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id IN @ids ORDER BY __$seqval", new { ids = new[] { first, second } })).ToArray();
                return rows.Length == 2;
            }, "同事务两条真实SQL Server CDC记录");
            CollectionAssert.AreEqual(rows[0].CommitLsn, rows[1].CommitLsn, "两行必须属于同一提交LSN。");
            Assert.IsFalse(rows[0].Sequence.SequenceEqual(rows[1].Sequence), "事务内CDC顺序必须可区分。");
            var fence = CdcDeliveryPosition.ForSqlServerBytes(null, rows[0].CommitLsn);
            CdcDeliveryPosition? observed = null;
            var offsetDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < offsetDeadline)
            {
                observed = await connect.TryReadConnectorPositionAsync(connector);
                if (observed?.Lsn?.CommitLsn == fence.Lsn!.CommitLsn) break;
                await Task.Delay(250);
            }
            Assert.AreEqual(fence.Lsn!.CommitLsn, observed?.Lsn?.CommitLsn, "必须真实读到首条后的同提交LSN offset，不能使用合成位点。");
            Assert.IsFalse(CdcDeliveryPosition.ConnectorCoversProducerFence(fence, observed!), "部分事务的相同commit_lsn不得覆盖生产者屏障。");
            rollback = ControlAsync(source, "/api/v1/messaging/delivery/rollback",
                new ChangeDeliveryOwnerRequest(eventType, 1, EventDeliveryOwner.LegacyPolling, "SQL Server partial CDC transaction"));
            await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>(
                "SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = eventType }) == 1,
                "实际回退必须先建立生产者保护");
            await WaitKafkaAsync(async () => await db.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM fn_messaging_stream_ownership ownership
                INNER JOIN fn_messaging_rollback_operation operation ON operation.Id=ownership.RollbackGeneration
                WHERE ownership.MessageType=@type AND ownership.SchemaVersion=1 AND ownership.RollbackState=1
                  AND operation.Phase=0 AND operation.ResourcesJson IS NOT NULL
                """, new { type = eventType }) == 1, "回退必须绑定真实控制资源并进入位点等待");
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.IsFalse(rollback.IsCompleted, "第二条仍在途时回退不得完成。");
            Assert.AreEqual(fence.Lsn.CommitLsn, (await connect.TryReadConnectorPositionAsync(connector))?.Lsn?.CommitLsn,
                "回退等待期间必须仍处于真实同事务提交位点。");
            Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = eventType }));
            Assert.IsFalse(await connect.IsConnectorPausedAsync(connector), "部分事务尚未越过屏障时不得暂停Connector。");
            var outboxCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event");
            gate.Release.TrySetResult();
            var secondRecord = await Task.Run(() => observer.Consume(TimeSpan.FromSeconds(30)));
            Assert.IsNotNull(secondRecord, "解除门禁后第二条必须实际到达。");
            Assert.IsTrue(new KafkaEnvelopeReader().TryRead(secondRecord, out var secondEnvelope, out var secondCode), secondCode);
            Assert.AreEqual(second, secondEnvelope!.EventId);

            using var completed = await rollback.WaitAsync(TimeSpan.FromMinutes(4));
            Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode, await completed.Content.ReadAsStringAsync());
            Assert.AreEqual(outboxCount, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_messaging_outbox_event"), "完整回退不能依靠额外Outbox事件推进位点。");
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT CurrentOwner FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = eventType }));
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT RollbackState FROM fn_messaging_stream_ownership WHERE MessageType=@type AND SchemaVersion=1", new { type = eventType }));
            Assert.IsTrue(await connect.IsConnectorPausedAsync(connector), "完整回退后原Connector必须保持暂停。");
        }
        finally { gate.Release.TrySetResult(); }
    }

    private sealed class CdcCapturedRow
    {
        public byte[] CommitLsn { get; set; } = [];
        public byte[] Sequence { get; set; } = [];
    }
}
