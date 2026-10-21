extern alias kafkabenchmarks;
extern alias consumerhost;

using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Confluent.Kafka;
using Full.NET.Logging.Kafka;
using Full.NET.LogConsumer;
using KafkaLogOffsetCommitter = consumerhost::KafkaLogOffsetCommitter;
using Runner = kafkabenchmarks::Full.NET.Benchmarks.Logging.LoggingRequestLatencyRunner;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 Kestrel 请求经正式 TLS Kafka 出口交付，最终从 Broker 读取验收。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogRequestLatencyTests
{
    [TestMethod]
    public Task Projected_http_logs_are_delivered_by_standalone_consumer_over_https()
        => RunAsync(writeElasticsearch: true, independent: true);

    [TestMethod]
    public Task Projected_http_logs_are_confirmed_in_elasticsearch_before_offset_completion()
        => RunAsync(writeElasticsearch: true);

    [TestMethod]
    public Task Sustained_http_logs_and_projections_are_readable_from_tls_broker()
        => RunAsync(writeElasticsearch: false);

    private static async Task RunAsync(bool writeElasticsearch, bool independent = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root, "Kafka 请求验收必须在仓库本地运行。");
        // 测试入口拥有 TEMP 生命周期，成功后回收；不在源码目录累积请求回放产物。
        var directory = Directory.CreateTempSubdirectory(independent ? "fullnet-logging-request-process-"
            : writeElasticsearch ? "fullnet-logging-request-es-" : "fullnet-logging-request-kafka-").FullName;
        var reportPath = Path.Combine(directory, "result.json");
        File.Delete(reportPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var reports = new List<object>();
        object? elasticsearchProof = null;
        KafkaRequestConsumerProcess? child = null;
        await using (var es = independent ? await KafkaRequestElasticsearchFixture.StartTlsAsync(deadline.Token)
            : writeElasticsearch ? await KafkaRequestElasticsearchFixture.StartAsync(deadline.Token) : null)
        await using (var kafka = await KafkaLogTlsFixture.StartAsync(cancellationToken: deadline.Token))
        {
            foreach (var mode in writeElasticsearch ? new[] { "Projected" } : new[] { "Summary", "Projected" })
            {
                var suffix = Guid.NewGuid().ToString("N");
                var general = $"fullnet.request.general.{suffix}";
                var priority = $"fullnet.request.priority.{suffix}";
                var dlq = $"fullnet.request.dlq.{suffix}";
                var group = $"fullnet.request.{suffix}";
                await kafka.EnsureTopicsAsync(deadline.Token, independent ? [general, priority, dlq] : [general, priority]);
                await using var process = independent ? await KafkaRequestConsumerProcess.StartAsync(kafka, es!,
                    general, priority, group, dlq, directory, deadline.Token) : null;
                child = process;
                var configuration = new Dictionary<string, string?>
                {
                    [$"{KafkaLogProducerOptions.SectionName}:BootstrapServers"] = kafka.BootstrapServers,
                    [$"{KafkaLogProducerOptions.SectionName}:GeneralTopic"] = general,
                    [$"{KafkaLogProducerOptions.SectionName}:PriorityTopic"] = priority,
                    [$"{KafkaLogProducerOptions.SectionName}:SecurityProtocol"] = "Ssl",
                    [$"{KafkaLogProducerOptions.SectionName}:SslCaLocation"] = kafka.CaPath,
                    [$"{KafkaLogProducerOptions.SectionName}:MessageTimeoutMs"] = "15000",
                    [$"{KafkaLogProducerOptions.SectionName}:ShutdownFlushTimeoutMs"] = "15000",
                };
                async Task<IReadOnlyList<string>> ReadReceipts(CancellationToken token)
                {
                    using var consumer = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
                    {
                        BootstrapServers = kafka.BootstrapServers, SecurityProtocol = SecurityProtocol.Ssl,
                        SslCaLocation = kafka.CaPath, GroupId = group, EnableAutoCommit = false,
                        EnableAutoOffsetStore = false,
                    }).Build();
                    consumer.Assign([
                        new TopicPartitionOffset(general, new Partition(0), Offset.Beginning),
                        new TopicPartitionOffset(priority, new Partition(0), Offset.Beginning),
                    ]);
                    var ends = consumer.Assignment.ToDictionary(partition => partition,
                        partition => consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(10)).High.Value);
                    var seen = ends.Keys.ToDictionary(partition => partition, _ => 0L);
                    var lines = new List<string>();
                    // 收据对账在请求结束后串行执行，使用协调器既有上限减少 ES/Commit 往返；期限与全量断言保持不变。
                    const int receiptBatchMaxRecords = BatchLogDeliveryProcessor.MaximumRecords;
                    var batch = new List<KafkaLogInput>(receiptBatchMaxRecords);
                    var processor = es is null || independent ? null : new BatchLogDeliveryProcessor(es.Sink, new UnexpectedDlq(),
                        new KafkaLogOffsetCommitter(consumer), new Dictionary<int, int> { [1] = 30 }, 16384);
                    var receiptClock = Stopwatch.StartNew();
                    var stop = DateTime.UtcNow.AddSeconds(30);
                    var completedBatches = 0;
                    var committedRecords = 0;
                    var maximumFlushMilliseconds = 0d;
                    async Task FlushAsync()
                    {
                        if (batch.Count == 0) return;
                        var flushClock = Stopwatch.StartNew();
                        try
                        {
                            var outcome = await processor!.ProcessAsync(batch, () => true, token);
                            Assert.AreEqual(LogDeliveryDisposition.Completed, outcome.Disposition);
                            Assert.AreEqual(batch.Count, outcome.CommittedRecords);
                            completedBatches++;
                            committedRecords += outcome.CommittedRecords;
                            batch.Clear();
                        }
                        catch (Exception error)
                        {
                            // 只记录计数、耗时与取消来源，不泄露日志正文、凭据或目标地址。
                            Console.WriteLine(FormattableString.Invariant($"receipt_failure expected={ends.Values.Sum()} consumed={lines.Count} committed={committedRecords} batches={completedBatches} batch_records={batch.Count} elapsed_ms={receiptClock.Elapsed.TotalMilliseconds:F2} flush_ms={flushClock.Elapsed.TotalMilliseconds:F2} prior_max_flush_ms={maximumFlushMilliseconds:F2} receipt_cancelled={token.IsCancellationRequested} parent_cancelled={deadline.IsCancellationRequested} stop_reached={DateTime.UtcNow >= stop} exception={error.GetType().Name} inner={error.InnerException?.GetType().Name}"));
                            throw;
                        }
                        finally
                        {
                            maximumFlushMilliseconds = Math.Max(maximumFlushMilliseconds, flushClock.Elapsed.TotalMilliseconds);
                        }
                    }
                    while (seen.Any(pair => pair.Value < ends[pair.Key]) && DateTime.UtcNow < stop)
                    {
                        token.ThrowIfCancellationRequested();
                        var record = consumer.Consume(TimeSpan.FromMilliseconds(100));
                        if (record is null) continue;
                        var line = Encoding.UTF8.GetString(record.Message.Value);
                        using var json = JsonDocument.Parse(line);
                        var key = Encoding.UTF8.GetString(record.Message.Key);
                        Assert.AreEqual(key, json.RootElement.GetProperty("LogEventId").GetString());
                        if (json.RootElement.GetProperty("@mt").GetString() == "HttpOperationCompleted")
                        {
                            var isPriority = json.RootElement.GetProperty("reliability.class").GetString() == "Priority";
                            Assert.AreEqual(isPriority ? priority : general, record.Topic);
                        }
                        lines.Add(line);
                        seen[record.TopicPartition] = record.Offset.Value + 1;
                        if (processor is not null)
                        {
                            batch.Add(new KafkaLogInput(record.Topic, record.Partition.Value, record.Offset.Value,
                                key, record.Message.Value, record.Message.Key));
                            if (batch.Count == receiptBatchMaxRecords) await FlushAsync();
                        }
                    }
                    if (processor is not null)
                        Console.WriteLine(FormattableString.Invariant($"receipt_drain expected={ends.Values.Sum()} consumed={lines.Count} committed={committedRecords} batches={completedBatches} elapsed_ms={receiptClock.Elapsed.TotalMilliseconds:F2} max_flush_ms={maximumFlushMilliseconds:F2} receipt_cancelled={token.IsCancellationRequested} parent_cancelled={deadline.IsCancellationRequested} stop_reached={DateTime.UtcNow >= stop}"));
                    Assert.IsTrue(seen.All(pair => pair.Value == ends[pair.Key]), "必须读至排空后的 Broker 高位点。");
                    if (es is not null)
                    {
                        await FlushAsync();
                        if (processor is not null)
                        {
                            Assert.AreEqual(ends.Values.Sum(), (long)committedRecords, "全部收据必须成功写入并提交，不能靠减少输入满足预算。");
                            Console.WriteLine(FormattableString.Invariant($"receipt_complete expected={ends.Values.Sum()} consumed={lines.Count} committed={committedRecords} batches={completedBatches} batch_max_records={receiptBatchMaxRecords} elapsed_ms={receiptClock.Elapsed.TotalMilliseconds:F2} max_flush_ms={maximumFlushMilliseconds:F2}"));
                        }
                        var committed = consumer.Committed(ends.Keys, TimeSpan.FromSeconds(10));
                        while (independent && committed.Any(position => position.Offset.Value != ends[position.TopicPartition]))
                        {
                            token.ThrowIfCancellationRequested();
                            Assert.IsTrue(process!.IsAlive, "独立消费者在排空前退出。");
                            await Task.Delay(250, token);
                            committed = consumer.Committed(ends.Keys, TimeSpan.FromSeconds(10));
                        }
                        Assert.IsTrue(committed.All(position => position.Offset.Value == ends[position.TopicPartition]));
                        if (independent)
                        {
                            Assert.IsTrue(process!.IsAlive);
                            Assert.AreEqual(0L, consumer.QueryWatermarkOffsets(new TopicPartition(dlq, new Partition(0)), TimeSpan.FromSeconds(10)).High.Value);
                        }
                        elasticsearchProof = new
                        {
                            standaloneProcessId = process?.Id,
                            inProcessBatchMaxRecords = processor is null ? (int?)null : receiptBatchMaxRecords,
                            documents = await es.VerifyAsync(lines, token),
                            committedOffsets = committed.Select(position => new
                            {
                                topic = position.Topic, partition = position.Partition.Value, offset = position.Offset.Value,
                                brokerEnd = ends[position.TopicPartition],
                            }).ToArray(),
                        };
                    }
                    return lines;
                }
                reports.Add(await Runner.RunApplicationKafkaCaseAsync(directory, mode, configuration,
                    KafkaLogSnapshotExporter.Create, ReadReceipts, deadline.Token,
                    independent ? TimeSpan.FromSeconds(120) : null));
            }
        }
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            passed = true, cleanupCompleted = true, standaloneProcessStopped = child?.Stopped, reports, elasticsearchProof,
            scope = independent
                ? "Real HTTP to TLS Kafka to concurrent standalone production consumer to HTTPS ES with private CA and restricted API Key; full document and final offset reconciliation; forced cleanup after drain, no graceful shutdown, Collector comparison or capacity claim"
                : writeElasticsearch
                ? "Real HTTP to TLS Kafka to in-process production batch coordinator and real local HTTP ES; all documents and final offsets reconciled; no standalone consumer, ES TLS, Collector comparison or capacity claim"
                : "Local TLS Kafka and loopback Kestrel; 5000 requests at target 500/s per mode; no Collector comparison, ES or full business API capacity claim",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }

    private sealed class UnexpectedDlq : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason, CancellationToken cancellationToken)
            => throw new AssertFailedException($"真实请求不应进入隔离：{reason}");
    }
}
