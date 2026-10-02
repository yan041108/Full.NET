extern alias consumerhost;

using System.Net;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using Full.NET.LogConsumer;
using KafkaLogDeadLetterSink = consumerhost::KafkaLogDeadLetterSink;
using KafkaLogOffsetCommitter = consumerhost::KafkaLogOffsetCommitter;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 Elasticsearch 与 TLS Kafka 上验证写成功但未提交后的幂等重放。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogConsumerElasticsearchReplayTests
{
    [TestMethod]
    public async Task ElasticsearchWriteBeforeFailedCommitReplaysSameDocumentId()
    {
        await using var elasticsearch = new ContainerBuilder(
            "docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.ml.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(9200).ForPath("/")))
            .Build();
        await elasticsearch.StartAsync();
        var esUri = new UriBuilder("http", elasticsearch.Hostname, elasticsearch.GetMappedPublicPort(9200)).Uri;
        using var plainHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var handler = new ElasticsearchTestTransport(esUri)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            },
        };
        using var secureHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var topic = "fullnet.integration.logging.es-replay." + suffix;
        var group = "fullnet.integration.logging.es-replay-group." + suffix;
        await kafka.EnsureTopicsAsync(topic);
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        var id = Guid.CreateVersion7().ToString("D");
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiry = occurred.AddDays(30);
        var indexName = $"fn-logs-2-diagnostic-{occurred:yyyy.MM.dd}";
        var json = $$"""
            {"@t":"{{occurred:O}}","@mt":"replay","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
            """ + "\n";
        await producer.ProduceAsync(topic,
            new Message<string, byte[]> { Key = id, Value = Encoding.UTF8.GetBytes(json) });
        var sink = new ElasticsearchLogDocumentSink(secureHttp,
            new Uri("https://es-test.invalid/"));
        using (var first = NewConsumer(kafka, group))
        {
            first.Subscribe(topic);
            var input = ToInput(first.Consume(TimeSpan.FromSeconds(20))!);
            var processor = new SequentialLogDeliveryProcessor(sink, new NoDlq(),
                new ThrowingCommitter(), new Dictionary<int, int> { [2] = 30 }, 1024);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => processor.ProcessAsync(input));
            first.Close();
        }

        using (var replay = NewConsumer(kafka, group))
        {
            replay.Subscribe(topic);
            var input = ToInput(replay.Consume(TimeSpan.FromSeconds(20))!);
            Assert.AreEqual(0L, input.Offset);
            var processor = new SequentialLogDeliveryProcessor(sink, new NoDlq(),
                new KafkaLogOffsetCommitter(replay),
                new Dictionary<int, int> { [2] = 30 }, 1024);
            Assert.AreEqual(LogDeliveryDisposition.Completed,
                await processor.ProcessAsync(input));
            Assert.AreEqual(1L, replay.Committed(
                [new TopicPartition(topic, new Partition(0))],
                TimeSpan.FromSeconds(5))[0].Offset.Value);
            replay.Close();
        }

        using var refresh = await plainHttp.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
        Assert.AreEqual(HttpStatusCode.OK, refresh.StatusCode);
        using var count = await plainHttp.GetAsync(new Uri(esUri, indexName + "/_count"));
        Assert.AreEqual(HttpStatusCode.OK, count.StatusCode);
        using var body = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
        Assert.AreEqual(1L, body.RootElement.GetProperty("count").GetInt64());
        using var document = await plainHttp.GetAsync(new Uri(esUri, indexName + "/_doc/" + id));
        Assert.AreEqual(HttpStatusCode.OK, document.StatusCode);
    }

    [TestMethod]
    public async Task ElasticsearchMappingRejectionWaitsForDurableDlqBeforeCommit()
    {
        await using var elasticsearch = new ContainerBuilder(
            "docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("xpack.ml.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(9200).ForPath("/")))
            .Build();
        await elasticsearch.StartAsync();
        var esUri = new UriBuilder("http", elasticsearch.Hostname, elasticsearch.GetMappedPublicPort(9200)).Uri;
        using var plainHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var handler = new ElasticsearchTestTransport(esUri)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
            },
        };
        using var secureHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var topic = "fullnet.integration.logging.es-rejected." + suffix;
        var dlqTopic = "fullnet.integration.logging.es-rejected-dlq." + suffix;
        var group = "fullnet.integration.logging.es-rejected-group." + suffix;
        await kafka.EnsureTopicsAsync(topic, dlqTopic);
        var id = Guid.CreateVersion7().ToString("D");
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiry = occurred.AddDays(30);
        var indexName = $"fn-logs-2-diagnostic-{occurred:yyyy.MM.dd}";
        using (var mapping = await plainHttp.PutAsync(new Uri(esUri, indexName),
                   new StringContent("""
                       {"mappings":{"properties":{"LogProbeNumber":{"type":"long"}}}}
                       """, Encoding.UTF8, "application/json")))
        {
            Assert.AreEqual(HttpStatusCode.OK, mapping.StatusCode);
        }

        var json = $$"""
            {"@t":"{{occurred:O}}","@mt":"mapping-rejected","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2,"LogProbeNumber":"not-a-number"}
            """;
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        await producer.ProduceAsync(topic,
            new Message<string, byte[]> { Key = id, Value = Encoding.UTF8.GetBytes(json) });
        var sink = new ElasticsearchLogDocumentSink(secureHttp,
            new Uri("https://es-test.invalid/"));
        using (var first = NewConsumer(kafka, group))
        {
            first.Subscribe(topic);
            var input = ToInput(first.Consume(TimeSpan.FromSeconds(20))!);
            var processor = new SequentialLogDeliveryProcessor(sink, new RejectingDlq(),
                new KafkaLogOffsetCommitter(first), new Dictionary<int, int> { [2] = 30 }, 1024);
            Assert.AreEqual(LogDeliveryDisposition.Retry,
                await processor.ProcessAsync(input));
            first.Close();
        }

        using var dlqProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
        using (var replay = NewConsumer(kafka, group))
        {
            replay.Subscribe(topic);
            var input = ToInput(replay.Consume(TimeSpan.FromSeconds(20))!);
            Assert.AreEqual(0L, input.Offset);
            var processor = new SequentialLogDeliveryProcessor(sink,
                new KafkaLogDeadLetterSink(dlqProducer, dlqTopic),
                new KafkaLogOffsetCommitter(replay),
                new Dictionary<int, int> { [2] = 30 }, 1024);
            Assert.AreEqual(LogDeliveryDisposition.Completed,
                await processor.ProcessAsync(input));
            Assert.AreEqual(1L, replay.Committed(
                [new TopicPartition(topic, new Partition(0))],
                TimeSpan.FromSeconds(5))[0].Offset.Value);
            replay.Close();
        }

        using var dlqReader = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = group + ".dlq",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        dlqReader.Subscribe(dlqTopic);
        var isolated = dlqReader.Consume(TimeSpan.FromSeconds(20));
        Assert.IsNotNull(isolated);
        Assert.AreEqual(topic + ":0:0", isolated.Message.Key);
        Assert.AreEqual(json, Encoding.UTF8.GetString(isolated.Message.Value));
        Assert.AreEqual("PermanentSinkError",
            Encoding.UTF8.GetString(isolated.Message.Headers.GetLastBytes("isolation-reason")));
        dlqReader.Close();

        using var refresh = await plainHttp.PostAsync(new Uri(esUri, indexName + "/_refresh"), null);
        Assert.AreEqual(HttpStatusCode.OK, refresh.StatusCode);
        using var count = await plainHttp.GetAsync(new Uri(esUri, indexName + "/_count"));
        Assert.AreEqual(HttpStatusCode.OK, count.StatusCode);
        using var body = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
        Assert.AreEqual(0L, body.RootElement.GetProperty("count").GetInt64());
    }

    private static IConsumer<byte[], byte[]> NewConsumer(KafkaLogTlsFixture kafka, string group) =>
        new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();

    private static KafkaLogInput ToInput(ConsumeResult<byte[], byte[]> result) =>
        new(result.Topic, result.Partition.Value, result.Offset.Value,
            Encoding.UTF8.GetString(result.Message.Key), result.Message.Value,
            result.Message.Key);

    private sealed class ElasticsearchTestTransport(Uri target) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("https://es-test.invalid/_bulk", request.RequestUri!.ToString());
            request.RequestUri = new Uri(target, "_bulk");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class NoDlq : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason,
            CancellationToken cancellationToken) => throw new AssertFailedException(
                $"Valid document should not be isolated: {reason}");
    }

    private sealed class RejectingDlq : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(LogIsolationReason.PermanentSinkError, reason);
            return Task.FromResult(false);
        }
    }

    private sealed class ThrowingCommitter : ILogOffsetCommitter
    {
        public void CommitNext(KafkaLogInput input) =>
            throw new InvalidOperationException("injected commit failure");
    }
}
