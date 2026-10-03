using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;

namespace Full.NET.IntegrationTests.Messaging;

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
        var directory = Path.Combine(root.FullName, "artifacts/logging-route-comparison");
        Directory.CreateDirectory(directory);
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

    private static async Task<object> RunCaseAsync(string root, string directory, string route, int round, int position, CancellationToken token)
    {
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
        await using (var es = await KafkaRequestElasticsearchFixture.StartTlsAsync(token))
        {
            await kafka.EnsureTopicsAsync(token, general, priority, dlq);
            await using var consumerProcess = await KafkaRequestConsumerProcess.StartAsync(kafka, es, general, priority, group, dlq, directory, token);
            consumerProof = consumerProcess;
            using var reader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl, SslCaLocation = kafka.CaPath,
                GroupId = group, EnableAutoCommit = false, EnableAutoOffsetStore = false, QueuedMaxMessagesKbytes = 65536,
            }).Build();
            reader.Assign([new TopicPartitionOffset(general, 0, Offset.Beginning), new TopicPartitionOffset(priority, 0, Offset.Beginning)]);
            var criDirectory = Path.Combine(directory, "cri");
            await using var collector = collectorMode ? await KafkaRequestCollectorFixture.StartAsync(root, directory, kafka, general, priority, [], token, criDirectory) : null;
            await using var bridge = collectorMode ? await LiveCollectorCriBridge.StartAsync(Path.Combine(directory, "Projected-1.jsonl"), criDirectory, token) : null;
            await using var request = LoggingRequestCaseProcess.Start(root, directory, route, kafka, general, priority);
            requestProof = request;
            var records = new List<ConsumeResult<byte[], byte[]>>();
            TopicPartitionOffset? measuredHttp = null;
            long duringCommit = 0;
            DateTimeOffset? commitConfirmedUtc = null;
            var requestTask = request.CompleteAsync(token);
            while (!requestTask.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(consumerProcess.IsAlive);
                var record = reader.Consume(TimeSpan.FromMilliseconds(20));
                if (record is not null)
                {
                    records.Add(record);
                    Assert.IsTrue(records.Count <= 10000);
                    using var json = JsonDocument.Parse(record.Message.Value);
                    if (request.MeasurementBegin is not null && json.RootElement.GetProperty("@mt").GetString() == "HttpOperationCompleted"
                        && json.RootElement.GetProperty("RequestId").GetString()!.StartsWith("logging-probe:measured:", StringComparison.Ordinal))
                        measuredHttp ??= new TopicPartitionOffset(record.TopicPartition, record.Offset.Value + 1);
                }
                if (measuredHttp is not null && duringCommit == 0)
                {
                    var committed = reader.Committed([measuredHttp.TopicPartition], TimeSpan.FromSeconds(2))[0];
                    if (committed.Offset.Value >= measuredHttp.Offset.Value)
                    {
                        duringCommit = committed.Offset.Value;
                        commitConfirmedUtc = DateTimeOffset.UtcNow;
                    }
                }
                await Task.Delay(25, token);
            }
            measurement = await requestTask;
            var endUtc = measurement.GetProperty("measurementEndUtc").GetDateTimeOffset();
            Assert.IsNotNull(measuredHttp);
            Assert.IsTrue(duringCommit >= measuredHttp.Offset.Value && commitConfirmedUtc < endUtc, "实测发送窗口内必须确认具体实测 HTTP 记录。");
            var metrics = measurement.GetProperty("requestReport");
            Assert.AreEqual(route, metrics.GetProperty("deliveryMode").GetString());
            Assert.IsTrue(metrics.GetProperty("receiptVerificationDeferred").GetBoolean());
            Assert.AreEqual(5000, metrics.GetProperty("statistics").GetProperty("SampleCount").GetInt32());
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
            var ends = reader.Assignment.ToDictionary(partition => partition, partition => reader.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High.Value);
            var brokerRecords = checked((int)ends.Values.Sum());
            Assert.IsTrue(brokerRecords is >= 5200 and <= 10000);
            if (originals is not null) Assert.AreEqual(originals.Count, brokerRecords);
            while (records.Count < brokerRecords)
            {
                token.ThrowIfCancellationRequested();
                var record = reader.Consume(TimeSpan.FromMilliseconds(100));
                if (record is not null) records.Add(record);
            }
            Assert.AreEqual(brokerRecords, records.Count);
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
                    if (status == 500) errors++;
                    Assert.AreEqual(20, JsonNode.Parse(json["RequestPayload"]!.GetValue<string>())!["pageSize"]!.GetValue<int>());
                    Assert.AreEqual(100, JsonNode.Parse(json["ResponsePayload"]!.GetValue<string>())!["totalCount"]!.GetValue<int>());
                    if (json["RequestId"]!.GetValue<string>().StartsWith("logging-probe:measured:", StringComparison.Ordinal))
                    { measuredBytes += record.Message.Value.Length; measuredRecords++; }
                }
                lines.Add(line);
            }
            Assert.AreEqual(5200, requestIds.Count);
            Assert.AreEqual(5000, measuredRecords);
            Assert.AreEqual(520, errors);
            var final = reader.Committed(ends.Keys, TimeSpan.FromSeconds(10));
            while (final.Any(offset => offset.Offset.Value != ends[offset.TopicPartition]))
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(consumerProcess.IsAlive);
                await Task.Delay(250, token);
                final = reader.Committed(ends.Keys, TimeSpan.FromSeconds(10));
            }
            drain.Stop();
            Assert.AreEqual(0L, reader.QueryWatermarkOffsets(new TopicPartition(dlq, 0), TimeSpan.FromSeconds(10)).High.Value);
            await File.WriteAllLinesAsync(Path.Combine(directory, "broker.jsonl"), lines, token);
            proof = new
            {
                documents = await es.VerifyAsync(lines, token), httpEvents = requestIds.Count, expected500 = errors,
                measuredRecords, measuredBytes, measuredLogBytesPerSecond = measuredBytes * 1000d / metrics.GetProperty("elapsedMilliseconds").GetDouble(),
                drainMilliseconds = drain.Elapsed.TotalMilliseconds, measuredHttpTopic = measuredHttp.Topic,
                measuredHttpOffset = measuredHttp.Offset.Value - 1, duringCommit, commitConfirmedUtc,
                collectorInputRecords = originals?.Count * 2, rejectedMirrors = originals?.Count,
                finalOffsets = final.Select(offset => new { topic = offset.Topic, offset = offset.Offset.Value, brokerEnd = ends[offset.TopicPartition] }).ToArray(),
            };
        }
        Assert.IsTrue(requestProof!.Stopped && consumerProof!.Stopped);
        return new { route, round, position, measurement, proof, cleanupCompleted = true };
    }

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
