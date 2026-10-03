using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>验证原生 Worker 使用受限 Producer 将启动资源日志投递到 Kafka。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerKafkaLogMySqlE2ETests
{
    [TestMethod]
    public async Task Native_worker_emits_resource_log_to_restricted_kafka_topic()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            .ConfigureAwait(false);
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            DatabaseProvider.MySql, connectionString).ConfigureAwait(false);
        await using var kafka = await KafkaLogTlsFixture.StartAsync(useSasl: true, useAcls: true)
            .ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.native.worker.logging.general.{suffix}";
        var priorityTopic = $"fullnet.native.worker.logging.priority.{suffix}";
        await kafka.EnsureTopicsAsync(generalTopic, priorityTopic).ConfigureAwait(false);
        await kafka.GrantWriterAsync(generalTopic).ConfigureAwait(false);
        await kafka.GrantWriterAsync(priorityTopic).ConfigureAwait(false);

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FullNet:Logging:DeliveryMode"] = "ApplicationKafka",
            ["FullNet:Logging:ExpectedDeliveryMode"] = "ApplicationKafka",
            ["FullNet:Logging:IndexRouteVersion"] = "2",
            ["FullNet:Logging:IndexRetentionDays"] = "30",
            ["FullNet:Logging:Kafka:BootstrapServers"] = kafka.BootstrapServers,
            ["FullNet:Logging:Kafka:GeneralTopic"] = generalTopic,
            ["FullNet:Logging:Kafka:PriorityTopic"] = priorityTopic,
            ["FullNet:Logging:Kafka:SecurityProtocol"] = "SaslSsl",
            ["FullNet:Logging:Kafka:SaslMechanism"] = "Plain",
            ["FullNet:Logging:Kafka:SaslUsername"] = kafka.SaslUsername,
            ["FullNet:Logging:Kafka:SaslPassword"] = kafka.SaslPassword,
            ["FullNet:Logging:Kafka:SslCaLocation"] = kafka.CaPath,
            ["FullNet:Logging:Kafka:MessageTimeoutMs"] = "15000",
            ["FullNet:Logging:Kafka:ShutdownFlushTimeoutMs"] = "15000",
        };
        using var productionAssets = new ProductionLogHostTestAssets();
        productionAssets.ApplyTo(settings);
        await using var host = await NativeWorkerProcessHost.StartAsync(
            artifact,
            DatabaseProvider.MySql,
            connectionString,
            NativeAotTestTimeouts.ProcessStartup,
            additionalSettings: settings).ConfigureAwait(false);

        using var consumer = kafka.CreateReadbackConsumer($"fullnet.native.worker.logging.readback.{suffix}");
        consumer.Assign(new TopicPartitionOffset(generalTopic, new Partition(0), Offset.Beginning));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        ConsumeResult<string, byte[]>? announcement = null;
        while (DateTime.UtcNow < deadline)
        {
            var candidate = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (candidate is not null
                && Encoding.UTF8.GetString(candidate.Message.Value)
                    .Contains("LoggingResource Application=", StringComparison.Ordinal))
            {
                announcement = candidate;
                break;
            }
        }

        Assert.IsNotNull(announcement,
            $"Native Worker 未向 {generalTopic} 投递启动资源日志；进程日志：{host.LogFilePath}");
        using (var json = JsonDocument.Parse(announcement.Message.Value))
        {
            Assert.AreEqual(announcement.Message.Key,
                json.RootElement.GetProperty("LogEventId").GetString());
            Assert.AreEqual("Production", json.RootElement.GetProperty("Environment").GetString());
        }

        await host.StopGracefullyAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Assert.AreEqual(0, host.ExitCode,
            $"Native Worker 未正常响应 SIGTERM；进程日志：{host.LogFilePath}");
        host.AssertNoFatalMarkersInLogs();
    }
}
