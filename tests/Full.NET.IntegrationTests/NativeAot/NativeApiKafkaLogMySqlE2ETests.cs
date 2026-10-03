using System.Net;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Messaging;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>验证 Native Host.Api 的日志 Producer 通过 TLS/ACL 向真实 Kafka 发送启动事件。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiKafkaLogMySqlE2ETests
{
    [TestMethod]
    public async Task Native_api_emits_general_and_priority_logs_to_restricted_kafka_topics()
    {
        if (!NativeApiArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            .ConfigureAwait(false);
        await NativeApiDatabaseBootstrap.BootstrapAsync(
            DatabaseProvider.MySql, connectionString).ConfigureAwait(false);
        await using var kafka = await KafkaLogTlsFixture.StartAsync(useSasl: true, useAcls: true)
            .ConfigureAwait(false);
        var suffix = Guid.NewGuid().ToString("N");
        var generalTopic = $"fullnet.native.logging.general.{suffix}";
        var priorityTopic = $"fullnet.native.logging.priority.{suffix}";
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
            ["Observability:HttpOperation:SlowRequestThreshold"] = "00:00:00",
        };
        using var productionAssets = new ProductionLogHostTestAssets();
        productionAssets.ApplyTo(settings);
        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            DatabaseProvider.MySql,
            connectionString,
            settings,
            NativeAotTestTimeouts.ProcessStartup).ConfigureAwait(false);

        using var client = host.CreateClient();
        using var live = await client.GetAsync("/health/live").ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
        using var missing = await client.GetAsync("/api/v1/logging-native-test-missing")
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);

        using var consumer = kafka.CreateReadbackConsumer($"fullnet.native.logging.readback.{suffix}");
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

        Assert.IsNotNull(announcement, $"Native Host.Api 未向 {generalTopic} 投递资源日志；进程日志：{host.LogFilePath}");
        using (var generalJson = JsonDocument.Parse(announcement.Message.Value))
        {
            Assert.AreEqual(announcement.Message.Key,
                generalJson.RootElement.GetProperty("LogEventId").GetString());
            Assert.AreEqual("Production", generalJson.RootElement.GetProperty("Environment").GetString());
        }

        consumer.Assign(new TopicPartitionOffset(priorityTopic, new Partition(0), Offset.Beginning));
        ConsumeResult<string, byte[]>? priority = null;
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var candidate = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (candidate is not null
                && IsRequestedHttpOperation(candidate.Message.Value))
            {
                priority = candidate;
                break;
            }
        }

        Assert.IsNotNull(priority, $"Native Host.Api 未向 {priorityTopic} 投递 HTTP Priority 日志；进程日志：{host.LogFilePath}");
        using (var priorityJson = JsonDocument.Parse(priority.Message.Value))
        {
            Assert.AreEqual(priority.Message.Key,
                priorityJson.RootElement.GetProperty("LogEventId").GetString());
        }
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.StopGracefullyAsync(stopTimeout.Token).ConfigureAwait(false);
        Assert.AreEqual(0, host.ExitCode, $"Native Host.Api 退出异常；进程日志：{host.LogFilePath}");
        host.AssertNoFatalMarkersInLogs();
    }

    private static bool IsRequestedHttpOperation(byte[] payload)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        return root.TryGetProperty("EventName", out var eventName)
            && eventName.GetString() == "HttpOperationCompleted"
            && root.TryGetProperty("http.method", out var method)
            && method.GetString() == "GET"
            && root.TryGetProperty("http.route", out var route)
            && route.GetString() == "<unmatched>"
            && root.TryGetProperty("http.status_code", out var status)
            && status.GetInt32() == 404;
    }
}
