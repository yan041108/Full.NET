using System.Text;
using Confluent.Kafka;
using Full.NET.Hosting.Observability;
using Full.NET.Logging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>使用真实 Broker 验证日志双通道的投递终态与可消费内容。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogDeliveryTests
{
    [TestMethod]
    public async Task Restricted_writer_can_produce_only_to_granted_topics()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync(useSasl: true, useAcls: true).ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var allowedTopic = $"fullnet.integration.logging.acl.allowed.{suffix}";
        var deniedTopic = $"fullnet.integration.logging.acl.denied.{suffix}";
        await kafka.EnsureTopicsAsync(allowedTopic, deniedTopic).ConfigureAwait(false);
        await kafka.GrantWriterAsync(allowedTopic).ConfigureAwait(false);

        using var accepted = KafkaLogDeliveryLane.Create(new KafkaLogProducerOptions
        {
            BootstrapServers = kafka.BootstrapServers,
            GeneralTopic = allowedTopic,
            PriorityTopic = deniedTopic,
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            SaslUsername = kafka.SaslUsername,
            SaslPassword = kafka.SaslPassword,
            SslCaLocation = kafka.CaPath,
            MessageTimeoutMs = 5_000,
        }, highPriority: false);
        var id = Guid.CreateVersion7().ToString("D");
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            accepted.TryProduce(Encoding.UTF8.GetBytes("{\"kind\":\"allowed\"}"), id));
        await WaitForOutcomeAsync(accepted).ConfigureAwait(false);
        Assert.AreEqual(1, accepted.AcknowledgedCount);
        Assert.AreEqual(0, accepted.FailedCount);
        Assert.AreEqual(0, accepted.ReservedMessages);

        using var unauthorizedReader = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = kafka.SaslUsername,
            SaslPassword = kafka.SaslPassword,
            SslCaLocation = kafka.CaPath,
            GroupId = $"fullnet.integration.logging.acl.reader.{suffix}",
            EnableAutoCommit = false,
        }).Build();
        unauthorizedReader.Assign(new TopicPartitionOffset(allowedTopic, new Partition(0), Offset.Beginning));
        var deniedRead = Assert.ThrowsExactly<ConsumeException>(
            () => unauthorizedReader.Consume(TimeSpan.FromSeconds(5)));
        // 当前 SDK 要求配置 group.id；Broker 在读取前即拒绝未授权的消费组。
        Assert.AreEqual(ErrorCode.GroupAuthorizationFailed, deniedRead.Error.Code);

        using var denied = KafkaLogDeliveryLane.Create(new KafkaLogProducerOptions
        {
            BootstrapServers = kafka.BootstrapServers,
            GeneralTopic = deniedTopic,
            PriorityTopic = allowedTopic,
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            SaslUsername = kafka.SaslUsername,
            SaslPassword = kafka.SaslPassword,
            SslCaLocation = kafka.CaPath,
            MessageTimeoutMs = 5_000,
        }, highPriority: false);
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            denied.TryProduce(Encoding.UTF8.GetBytes("{\"kind\":\"denied\"}"),
                Guid.CreateVersion7().ToString("D")));
        await WaitForOutcomeAsync(denied).ConfigureAwait(false);
        Assert.AreEqual(0, denied.AcknowledgedCount);
        Assert.AreEqual(1, denied.FailedCount);
        Assert.AreEqual(0, denied.ReservedMessages);
    }

    private static async Task WaitForOutcomeAsync(KafkaLogDeliveryLane lane)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (lane.AcknowledgedCount + lane.FailedCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Official_exporter_authenticates_with_sasl_ssl_and_rejects_wrong_password()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync(useSasl: true).ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.integration.logging.sasl.general.{suffix}";
        var priorityTopic = $"fullnet.integration.logging.sasl.priority.{suffix}";
        await kafka.EnsureTopicsAsync(generalTopic, priorityTopic).ConfigureAwait(false);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:DeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:IndexRouteVersion"] = "2",
            [$"{LoggingOptions.SectionName}:IndexRetentionDays"] = "30",
            [$"{KafkaLogProducerOptions.SectionName}:BootstrapServers"] = kafka.BootstrapServers,
            [$"{KafkaLogProducerOptions.SectionName}:GeneralTopic"] = generalTopic,
            [$"{KafkaLogProducerOptions.SectionName}:PriorityTopic"] = priorityTopic,
            [$"{KafkaLogProducerOptions.SectionName}:SecurityProtocol"] = "SaslSsl",
            [$"{KafkaLogProducerOptions.SectionName}:SaslMechanism"] = "Plain",
            [$"{KafkaLogProducerOptions.SectionName}:SaslUsername"] = kafka.SaslUsername,
            [$"{KafkaLogProducerOptions.SectionName}:SaslPassword"] = kafka.SaslPassword,
            [$"{KafkaLogProducerOptions.SectionName}:SslCaLocation"] = kafka.CaPath,
            [$"{KafkaLogProducerOptions.SectionName}:MessageTimeoutMs"] = "15000",
            [$"{KafkaLogProducerOptions.SectionName}:ShutdownFlushTimeoutMs"] = "15000",
        });
        builder.AddFullNetServiceDefaults(KafkaLogSnapshotExporter.Create);
        var marker = $"sasl-priority-{suffix}";
        using (var host = builder.Build())
        {
            host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("KafkaSaslHostIntegration")
                .LogError("{LogMarker}", marker);
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = kafka.SaslUsername,
            SaslPassword = kafka.SaslPassword,
            SslCaLocation = kafka.CaPath,
            GroupId = $"fullnet.integration.logging.sasl.{suffix}",
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign(new TopicPartitionOffset(priorityTopic, new Partition(0), Offset.Beginning));
        var record = consumer.Consume(TimeSpan.FromSeconds(15));
        Assert.IsNotNull(record);
        var payload = Encoding.UTF8.GetString(record.Message.Value);
        StringAssert.Contains(payload, marker);
        StringAssert.Contains(payload, record.Message.Key);

        using var rejected = KafkaLogDeliveryLane.Create(new KafkaLogProducerOptions
        {
            BootstrapServers = kafka.BootstrapServers,
            GeneralTopic = generalTopic,
            PriorityTopic = priorityTopic,
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            SaslUsername = kafka.SaslUsername,
            SaslPassword = "incorrect-test-password",
            SslCaLocation = kafka.CaPath,
            MessageTimeoutMs = 3_000,
        }, highPriority: false);
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            rejected.TryProduce(Encoding.UTF8.GetBytes("{\"kind\":\"denied\"}"),
                Guid.CreateVersion7().ToString("D")));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (rejected.FailedCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        Assert.AreEqual(0, rejected.AcknowledgedCount);
        Assert.AreEqual(1, rejected.FailedCount);
        Assert.AreEqual(0, rejected.ReservedMessages);
    }

    [TestMethod]
    public async Task Official_exporter_connects_to_tls_broker_and_delivers_both_priorities()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync().ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.integration.logging.tls.general.{suffix}";
        var priorityTopic = $"fullnet.integration.logging.tls.priority.{suffix}";
        await kafka.EnsureTopicsAsync(generalTopic, priorityTopic).ConfigureAwait(false);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:DeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:IndexRouteVersion"] = "2",
            [$"{LoggingOptions.SectionName}:IndexRetentionDays"] = "30",
            [$"{KafkaLogProducerOptions.SectionName}:BootstrapServers"] = kafka.BootstrapServers,
            [$"{KafkaLogProducerOptions.SectionName}:GeneralTopic"] = generalTopic,
            [$"{KafkaLogProducerOptions.SectionName}:PriorityTopic"] = priorityTopic,
            [$"{KafkaLogProducerOptions.SectionName}:SecurityProtocol"] = "Ssl",
            [$"{KafkaLogProducerOptions.SectionName}:SslCaLocation"] = kafka.CaPath,
            [$"{KafkaLogProducerOptions.SectionName}:MessageTimeoutMs"] = "15000",
            [$"{KafkaLogProducerOptions.SectionName}:ShutdownFlushTimeoutMs"] = "15000",
        });
        builder.AddFullNetServiceDefaults(KafkaLogSnapshotExporter.Create);
        var generalMarker = $"tls-general-{suffix}";
        var priorityMarker = $"tls-priority-{suffix}";
        using (var host = builder.Build())
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("KafkaTlsHostIntegration");
            logger.LogInformation("{LogMarker}", generalMarker);
            logger.LogError("{LogMarker}", priorityMarker);
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = $"fullnet.integration.logging.tls.{suffix}",
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign([
            new TopicPartitionOffset(generalTopic, new Partition(0), Offset.Beginning),
            new TopicPartitionOffset(priorityTopic, new Partition(0), Offset.Beginning),
        ]);
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (observed.Count < 2 && DateTime.UtcNow < deadline)
        {
            var record = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (record is not null)
            {
                var payload = Encoding.UTF8.GetString(record.Message.Value);
                if (payload.Contains(record.Message.Key, StringComparison.Ordinal))
                {
                    observed[record.Topic] = payload;
                }
            }
        }

        Assert.IsTrue(observed.TryGetValue(generalTopic, out var generalPayload));
        StringAssert.Contains(generalPayload, generalMarker);
        Assert.IsTrue(observed.TryGetValue(priorityTopic, out var priorityPayload));
        StringAssert.Contains(priorityPayload, priorityMarker);
    }

    [TestMethod]
    public async Task Host_ILogger_pipeline_delivers_general_and_priority_snapshots_to_broker()
    {
        var kafka = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.integration.logging.host.general.{suffix}";
        var priorityTopic = $"fullnet.integration.logging.host.priority.{suffix}";
        await kafka.EnsureTopicsAsync(generalTopic, priorityTopic).ConfigureAwait(false);

        var config = new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Plaintext,
            Acks = Acks.All,
            EnableIdempotence = true,
            EnableDeliveryReports = true,
            DeliveryReportFields = "none",
            MessageTimeoutMs = 10_000,
        };
        var general = new KafkaLogDeliveryLane(
            generalTopic, 100, 1_000_000, 100_000,
            new ConfluentKafkaLogProducerClient(config), 10_000);
        var priority = new KafkaLogDeliveryLane(
            priorityTopic, 100, 1_000_000, 100_000,
            new ConfluentKafkaLogProducerClient(config), 10_000, highPriority: true);
        var exporter = new TestHostExporter(
            new KafkaLogProducerPair(general, priority, TimeSpan.FromSeconds(10)));
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Staging,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:DeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:IndexRouteVersion"] = "2",
            [$"{LoggingOptions.SectionName}:IndexRetentionDays"] = "30",
        });
        builder.AddFullNetServiceDefaults(_ => exporter);
        var generalMarker = $"log-general-{suffix}";
        var priorityMarker = $"log-priority-{suffix}";
        using (var host = builder.Build())
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("KafkaHostIntegration");
            logger.LogInformation("{LogMarker}", generalMarker);
            logger.LogError("{LogMarker}", priorityMarker);
        }

        Assert.AreEqual(0, exporter.RejectedCount);
        Assert.IsTrue(general.AcknowledgedCount >= 1);
        Assert.IsTrue(priority.AcknowledgedCount >= 1);
        Assert.AreEqual(0, general.ReservedMessages + priority.ReservedMessages);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"fullnet.integration.logging.host.{suffix}",
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign([
            new TopicPartitionOffset(generalTopic, new Partition(0), Offset.Beginning),
            new TopicPartitionOffset(priorityTopic, new Partition(0), Offset.Beginning),
        ]);
        var seenGeneral = false;
        var seenPriority = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while ((!seenGeneral || !seenPriority) && DateTime.UtcNow < deadline)
        {
            var record = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (record is null)
            {
                continue;
            }

            var payload = Encoding.UTF8.GetString(record.Message.Value);
            if (record.Topic == generalTopic && payload.Contains(generalMarker, StringComparison.Ordinal))
            {
                seenGeneral = payload.Contains(record.Message.Key, StringComparison.Ordinal);
            }
            else if (record.Topic == priorityTopic
                     && payload.Contains(priorityMarker, StringComparison.Ordinal))
            {
                seenPriority = payload.Contains(record.Message.Key, StringComparison.Ordinal);
            }
        }

        Assert.IsTrue(seenGeneral, "普通 ILogger 快照应带同一事件 ID 到普通 Topic。");
        Assert.IsTrue(seenPriority, "优先 ILogger 快照应带同一事件 ID 到优先 Topic。");
    }

    [TestMethod]
    public async Task Broker_outage_releases_failed_reservation_and_recovers_on_same_lane()
    {
        await using var kafka = await KafkaTestEnvironment.StartAsync().ConfigureAwait(false);
        var topic = $"fullnet.integration.logging.recovery.{Guid.NewGuid():N}";
        await kafka.EnsureTopicsAsync(topic).ConfigureAwait(false);
        using var lane = new KafkaLogDeliveryLane(
            topic, 10, 100_000, 10_000,
            new ConfluentKafkaLogProducerClient(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                SecurityProtocol = SecurityProtocol.Plaintext,
                Acks = Acks.All,
                EnableIdempotence = true,
                EnableDeliveryReports = true,
                DeliveryReportFields = "none",
                MessageTimeoutMs = 2_000,
                QueueBufferingMaxMessages = 100,
                QueueBufferingMaxKbytes = 1_024,
            }), 10_000);

        await kafka.PauseBrokerAsync().ConfigureAwait(false);
        try
        {
            Assert.AreEqual(KafkaLogProduceResult.Accepted,
                lane.TryProduce(Encoding.UTF8.GetBytes("{\"phase\":\"outage\"}"),
                    Guid.CreateVersion7().ToString("D")));
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (lane.FailedCount == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.AreEqual(1, lane.FailedCount, "Broker 中断应通过真实投递回调报告失败。");
            Assert.AreEqual(0, lane.ReservedMessages);
            Assert.AreEqual(0L, lane.ReservedBytes);
        }
        finally
        {
            await kafka.ResumeBrokerAsync().ConfigureAwait(false);
        }

        var recoveryId = Guid.CreateVersion7().ToString("D");
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            lane.TryProduce(Encoding.UTF8.GetBytes("{\"phase\":\"recovered\"}"), recoveryId));
        var recoveryDeadline = DateTime.UtcNow.AddSeconds(20);
        while (lane.AcknowledgedCount == 0 && DateTime.UtcNow < recoveryDeadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        Assert.AreEqual(1, lane.AcknowledgedCount, "Broker 恢复后同一 Producer 应重新获得 ACK。");
        Assert.AreEqual(0, lane.ReservedMessages);
        Assert.AreEqual(0L, lane.ReservedBytes);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"fullnet.integration.logging.recovery.{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign(new TopicPartitionOffset(topic, new Partition(0), Offset.Beginning));
        var record = consumer.Consume(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(record);
        Assert.AreEqual(recoveryId, record.Message.Key);
    }

    [TestMethod]
    public async Task General_and_priority_logs_receive_broker_ack_and_remain_readable()
    {
        var kafka = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.integration.logging.general.{suffix}";
        var priorityTopic = $"fullnet.integration.logging.priority.{suffix}";
        await kafka.EnsureTopicsAsync(generalTopic, priorityTopic).ConfigureAwait(false);

        // 本地夹具仅有明文监听；这里只验证实际 SDK/Broker 投递，不改变生产 TLS 配置门禁。
        var config = new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Plaintext,
            Acks = Acks.All,
            EnableIdempotence = true,
            EnableDeliveryReports = true,
            DeliveryReportFields = "none",
            MessageTimeoutMs = 10_000,
            QueueBufferingMaxMessages = 100,
            QueueBufferingMaxKbytes = 1_024,
        };
        using var general = new KafkaLogDeliveryLane(
            generalTopic, 10, 100_000, 10_000,
            new ConfluentKafkaLogProducerClient(config), 10_000);
        using var priority = new KafkaLogDeliveryLane(
            priorityTopic, 10, 100_000, 10_000,
            new ConfluentKafkaLogProducerClient(config), 10_000, highPriority: true);
        using var pair = new KafkaLogProducerPair(general, priority, TimeSpan.FromSeconds(10));

        var generalId = Guid.CreateVersion7().ToString("D");
        var priorityId = Guid.CreateVersion7().ToString("D");
        var generalPayload = Encoding.UTF8.GetBytes("{\"kind\":\"general\"}");
        var priorityPayload = Encoding.UTF8.GetBytes("{\"kind\":\"priority\"}");
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            pair.TryProduce(generalPayload, generalId, highPriority: false));
        Assert.AreEqual(KafkaLogProduceResult.Accepted,
            pair.TryProduce(priorityPayload, priorityId, highPriority: true));

        pair.Dispose();
        Assert.AreEqual(1, general.AcknowledgedCount);
        Assert.AreEqual(1, priority.AcknowledgedCount);
        Assert.AreEqual(0, general.FailedCount + priority.FailedCount);
        Assert.AreEqual(0, general.AbandonedCount + priority.AbandonedCount);
        Assert.AreEqual(0, general.ReservedMessages + priority.ReservedMessages);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"fullnet.integration.logging.{suffix}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Assign([
            new TopicPartitionOffset(generalTopic, new Partition(0), Offset.Beginning),
            new TopicPartitionOffset(priorityTopic, new Partition(0), Offset.Beginning),
        ]);

        var observed = new Dictionary<string, ConsumeResult<string, byte[]>>(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (observed.Count < 2 && DateTime.UtcNow < deadline)
        {
            var record = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (record is not null)
            {
                observed[record.Topic] = record;
            }
        }

        Assert.AreEqual(2, observed.Count, "Broker ACK 后应可分别消费普通与优先 Topic。");
        Assert.AreEqual(generalId, observed[generalTopic].Message.Key);
        CollectionAssert.AreEqual(generalPayload, observed[generalTopic].Message.Value);
        Assert.AreEqual(priorityId, observed[priorityTopic].Message.Key);
        CollectionAssert.AreEqual(priorityPayload, observed[priorityTopic].Message.Value);
    }

    private sealed class TestHostExporter(KafkaLogProducerPair pair) : IHostLogSnapshotExporter
    {
        private int _rejectedCount;

        public int RejectedCount => Volatile.Read(ref _rejectedCount);

        public void Emit(HostLogSnapshot snapshot)
        {
            if (pair.TryProduce(snapshot) != KafkaLogProduceResult.Accepted)
            {
                Interlocked.Increment(ref _rejectedCount);
            }
        }

        public void Dispose() => pair.Dispose();
    }
}
