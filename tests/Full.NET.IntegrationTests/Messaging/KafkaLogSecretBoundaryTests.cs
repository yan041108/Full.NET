using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using Full.NET.Hosting.Observability;
using Full.NET.Logging.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>用少量带原始秘密的诊断日志验证两个入口的实际字节，不保存原始秘密证据。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogSecretBoundaryTests
{
    [TestMethod]
    [DataRow("Collector")]
    [DataRow("ApplicationKafka")]
    public async Task Secrets_and_restricted_details_are_absent_from_real_delivery(string route)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var directory = Path.Combine(root.FullName, "artifacts/logging-secret-boundary", route);
        Directory.CreateDirectory(directory);
        var resultPath = Path.Combine(directory, "result.json");
        File.Delete(resultPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = deadline.Token;
        var collectorMode = route == "Collector";
        var suffix = Guid.NewGuid().ToString("N");
        // 哨兵仅存在于内存，失败消息只包含编号，不能把原秘密复制到诊断输出。
        var secrets = Enumerable.Range(0, 10).Select(_ => "private-" + Guid.NewGuid().ToString("N")).ToArray();
        var forbidden = secrets.Concat(["192.0.2.45", "10.77.0.12:8443"]).ToArray();
        foreach (var value in forbidden)
            Assert.ThrowsExactly<AssertFailedException>(() => VerifyNoSecrets(value, forbidden));
        object? documentProof = null;
        object? offsets = null;
        KafkaRequestConsumerProcess? processProof = null;
        await using (var kafka = await KafkaLogTlsFixture.StartAsync(cancellationToken: token, enableCollectorListener: collectorMode))
        await using (var es = await KafkaRequestElasticsearchFixture.StartTlsAsync(token))
        {
            var general = "fullnet.secrets.general." + suffix;
            var priority = "fullnet.secrets.priority." + suffix;
            var dlq = "fullnet.secrets.dlq." + suffix;
            var group = "fullnet.secrets." + suffix;
            await kafka.EnsureTopicsAsync(token, general, priority, dlq);
            await using var process = await KafkaRequestConsumerProcess.StartAsync(kafka, es, general, priority, group, dlq, directory, token);
            processProof = process;
            var source = GenerateSource(route, kafka, general, priority, secrets);
            Assert.AreEqual(3, source.Count);
            foreach (var line in source) VerifyNoSecrets(line, forbidden);
            await File.WriteAllLinesAsync(Path.Combine(directory, "source.jsonl"), source, token);
            var originals = source.ToDictionary(line => JsonNode.Parse(line)!["LogEventId"]!.GetValue<string>(), line => JsonNode.Parse(line)!);
            Assert.AreEqual(3, originals.Count);
            Assert.IsTrue(source.Any(line => JsonNode.Parse(line)!["LogMarker"]?.GetValue<string>() == "safe-general"));
            Assert.IsTrue(source.Any(line => JsonNode.Parse(line)!["LogMarker"]?.GetValue<string>() == "safe-priority"));
            Assert.IsTrue(source.Any(line => JsonNode.Parse(line)!["ExceptionType"]?.GetValue<string>() == nameof(InvalidOperationException)));
            await using var collector = collectorMode
                ? await KafkaRequestCollectorFixture.StartAsync(root.FullName, directory, kafka, general, priority, source, token) : null;
            using var reader = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl, SslCaLocation = kafka.CaPath,
                GroupId = group, EnableAutoCommit = false, EnableAutoOffsetStore = false,
            }).Build();
            reader.Assign([new TopicPartitionOffset(general, 0, Offset.Beginning), new TopicPartitionOffset(priority, 0, Offset.Beginning)]);
            if (collector is not null)
            {
                while (!await collector.VerifyInputDrainedAsync(kafka.CollectorMetricsUri, source.Count, token))
                    await Task.Delay(100, token);
                await collector.StopAsync(token);
            }
            var ends = reader.Assignment.ToDictionary(partition => partition, partition => reader.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(5)).High.Value);
            Assert.AreEqual(2L, ends[new TopicPartition(general, 0)]);
            Assert.AreEqual(1L, ends[new TopicPartition(priority, 0)]);
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (lines.Count < 3)
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(process.IsAlive);
                var record = reader.Consume(TimeSpan.FromMilliseconds(100));
                if (record is null) continue;
                var line = Encoding.UTF8.GetString(record.Message.Value);
                VerifyNoSecrets(line, forbidden);
                var json = JsonNode.Parse(line)!.AsObject();
                Assert.IsTrue(seen.Add(record.Message.Key) && originals.ContainsKey(record.Message.Key));
                Assert.AreEqual(record.Message.Key, json["LogEventId"]!.GetValue<string>());
                Assert.AreEqual(json["@l"]?.GetValue<string>() == "Error" ? priority : general, record.Topic);
                VerifySource(originals[record.Message.Key], json, collectorMode);
                lines.Add(line);
            }
            var committed = reader.Committed(ends.Keys, TimeSpan.FromSeconds(5));
            while (committed.Any(offset => offset.Offset.Value != ends[offset.TopicPartition]))
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(process.IsAlive);
                await Task.Delay(100, token);
                committed = reader.Committed(ends.Keys, TimeSpan.FromSeconds(5));
            }
            Assert.AreEqual(0L, reader.QueryWatermarkOffsets(new TopicPartition(dlq, 0), TimeSpan.FromSeconds(5)).High.Value);
            // ES 逐项读取并 DeepEquals 完整来源，因此其实际 _source 也必须满足已验证字节边界。
            documentProof = await es.VerifyAsync(lines, token);
            offsets = committed.Select(offset => new { topic = offset.Topic, offset = offset.Offset.Value, brokerEnd = ends[offset.TopicPartition] }).ToArray();
            await File.WriteAllLinesAsync(Path.Combine(directory, "broker.jsonl"), lines, token);
        }
        Assert.IsTrue(processProof!.Stopped);
        foreach (var name in collectorMode ? new[] { "consumer.stdout.log", "consumer.stderr.log", "collector.log" }
                     : new[] { "consumer.stdout.log", "consumer.stderr.log" })
        {
            var path = Path.Combine(directory, name);
            Assert.IsTrue(File.Exists(path), "应有的进程诊断必须存在。");
            VerifyNoSecrets(await File.ReadAllTextAsync(path, token), forbidden);
        }
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
        {
            passed = true, route, cleanupCompleted = true, sourceRecords = 3, forbiddenValueCount = forbidden.Length,
            consoleRecords = collectorMode ? 3 : 0, capturedProducerSnapshots = collectorMode ? 0 : 3,
            collectorInputRecords = collectorMode ? 6 : 0, rejectedMirrors = collectorMode ? 3 : 0,
            documents = documentProof, offsets, dlqRecords = 0,
            scope = "Three diagnostic events; raw secret sentinels, restricted bodies/addresses, nested credentials, opaque Authorization value and exception message; real source/Broker/ES equality and consumer diagnostics; no B1 database, legacy ES Sink, archive, all possible secrets or capacity claim",
        }, new JsonSerializerOptions { WriteIndented = true }), token);
    }

    private static IReadOnlyList<string> GenerateSource(string route, KafkaLogTlsFixture kafka,
        string general, string priority, string[] secrets)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [LoggingOptions.SectionName + ":DeliveryMode"] = route,
            [LoggingOptions.SectionName + ":ExpectedDeliveryMode"] = route,
            [LoggingOptions.SectionName + ":IndexRouteVersion"] = "1",
            [LoggingOptions.SectionName + ":IndexRetentionDays"] = "30",
            [KafkaLogProducerOptions.SectionName + ":BootstrapServers"] = kafka.BootstrapServers,
            [KafkaLogProducerOptions.SectionName + ":GeneralTopic"] = general,
            [KafkaLogProducerOptions.SectionName + ":PriorityTopic"] = priority,
            [KafkaLogProducerOptions.SectionName + ":SecurityProtocol"] = "Ssl",
            [KafkaLogProducerOptions.SectionName + ":SslCaLocation"] = kafka.CaPath,
            [KafkaLogProducerOptions.SectionName + ":MessageTimeoutMs"] = "15000",
            [KafkaLogProducerOptions.SectionName + ":ShutdownFlushTimeoutMs"] = "15000",
        });
        var captured = new List<string>();
        builder.AddFullNetServiceDefaults(route == "ApplicationKafka"
            ? configuration => new RecordingExporter(KafkaLogSnapshotExporter.Create(configuration), captured) : null);
        var console = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            using (var host = builder.Build())
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LogSecurityBoundary");
                using var scope = logger.BeginScope(new Dictionary<string, object>
                {
                    ["Password"] = secrets[0], ["Token"] = secrets[1], ["Cookie"] = secrets[2],
                    ["RequestBody"] = secrets[3], ["ResponseBody"] = secrets[4],
                    ["ClientIp"] = "192.0.2.45", ["ServerAddress"] = "10.77.0.12:8443",
                });
                logger.LogInformation("{LogMarker} {@Metadata} {Note}", "safe-general",
                    new { ClientSecret = secrets[5], Nested = new { ResponseBody = secrets[6], SafeValue = "safe-nested" } },
                    "Bearer " + secrets[7]);
                logger.LogError(new InvalidOperationException(secrets[8]), "{LogMarker}", "safe-priority");
                logger.LogInformation("Authorization: {Value}", secrets[9]);
            }
        }
        finally { Console.SetOut(console); }
        if (route == "ApplicationKafka")
        {
            Assert.AreEqual("", output.ToString(), "直发入口不得同时输出可被 Collector 采集的 Console 镜像。");
            return captured;
        }
        return output.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static void VerifyNoSecrets(string value, IReadOnlyList<string> forbidden)
    {
        Assert.IsTrue(forbidden.Count > 0);
        for (var index = 0; index < forbidden.Count; index++)
            Assert.IsFalse(value.Contains(forbidden[index], StringComparison.Ordinal), "秘密哨兵泄漏，编号=" + index);
    }

    private static void VerifySource(JsonNode source, JsonObject received, bool collector)
    {
        if (!collector) { Assert.IsTrue(JsonNode.DeepEquals(source, received)); return; }
        var removed = new HashSet<string>(["DiagnosticGroup", "kubernetes", "tenant_id", "user_id"], StringComparer.Ordinal);
        var added = new HashSet<string>(["time", "stream", "_p", "collector.timestamp"], StringComparer.Ordinal);
        foreach (var pair in source.AsObject())
            if (!removed.Contains(pair.Key)) Assert.IsTrue(JsonNode.DeepEquals(pair.Value, received[pair.Key]));
        foreach (var pair in received)
            Assert.IsTrue(!removed.Contains(pair.Key) && (source.AsObject().ContainsKey(pair.Key) || added.Contains(pair.Key)));
        Assert.AreEqual("F", received["_p"]!.GetValue<string>());
    }

    private sealed class RecordingExporter(KafkaLogSnapshotExporter exporter, List<string> captured) : IHostLogSnapshotExporter
    {
        public void Emit(HostLogSnapshot snapshot)
        {
            // 测试只保留三个 owned 快照；在宿主复用缓冲前复制，正式 Producer 仍实际发送。
            lock (captured)
            {
                Assert.IsTrue(captured.Count < 3);
                captured.Add(Encoding.UTF8.GetString(snapshot.Utf8Json.Span));
            }
            exporter.Emit(snapshot);
        }
        public void Dispose() => exporter.Dispose();
    }
}
