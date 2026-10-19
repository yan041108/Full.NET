extern alias kafkabenchmarks;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using LoggingRequestLoadProfile = kafkabenchmarks::Full.NET.Benchmarks.Logging.LoggingRequestLoadProfile;
using LoggingRequestWarmupBarrier = kafkabenchmarks::Full.NET.Benchmarks.Logging.LoggingRequestWarmupBarrier;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>集成测试：KafkaLogRouteComparison。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogRouteComparisonTests
{
    [TestMethod]
    public async Task Isolated_request_processes_compare_collector_and_application_kafka_in_reverse_order()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        // 与真实采集链路使用相同的临时产物边界，不再在源码目录累积压测文件。
        var directory = Directory.CreateTempSubdirectory("fullnet-logging-route-comparison-").FullName;
        var resultPath = Path.Combine(directory, "result.json");
        File.Delete(resultPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var reports = new List<object>();
        foreach (var (route, round, position) in new[] { ("Collector", 1, 1), ("ApplicationKafka", 1, 2),
            ("ApplicationKafka", 2, 1), ("Collector", 2, 2) })
        {
            var caseDirectory = Path.Combine(directory, $"{round}-{position}-{route}");
            Directory.CreateDirectory(caseDirectory);
            File.Delete(Path.Combine(caseDirectory, "request.result.json"));
            reports.Add(await RunCaseAsync(root.FullName, caseDirectory, route, round, position, deadline.Token));
        }
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
        {
            passed = true, cleanupCompleted = true, reports, order = new[] { "Collector", "ApplicationKafka", "ApplicationKafka", "Collector" },
            scope = "Two reverse-order rounds; identical isolated child client/server measurement; parent owns CRI bridge and broker observer; fixed 5000 requests at target500/s, 200warm, projected payload; each case final ES/offset/DLQ reconciled and resources disposed; shared Docker host and synthetic CRI runtime, no production capacity, fault recovery, full business API or automatic route winner",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }

    internal static async Task<object> RunCaseAsync(string root, string directory, string route, int round, int position, CancellationToken token,
        int? capacityRate = null)
    {
        var load = capacityRate is { } rate ? LoggingRequestLoadProfile.Capacity(rate) : LoggingRequestLoadProfile.Comparison;
        var suffix = Guid.NewGuid().ToString("N");
        var general = "fullnet.compare.general." + suffix;
        var priority = "fullnet.compare.priority." + suffix;
        var dlq = "fullnet.compare.dlq." + suffix;
        var group = "fullnet.compare." + suffix;
        var collectorMode = route == "Collector";
        object? proof = null;
        JsonElement measurement = default;
        LoggingRequestCaseProcess? requestProof = null;
        KafkaRequestConsumerProcess? consumerProof = null;
        await using (var kafka = await KafkaLogTlsFixture.StartAsync(cancellationToken: token, enableCollectorListener: collectorMode))
        await using (var es = await KafkaRequestElasticsearchFixture.StartTlsAsync(token, capacityRate == 1000 ? 2 : 1))
        {
            await kafka.EnsureTopicsAsync(token, general, priority, dlq);
            var consumerBatchMaxRecords = capacityRate is not null ? 64 : 8;
            await using var consumerProcess = await KafkaRequestConsumerProcess.StartAsync(kafka, es, general, priority, group, dlq, directory, token, consumerBatchMaxRecords);
            consumerProof = consumerProcess;
            using var reader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl, SslCaLocation = kafka.CaPath,
                GroupId = group, EnableAutoCommit = false, EnableAutoOffsetStore = false, QueuedMaxMessagesKbytes = 65536,
            }).Build();
            reader.Assign([new TopicPartitionOffset(general, 0, Offset.Beginning), new TopicPartitionOffset(priority, 0, Offset.Beginning)]);
            var criDirectory = Path.Combine(directory, "cri");
            await using var collector = collectorMode ? await KafkaRequestCollectorFixture.StartAsync(root, directory, kafka, general, priority, [], token, criDirectory, lowLatency: capacityRate is not null) : null;
            await using var bridge = collectorMode ? await LiveCollectorCriBridge.StartAsync(Path.Combine(directory, "Projected-1.jsonl"), criDirectory, token, load.MaximumBrokerRecords) : null;
            await using var request = LoggingRequestCaseProcess.Start(root, directory, route, kafka, general, priority, capacityRate);
            requestProof = request;
            var records = new List<ConsumeResult<byte[], byte[]>>();
            var warmupIds = new HashSet<string>(StringComparer.Ordinal);
            var warmupOffsets = new Dictionary<TopicPartition, long>();
            var measuredOffsets = new Dictionary<TopicPartition, List<long>>();
            var progress = new List<CommitProgress>();
            var lastProgressUtc = DateTimeOffset.MinValue;
            DateTimeOffset? warmupConfirmedUtc = null;
            TopicPartitionOffset? measuredHttp = null;
            long duringCommit = 0;
            DateTimeOffset? commitConfirmedUtc = null;
            var requestTask = request.CompleteAsync(token);
            while (!requestTask.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(consumerProcess.IsAlive);
                // 有界读取已就绪收据，避免 500 请求/秒的比较被逐条 25ms 观察延迟限制。
                for (var batch = 0; batch < 256 && !requestTask.IsCompleted; batch++)
                {
                    token.ThrowIfCancellationRequested();
                    var record = reader.Consume(batch == 0 ? TimeSpan.FromMilliseconds(20) : TimeSpan.Zero);
                    if (record is null) break;
                    records.Add(record);
                    Assert.IsTrue(records.Count <= load.MaximumBrokerRecords);
                    using var json = JsonDocument.Parse(record.Message.Value);
                    if (json.RootElement.GetProperty("@mt").GetString() == "HttpOperationCompleted"
                        && json.RootElement.GetProperty("RequestId").GetString() is { } requestId
                        && requestId.StartsWith("logging-probe:warmup:", StringComparison.Ordinal))
                    {
                        Assert.IsTrue(warmupIds.Add(requestId));
                        warmupOffsets[record.TopicPartition] = record.Offset.Value + 1;
                    }
                    if (request.MeasurementBegin is not null && json.RootElement.GetProperty("@mt").GetString() == "HttpOperationCompleted"
                        && json.RootElement.GetProperty("RequestId").GetString()!.StartsWith("logging-probe:measured:", StringComparison.Ordinal))
                    {
                        measuredHttp ??= new TopicPartitionOffset(record.TopicPartition, record.Offset.Value + 1);
                        if (!measuredOffsets.TryGetValue(record.TopicPartition, out var offsets))
                            measuredOffsets[record.TopicPartition] = offsets = [];
                        offsets.Add(record.Offset.Value);
                    }
                }
                if (warmupConfirmedUtc is null && warmupIds.Count == load.WarmupRequests)
                {
                    var committed = ReadCommittedIfCoordinatorReady(reader, warmupOffsets.Keys);
                    if (committed is not null && committed.All(offset => offset.Offset.Value >= warmupOffsets[offset.TopicPartition]))
                    {
                        warmupConfirmedUtc = DateTimeOffset.UtcNow;
                        await LoggingRequestWarmupBarrier.ConfirmAsync(directory, token);
                    }
                }
                if (measuredHttp is not null && duringCommit == 0)
                {
                    var committed = ReadCommittedIfCoordinatorReady(reader, [measuredHttp.TopicPartition]);
                    if (committed is not null && committed[0].Offset.Value >= measuredHttp.Offset.Value)
                    {
                        duringCommit = committed[0].Offset.Value;
                        commitConfirmedUtc = DateTimeOffset.UtcNow;
                    }
                }
                if (warmupConfirmedUtc is not null && DateTimeOffset.UtcNow - lastProgressUtc >= TimeSpan.FromMilliseconds(500))
                {
                    lastProgressUtc = DateTimeOffset.UtcNow;
                    var committed = ReadCommittedIfCoordinatorReady(reader, reader.Assignment);
                    if (committed is not null)
                    {
                        var confirmed = 0;
                        foreach (var offset in committed)
                            if (measuredOffsets.TryGetValue(offset.TopicPartition, out var offsets))
                            {
                                var index = offsets.BinarySearch(offset.Offset.Value);
                                confirmed += index >= 0 ? index : ~index;
                            }
                        Assert.IsTrue(progress.Count < 256, "提交进度观察必须有界。");
                        progress.Add(new(DateTimeOffset.UtcNow, confirmed));
                    }
                }
                await Task.Delay(25, token);
            }
            measurement = await requestTask;
            var endUtc = measurement.GetProperty("measurementEndUtc").GetDateTimeOffset();
            Assert.IsNotNull(warmupConfirmedUtc);
            Assert.IsTrue(warmupConfirmedUtc <= measurement.GetProperty("measurementStartUtc").GetDateTimeOffset());
            var endProgress = progress.LastOrDefault(sample => sample.AtUtc < endUtc
                && sample.AtUtc >= measurement.GetProperty("measurementStartUtc").GetDateTimeOffset());
            Assert.IsNotNull(endProgress, "必须取得实际发送窗口内的提交进度。");
            Assert.IsNotNull(measuredHttp);
            Assert.IsTrue(duringCommit >= measuredHttp.Offset.Value && commitConfirmedUtc < endUtc, "实测发送窗口内必须确认具体实测 HTTP 记录。");
            var metrics = measurement.GetProperty("requestReport");
            Assert.AreEqual(route, metrics.GetProperty("deliveryMode").GetString());
            Assert.IsTrue(metrics.GetProperty("receiptVerificationDeferred").GetBoolean());
            Assert.AreEqual(load.Requests, metrics.GetProperty("statistics").GetProperty("SampleCount").GetInt32());
            Assert.AreEqual(0, metrics.GetProperty("unexpected").GetInt32());
            Assert.AreEqual(0L, metrics.GetProperty("preDisposalDroppedMessages").GetInt64());
            var drain = Stopwatch.StartNew();
            if (bridge is not null) await bridge.CompleteAsync();
            var originals = collectorMode ? (await File.ReadAllLinesAsync(Path.Combine(directory, "Projected-1.jsonl"), token))
                .ToDictionary(line => JsonNode.Parse(line)!["LogEventId"]!.GetValue<string>(), line => JsonNode.Parse(line)!) : null;
            if (collector is not null)
            {
                while (!await collector.VerifyInputDrainedAsync(kafka.CollectorMetricsUri, originals!.Count, token)) await Task.Delay(250, token);
                await collector.StopAsync(token);
            }
            var sourceDrainedMilliseconds = drain.Elapsed.TotalMilliseconds;
            var ends = reader.Assignment.ToDictionary(partition => partition, partition => reader.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High.Value);
            var brokerRecords = checked((int)ends.Values.Sum());
            Assert.IsTrue(brokerRecords >= load.Requests + load.WarmupRequests && brokerRecords <= load.MaximumBrokerRecords);
            if (originals is not null) Assert.AreEqual(originals.Count, brokerRecords);
            while (records.Count < brokerRecords)
            {
                token.ThrowIfCancellationRequested();
                var record = reader.Consume(TimeSpan.FromMilliseconds(100));
                if (record is not null) records.Add(record);
            }
            Assert.AreEqual(brokerRecords, records.Count);
            var brokerReceiptsReadMilliseconds = drain.Elapsed.TotalMilliseconds;
            // 先观察真实位点，再做逐条内容校验；校验耗时不得掩盖消费端追赶等待。
            var offsetWait = Stopwatch.StartNew();
            var final = ReadCommittedIfCoordinatorReady(reader, ends.Keys);
            while (final is null || final.Any(offset => offset.Offset.Value != ends[offset.TopicPartition]))
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(consumerProcess.IsAlive);
                await Task.Delay(250, token);
                final = ReadCommittedIfCoordinatorReady(reader, ends.Keys);
            }
            var offsetCatchupWaitMilliseconds = offsetWait.Elapsed.TotalMilliseconds;
            var offsetsConfirmedMilliseconds = drain.Elapsed.TotalMilliseconds;
            var contentStarted = Stopwatch.GetTimestamp();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var requestIds = new HashSet<string>(StringComparer.Ordinal);
            var lines = new List<string>();
            var errors = 0;
            long measuredBytes = 0;
            var measuredRecords = 0;
            foreach (var record in records)
            {
                var line = Encoding.UTF8.GetString(record.Message.Value);
                var json = JsonNode.Parse(line)!.AsObject();
                var key = Encoding.UTF8.GetString(record.Message.Key);
                Assert.IsTrue(ids.Add(key));
                Assert.AreEqual(key, json["LogEventId"]!.GetValue<string>());
                var isPriority = json["reliability.class"]?.GetValue<string>() == "Priority" || json["@l"]?.GetValue<string>() is "Error" or "Fatal";
                Assert.AreEqual(isPriority ? priority : general, record.Topic);
                if (originals is not null) VerifyCollectorSource(originals, key, json);
                if (json["@mt"]!.GetValue<string>() == "HttpOperationCompleted")
                {
                    Assert.IsTrue(requestIds.Add(json["RequestId"]!.GetValue<string>()));
                    var status = json["http.status_code"]!.GetValue<int>();
                    Assert.IsTrue(status is 200 or 500);
                    var parts = json["RequestId"]!.GetValue<string>().Split(':');
                    Assert.IsTrue(parts.Length == 3 && parts[0] == "logging-probe" && parts[1] is "warmup" or "measured");
                    Assert.IsTrue(int.TryParse(parts[2], out var sequence) && sequence >= 0
                        && sequence < (parts[1] == "warmup" ? load.WarmupRequests : load.Requests));
                    Assert.AreEqual(sequence % 10 == 0 ? 500 : 200, status);
                    if (status == 500) errors++;
                    Assert.AreEqual(20, JsonNode.Parse(json["RequestPayload"]!.GetValue<string>())!["pageSize"]!.GetValue<int>());
                    Assert.AreEqual(100, JsonNode.Parse(json["ResponsePayload"]!.GetValue<string>())!["totalCount"]!.GetValue<int>());
                    if (json["RequestId"]!.GetValue<string>().StartsWith("logging-probe:measured:", StringComparison.Ordinal))
                    { measuredBytes += record.Message.Value.Length; measuredRecords++; }
                }
                lines.Add(line);
            }
            Assert.AreEqual(load.Requests + load.WarmupRequests, requestIds.Count);
            Assert.AreEqual(load.Requests, measuredRecords);
            Assert.AreEqual((load.Requests + load.WarmupRequests) / 10, errors);
            var contentVerifiedMilliseconds = drain.Elapsed.TotalMilliseconds;
            var contentValidationMilliseconds = Stopwatch.GetElapsedTime(contentStarted).TotalMilliseconds;
            drain.Stop();
            Assert.AreEqual(0L, reader.QueryWatermarkOffsets(new TopicPartition(dlq, 0), TimeSpan.FromSeconds(10)).High.Value);
            await File.WriteAllLinesAsync(Path.Combine(directory, "broker.jsonl"), lines, token);
            proof = new
            {
                documents = await es.VerifyAsync(lines, token), httpEvents = requestIds.Count, expected500 = errors,
                measuredRecords, measuredBytes, measuredLogBytesPerSecond = measuredBytes * 1000d / metrics.GetProperty("elapsedMilliseconds").GetDouble(),
                drainMilliseconds = drain.Elapsed.TotalMilliseconds, measuredHttpTopic = measuredHttp.Topic,
                sourceDrainedMilliseconds, brokerReceiptsReadMilliseconds, contentVerifiedMilliseconds,
                warmupConfirmedUtc, warmupRecords = warmupIds.Count,
                consumerBatchMaxRecords, collectorTimingProfile = collectorMode ? (capacityRate is not null ? "low-latency" : "default") : null,
                elasticsearchCpuCount = SharedDatabaseFixture.BoundedContainers ? (capacityRate == 1000 ? 2 : 1) : (int?)null,
                offsetCatchupWaitMilliseconds, offsetsConfirmedMilliseconds, contentValidationMilliseconds,
                endWindowCommittedMeasuredRecords = endProgress.MeasuredRecords,
                endWindowSampleAgeMilliseconds = (endUtc - endProgress.AtUtc).TotalMilliseconds, progress,
                measuredHttpOffset = measuredHttp.Offset.Value - 1, duringCommit, commitConfirmedUtc,
                collectorInputRecords = originals?.Count * 2, rejectedMirrors = originals?.Count,
                finalOffsets = final.Select(offset => new { topic = offset.Topic, offset = offset.Offset.Value, brokerEnd = ends[offset.TopicPartition] }).ToArray(),
            };
        }
        Assert.IsTrue(requestProof!.Stopped && consumerProof!.Stopped);
        return new { route, round, position, targetRequestsPerSecond = load.RequestsPerSecond,
            measuredRequests = load.Requests, measurement, proof, cleanupCompleted = true };
    }

    private static List<TopicPartitionOffset>? ReadCommittedIfCoordinatorReady(IConsumer<byte[], byte[]> reader,
        IEnumerable<TopicPartition> partitions)
    {
        try { return reader.Committed(partitions, TimeSpan.FromSeconds(2)); }
        catch (KafkaException error) when (error.Error.Code is ErrorCode.GroupLoadInProgress
            or ErrorCode.GroupCoordinatorNotAvailable or ErrorCode.NotCoordinatorForGroup)
        {
            // 只等待观察查询的协调器就绪；不重发记录、不放宽发送窗口内必须真实提交的断言。
            return null;
        }
    }

    private sealed record CommitProgress(DateTimeOffset AtUtc, int MeasuredRecords);

    private static void VerifyCollectorSource(Dictionary<string, JsonNode> originals, string key, JsonObject json)
    {
        Assert.IsTrue(originals.TryGetValue(key, out var source), "镜像或未知记录不得进入 Broker。");
        var removed = new HashSet<string>(["DiagnosticGroup", "kubernetes", "tenant_id", "user_id"], StringComparer.Ordinal);
        var added = new HashSet<string>(["time", "stream", "_p", "collector.timestamp"], StringComparer.Ordinal);
        foreach (var pair in source!.AsObject())
            if (!removed.Contains(pair.Key)) Assert.IsTrue(JsonNode.DeepEquals(pair.Value, json[pair.Key]), "来源字段变化：" + pair.Key);
        foreach (var pair in json) Assert.IsTrue(!removed.Contains(pair.Key) && (source.AsObject().ContainsKey(pair.Key) || added.Contains(pair.Key)));
        Assert.AreEqual("F", json["_p"]!.GetValue<string>());
    }
}
