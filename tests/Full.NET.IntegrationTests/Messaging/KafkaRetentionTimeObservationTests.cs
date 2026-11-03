using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Messaging.Kafka;
using Testcontainers.Kafka;
using DotNet.Testcontainers.Builders;
using System.Text.Json;
using System.Diagnostics;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>独享 Broker 上用真实追加时间、真实消费组提交位置验证时间保留比；不改变共享 Topic。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaRetentionTimeObservationTests
{
    [TestMethod]
    public async Task Real_time_ratio_fires_original_ten_minute_alert_and_committing_the_group_resolves_it()
    {
        await using var server = new KafkaBuilder("apache/kafka:4.1.2").WithEnvironment("KAFKA_HEAP_OPTS", "-Xms256m -Xmx512m")
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var options = new KafkaMessagingOptions { Enabled = true, BootstrapServers = server.GetBootstrapAddress(), ClientId = "time-alert", SecurityProtocol = "Plaintext" };
        using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
        var topic = "time-alert-" + Guid.NewGuid().ToString("N");
        await admin.CreateTopicsAsync([new TopicSpecification
        {
            Name = topic, NumPartitions = 1, ReplicationFactor = 1, Configs = new Dictionary<string, string>
            {
                ["message.timestamp.type"] = "LogAppendTime", ["retention.ms"] = "30000", ["retention.bytes"] = "-1",
                ["cleanup.policy"] = "delete", ["segment.ms"] = "3600000",
            },
        }]);
        var description = await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]));
        var probe = new KafkaRetentionTimeProbe(description.TopicDescriptions.Single().TopicId.ToString(), 1);
        var group = "time-alert-group-" + Guid.NewGuid().ToString("N");
        var partition = new TopicPartition(topic, 0);
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        { BootstrapServers = options.BootstrapServers, GroupId = group, EnableAutoCommit = false }).Build();
        consumer.Assign(partition); consumer.Commit([new TopicPartitionOffset(partition, 0)]);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = options.BootstrapServers, MessageTimeoutMs = 10000 }).Build();
        var firstAppend = await producer.ProduceAsync(topic, new Message<string, string> { Key = "first", Value = "unread" });
        Assert.IsTrue(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - firstAppend.Timestamp.UnixTimestampMs) < 1000,
            "先验证宿主与 Broker 的一秒时间预算，不假定跨系统墙钟零误差。");
        var lastConfirmedOffset = firstAppend.Offset.Value;
        var root = Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("缺少仓库根目录。");
        var directory = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR") ?? throw new InvalidOperationException("缺少证据目录。");
        await using var exporter = await MessagingMetricsTestExporter.StartAsync();
        using var activity = new CancellationTokenSource(TimeSpan.FromMinutes(14));
        var writer = AppendAsync(activity.Token);
        var observer = new KafkaConsumerLagObserver();
        var sampler = SampleAsync(activity.Token);
        var timer = Stopwatch.StartNew();
        try
        {
            var config = Path.Combine(directory, "prometheus-time.yml");
            await File.WriteAllTextAsync(config, $"global:\n  scrape_interval: 2s\n  evaluation_interval: 2s\nrule_files:\n  - /etc/prometheus/fullnet-rules.yaml\nscrape_configs:\n  - job_name: fullnet-messaging-cdc\n    static_configs:\n      - targets: ['host.docker.internal:{exporter.Port}']\n");
            await using var prometheus = new ContainerBuilder("prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e")
                .WithExtraHost("host.docker.internal", "host-gateway").WithPortBinding(9090, true)
                .WithBindMount(config, "/etc/prometheus/prometheus.yml")
                .WithBindMount(Path.Combine(root, "deploy/observability/prometheus-rules.yaml"), "/etc/prometheus/fullnet-rules.yaml")
                .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 512L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready"))).Build();
            await prometheus.StartAsync();
            using var http = new HttpClient { BaseAddress = new($"http://127.0.0.1:{prometheus.GetMappedPublicPort(9090)}"), Timeout = TimeSpan.FromSeconds(5) };
            await WaitAsync(async () => await QueryAsync("fullnet_messaging_kafka_observed_topics_lag_retention_ratio > 0.8"), TimeSpan.FromSeconds(45));
            var pendingAt = timer.Elapsed;
            const string firing = "ALERTS{alertname=\"FullNetMessagingKafkaLagNearRetention\",alertstate=\"firing\"} == 1";
            await WaitAsync(() => QueryAsync(firing), TimeSpan.FromMinutes(11));
            Assert.IsTrue(timer.Elapsed - pendingAt >= TimeSpan.FromMinutes(9.5), "不能缩短生产规则的 10 分钟等待。");
            await File.WriteAllTextAsync(Path.Combine(directory, "time-alert-firing.json"), await http.GetStringAsync("/api/v1/alerts"));
            await activity.CancelAsync(); await Task.WhenAll(writer, sampler);
            using var client = new KafkaLagQueryClient(options);
            var ends = await client.ReadEndOffsetsAsync([partition]);
            Assert.AreEqual(Interlocked.Read(ref lastConfirmedOffset) + 1, ends[partition],
                "只有全部已接受发送得到确认，才能冻结恢复时的真实高水位。");
            consumer.Commit([new TopicPartitionOffset(partition, ends[partition])]);
            var drained = await observer.ObserveLagAsync(options, topic, group, default, probe, publishTelemetry: true);
            Assert.AreEqual(0L, drained!.TotalLagMessages); Assert.AreEqual(0d, drained.LagRetentionRatio);
            await WaitAsync(async () => !await QueryAsync(firing), TimeSpan.FromSeconds(45));
            await File.WriteAllTextAsync(Path.Combine(directory, "time-alert-resolved.json"), await http.GetStringAsync("/api/v1/alerts"));
            await File.WriteAllTextAsync(Path.Combine(directory, "time-alert-summary.json"), JsonSerializer.Serialize(new
            { ElapsedSeconds = timer.Elapsed.TotalSeconds, Committed = ends[partition], TopicRetentionMilliseconds = 30000,
                MaximumClockSkewSeconds = 1, OriginalAlertForSeconds = 600 }));

            async Task<bool> QueryAsync(string expression)
            {
                using var response = await http.GetAsync("/api/v1/query?query=" + Uri.EscapeDataString(expression)); response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("data").GetProperty("result").GetArrayLength() > 0;
            }
            async Task WaitAsync(Func<Task<bool>> condition, TimeSpan budget)
            {
                using var deadline = new CancellationTokenSource(budget);
                while (!await condition())
                {
                    if (writer.IsFaulted) await writer;
                    if (sampler.IsFaulted) await sampler;
                    await Task.Delay(1000, deadline.Token);
                }
            }
        }
        finally { await activity.CancelAsync(); await Task.WhenAll(writer, sampler); consumer.Close(); KafkaMessagingTelemetry.RemoveConsumerState(group); }

        async Task AppendAsync(CancellationToken token)
        {
            try
            {
                // Kafka 按整个段清理：持续追加使含首条未读记录的当前段仍可读，真实演示风险超过保留预算。
                for (var count = 0; count < 1800; count++)
                {
                    // 取消等待不能撤销已入队发送；单条发送受 MessageTimeoutMs 限制，停止后等待其真实确认。
                    var delivered = await producer.ProduceAsync(topic, new Message<string, string> { Key = "keep-segment", Value = "bounded" });
                    Interlocked.Exchange(ref lastConfirmedOffset, delivered.Offset.Value);
                    await Task.Delay(500, token);
                }
                throw new InvalidOperationException("时间告警负载超出 1800 条预算。");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        async Task SampleAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    var sample = await observer.ObserveLagAsync(options, topic, group, token, probe, publishTelemetry: true);
                    if (sample?.LagRetentionRatio is null)
                    {
                        // 失败前保存真实窗口/时间索引，不能靠改参数后通过来猜测原未知原因。
                        var evidence = new List<object>();
                        foreach (var spec in new[] { OffsetSpec.Earliest(), OffsetSpec.Latest(), OffsetSpec.ForTimestamp(0), OffsetSpec.MaxTimestamp() })
                        {
                            var result = await admin.ListOffsetsAsync([new TopicPartitionOffsetSpec { TopicPartition = partition, OffsetSpec = spec }],
                                new ListOffsetsOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
                            var point = result.ResultInfos.Single();
                            evidence.Add(new { point.Timestamp, Offset = point.TopicPartitionOffsetError.Offset.Value, Error = point.TopicPartitionOffsetError.Error.Code });
                        }
                        var json = JsonSerializer.Serialize(new { UtcNow = DateTimeOffset.UtcNow, sample, Points = evidence });
                        await File.WriteAllTextAsync(Path.Combine(directory, "time-alert-unknown.json"), json);
                        var logs = await server.GetLogsAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        var text = logs.Stdout + logs.Stderr;
                        await File.WriteAllTextAsync(Path.Combine(directory, "time-alert-broker-tail.log"), text.Length > 16000 ? text[^16000..] : text);
                        Assert.Fail("真实时间采样不能退化为未知后继续等待告警：" + json);
                    }
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    [TestMethod]
    public async Task Real_append_time_tracks_committed_position_and_unsupported_policies_are_unknown()
    {
        await using var server = new KafkaBuilder("apache/kafka:4.1.2")
            .WithEnvironment("KAFKA_HEAP_OPTS", "-Xms256m -Xmx512m")
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var options = new KafkaMessagingOptions { Enabled = true, BootstrapServers = server.GetBootstrapAddress(), ClientId = "time-observation", SecurityProtocol = "Plaintext" };
        using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
        var names = new[] { "append", "create", "bytes", "infinite", "compact" }.Select(name => "time-" + name + "-" + Guid.NewGuid().ToString("N")).ToArray();
        await admin.CreateTopicsAsync(names.Select((name, index) => new TopicSpecification
        {
            Name = name, NumPartitions = 1, ReplicationFactor = 1,
            Configs = new Dictionary<string, string>
            {
                ["message.timestamp.type"] = index == 1 ? "CreateTime" : "LogAppendTime",
                ["retention.ms"] = index == 3 ? "-1" : "60000", ["retention.bytes"] = index == 2 ? "1048576" : "-1",
                ["cleanup.policy"] = index == 4 ? "compact,delete" : "delete",
            },
        }));
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = options.BootstrapServers, MessageTimeoutMs = 30000 }).Build();
        var first = await producer.ProduceAsync(names[0], new Message<string, string> { Key = "a", Value = "first" });
        await Task.Delay(1200);
        var second = await producer.ProduceAsync(names[0], new Message<string, string> { Key = "b", Value = "second" });
        Assert.AreEqual(0L, first.Offset.Value); Assert.AreEqual(1L, second.Offset.Value);
        var description = await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames(names));
        var topicId = description.TopicDescriptions.Single(item => item.Name == names[0]).TopicId.ToString();
        var group = "time-group-" + Guid.NewGuid().ToString("N");
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        { BootstrapServers = options.BootstrapServers, GroupId = group, EnableAutoCommit = false }).Build();
        var partition = new TopicPartition(names[0], 0);
        consumer.Assign(partition);
        consumer.Commit([new TopicPartitionOffset(partition, 0)]);
        var observer = new KafkaConsumerLagObserver();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var probe = new KafkaRetentionTimeProbe(topicId, 0);
        var initial = await observer.ObserveLagAsync(options, names[0], group, deadline.Token, probe, publishTelemetry: true);
        Assert.AreEqual(2L, initial!.TotalLagMessages);
        Assert.IsTrue(initial.LagRetentionRatio is > 0.015 and < 0.5, $"首条真实追加年龄/60 秒：{initial.LagRetentionRatio}");
        consumer.Commit([new TopicPartitionOffset(partition, 1)]);
        var advanced = await observer.ObserveLagAsync(options, names[0], group, deadline.Token, probe, publishTelemetry: true);
        Assert.AreEqual(1L, advanced!.TotalLagMessages);
        Assert.IsNotNull(advanced.LagRetentionRatio);
        Assert.IsTrue(advanced.LagRetentionRatio < initial.LagRetentionRatio, "提交推进应减少时间风险，而非按消息数量猜测。");
        consumer.Commit([new TopicPartitionOffset(partition, 2)]);
        var drained = await observer.ObserveLagAsync(options, names[0], group, deadline.Token, probe, publishTelemetry: true);
        Assert.AreEqual(0d, drained!.LagRetentionRatio);
        Assert.IsNull((await observer.ObserveLagAsync(options, names[0], group, deadline.Token, new("wrong-topic-id", 0), publishTelemetry: true))!.LagRetentionRatio);
        using var client = new KafkaLagQueryClient(options);
        foreach (var name in names.Skip(1))
        {
            var p = new TopicPartition(name, 0);
            var id = description.TopicDescriptions.Single(item => item.Name == name).TopicId.ToString();
            Assert.IsNull(await client.ReadLagRetentionRatioAsync(name, new Dictionary<TopicPartition, long> { [p] = 0 },
                await client.ReadEndOffsetsAsync([p]), new(id, 0), deadline.Token), "不支持的时间戳/保留策略必须未知。");
        }
        consumer.Close();
    }
}
