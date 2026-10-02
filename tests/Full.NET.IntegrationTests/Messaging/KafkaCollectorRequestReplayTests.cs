extern alias kafkabenchmarks;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using Runner = kafkabenchmarks::Full.NET.Benchmarks.Logging.LoggingRequestLatencyRunner;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class KafkaCollectorRequestReplayTests
{
    [TestMethod]
    public Task Collector_streams_http_logs_and_commits_before_requests_complete()
        => RunAsync(live: true);

    [TestMethod]
    public Task Collector_cri_requests_reach_tls_kafka_and_https_es_without_application_kafka_mirrors()
        => RunAsync(live: false);

    private static async Task RunAsync(bool live)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var directory = Path.Combine(root.FullName, live ? "artifacts/logging-live-collector" : "artifacts/logging-collector-chain");
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "result.json");
        File.Delete(reportPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        object? requestReport = live ? null : await Runner.RunCollectorCaseAsync(directory, deadline.Token);
        var source = live ? [] : await File.ReadAllLinesAsync(Path.Combine(directory, "Projected-1.jsonl"), deadline.Token);
        var received = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        object? proof = null;
        KafkaRequestConsumerProcess? processProof = null;
        var suffix = Guid.NewGuid().ToString("N");
        var general = "fullnet.collector.general." + suffix;
        var priority = "fullnet.collector.priority." + suffix;
        var dlq = "fullnet.collector.dlq." + suffix;
        var group = "fullnet.collector." + suffix;
        var duringRequests = new List<ConsumeResult<byte[], byte[]>>();
        long committedDuringRequests = 0;
        var httpDuringRequests = 0;
        TopicPartitionOffset? measuredHttpPosition = null;
        await using (var kafka = await KafkaLogTlsFixture.StartAsync(cancellationToken: deadline.Token, enableCollectorListener: true))
        await using (var es = await KafkaRequestElasticsearchFixture.StartTlsAsync(deadline.Token))
        {
            await kafka.EnsureTopicsAsync(deadline.Token, general, priority, dlq);
            await using var process = await KafkaRequestConsumerProcess.StartAsync(kafka, es, general, priority, group, dlq, directory, deadline.Token);
            processProof = process;
            using var reader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl, SslCaLocation = kafka.CaPath,
                GroupId = group, EnableAutoCommit = false, EnableAutoOffsetStore = false,
            }).Build();
            reader.Assign([new TopicPartitionOffset(general, 0, Offset.Beginning), new TopicPartitionOffset(priority, 0, Offset.Beginning)]);
            await using var collector = await KafkaRequestCollectorFixture.StartAsync(root.FullName, directory, kafka,
                general, priority, source, deadline.Token, live ? Path.Combine(directory, "cri") : null);
            if (live)
            {
                await using var bridge = await LiveCollectorCriBridge.StartAsync(Path.Combine(directory, "Projected-1.jsonl"), Path.Combine(directory, "cri"), deadline.Token);
                using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                var measuring = 0;
                var measuredStartedUtc = DateTimeOffset.MaxValue;
                var requestTask = Runner.RunCollectorCaseAsync(directory, deadline.Token,
                    active =>
                    {
                        if (active) measuredStartedUtc = DateTimeOffset.UtcNow;
                        Interlocked.Exchange(ref measuring, active ? 1 : 0);
                    });
                async Task ObserveAsync()
                {
                    while (!requestTask.IsCompleted && !monitoring.IsCancellationRequested)
                    {
                        var record = reader.Consume(TimeSpan.FromMilliseconds(20));
                        if (record is not null)
                        {
                            duringRequests.Add(record);
                            Assert.IsTrue(duringRequests.Count <= 10000, "在途收据须保持有界。");
                            using var json = JsonDocument.Parse(record.Message.Value);
                            if (Volatile.Read(ref measuring) == 1 && json.RootElement.GetProperty("@mt").GetString() == "HttpOperationCompleted"
                                && json.RootElement.GetProperty("OccurredAtUtc").GetDateTimeOffset() >= measuredStartedUtc)
                            {
                                httpDuringRequests++;
                                measuredHttpPosition ??= new TopicPartitionOffset(record.TopicPartition, record.Offset.Value + 1);
                            }
                        }
                        if (Volatile.Read(ref measuring) == 1 && httpDuringRequests > 0 && committedDuringRequests == 0)
                        {
                            var positions = reader.Committed(reader.Assignment, TimeSpan.FromSeconds(2));
                            var confirmed = positions.Single(position => position.TopicPartition == measuredHttpPosition!.TopicPartition);
                            if (Volatile.Read(ref measuring) == 1 && confirmed.Offset.Value >= measuredHttpPosition!.Offset.Value)
                                committedDuringRequests = confirmed.Offset.Value;
                        }
                        await Task.Delay(25, monitoring.Token);
                    }
                }
                var observeTask = ObserveAsync();
                try { requestReport = await requestTask; }
                finally
                {
                    await monitoring.CancelAsync();
                    try { await observeTask; } catch (OperationCanceledException) when (monitoring.IsCancellationRequested) { }
                }
                await bridge.CompleteAsync();
                source = await File.ReadAllLinesAsync(Path.Combine(directory, "Projected-1.jsonl"), deadline.Token);
                Assert.IsTrue(httpDuringRequests > 0 && committedDuringRequests > 0, "必须在请求结束前收到 HTTP 日志并实际提交 Offset。");
            }
            var originals = source.ToDictionary(line => JsonNode.Parse(line)!["LogEventId"]!.GetValue<string>(), line => JsonNode.Parse(line));
            Assert.IsTrue(originals.Count >= 5200);
            var observed = new Queue<ConsumeResult<byte[], byte[]>>(duringRequests);
            var removed = new HashSet<string>(["DiagnosticGroup", "kubernetes", "tenant_id", "user_id"], StringComparer.Ordinal);
            var added = new HashSet<string>(["time", "stream", "_p", "collector.timestamp"], StringComparer.Ordinal);
            while (seen.Count < originals.Count)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Assert.IsTrue(process.IsAlive);
                var record = observed.Count > 0 ? observed.Dequeue() : reader.Consume(TimeSpan.FromMilliseconds(100));
                if (record is null) continue;
                var key = Encoding.UTF8.GetString(record.Message.Key);
                var line = Encoding.UTF8.GetString(record.Message.Value);
                var json = JsonNode.Parse(line)!.AsObject();
                Assert.AreEqual(key, json["LogEventId"]!.GetValue<string>());
                Assert.IsTrue(originals.TryGetValue(key, out var original) && seen.Add(key), "来源缺失、重复或镜像进入 Broker。");
                var expectedPriority = original!["reliability.class"]?.GetValue<string>() == "Priority"
                    || original["@l"]?.GetValue<string>() is "Error" or "Fatal";
                Assert.AreEqual(expectedPriority ? priority : general, record.Topic);
                foreach (var pair in original.AsObject())
                    if (!removed.Contains(pair.Key)) Assert.IsTrue(JsonNode.DeepEquals(pair.Value, json[pair.Key]), "来源字段变化：" + pair.Key);
                foreach (var pair in json)
                    Assert.IsTrue(!removed.Contains(pair.Key) && (original.AsObject().ContainsKey(pair.Key) || added.Contains(pair.Key)), "非允许采集字段：" + pair.Key);
                Assert.AreEqual("F", json["_p"]!.GetValue<string>());
                received.Add(line);
            }
            while (!await collector.VerifyInputDrainedAsync(kafka.CollectorMetricsUri, source.Length, deadline.Token))
            {
                deadline.Token.ThrowIfCancellationRequested();
                var extra = reader.Consume(TimeSpan.FromMilliseconds(100));
                Assert.IsNull(extra, "来源已齐全后仍收到重复或镜像。");
                await Task.Delay(250, deadline.Token);
            }
            // 有限输入全部抵达后先停采集器，再冻结高位点，防止尾部重复未被发现。
            await collector.StopAsync(deadline.Token);
            var ends = reader.Assignment.ToDictionary(partition => partition,
                partition => reader.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High.Value);
            Assert.AreEqual((long)source.Length, ends.Values.Sum(), "Broker 不得包含被排除镜像或重复。");
            var committed = reader.Committed(ends.Keys, TimeSpan.FromSeconds(10));
            while (committed.Any(position => position.Offset.Value != ends[position.TopicPartition]))
            {
                deadline.Token.ThrowIfCancellationRequested();
                Assert.IsTrue(process.IsAlive);
                await Task.Delay(250, deadline.Token);
                committed = reader.Committed(ends.Keys, TimeSpan.FromSeconds(10));
            }
            Assert.IsTrue(process.IsAlive);
            Assert.AreEqual(0L, reader.QueryWatermarkOffsets(new TopicPartition(dlq, 0), TimeSpan.FromSeconds(10)).High.Value);
            await File.WriteAllLinesAsync(Path.Combine(directory, "collector.broker.jsonl"), received, deadline.Token);
            proof = new
            {
                documents = await es.VerifyAsync(received, deadline.Token), sourceRecords = source.Length,
                collectorInputRecords = source.Length * 2L,
                httpDuringRequests, committedDuringRequests,
                measuredHttpTopic = measuredHttpPosition?.Topic,
                measuredHttpOffset = measuredHttpPosition is null ? (long?)null : measuredHttpPosition.Offset.Value - 1,
                rejectedApplicationKafkaMirrors = source.Length, standaloneProcessId = process.Id,
                committedOffsets = committed.Select(position => new { topic = position.Topic, offset = position.Offset.Value, brokerEnd = ends[position.TopicPartition] }).ToArray(),
            };
        }
        Assert.IsTrue(processProof!.Stopped);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            passed = true, cleanupCompleted = true, standaloneProcessStopped = processProof.Stopped, requestReport, proof,
            scope = live
                ? "Real Collector HTTP Console continuously bridged to CRI while requests run; fixed Fluent Bit TLS Kafka and standalone HTTPS ES consumer; HTTP receipts and committed offsets observed before requests complete; test bridge shares client/server process CPU and allocations; no real Kubernetes runtime, route performance comparison, Forward ACK or capacity claim"
                : "Real Collector HTTP snapshots replayed as CRI through fixed Fluent Bit trusted Pod routing and candidate TLS Kafka outputs to standalone consumer and HTTPS ES; finite preloaded replay, not concurrent request collection, Forward ACK, route performance comparison or capacity claim",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }
}
