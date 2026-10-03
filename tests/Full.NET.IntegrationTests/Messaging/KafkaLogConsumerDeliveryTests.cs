extern alias consumerhost;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Confluent.Kafka;
using Full.NET.LogConsumer;
using KafkaLogDeadLetterSink = consumerhost::KafkaLogDeadLetterSink;
using KafkaLogKeyDecoder = consumerhost::KafkaLogKeyDecoder;
using KafkaLogOffsetCommitter = consumerhost::KafkaLogOffsetCommitter;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>真实 TLS Broker 上验证最终确认、受限隔离与连续位点提交。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogConsumerDeliveryTests
{
    [TestMethod]
    public async Task IndependentConsumerReadinessRejectsUnassignedBroker()
    {
        var port = FreeHealthPort();
        var start = ConsumerStartInfo(port);
        start.Environment["FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS"] = "127.0.0.1:1";
        using var process = Process.Start(start);
        Assert.IsNotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(2) };
            await WaitForHealthAsync(http, "/health/live", HttpStatusCode.OK);
            using var ready = await http.GetAsync("/health/ready");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode,
                "没有实际取得 Kafka 分区时不得就绪。");
            var metrics = await http.GetStringAsync("/metrics");
            StringAssert.Contains(metrics, "fullnet_log_consumer_assigned_partitions 0");
            StringAssert.Contains(metrics, "fullnet_log_consumer_committed_records_total 0");
            if (OperatingSystem.IsLinux())
            {
                await SendTerminationAsync(process);
                Assert.AreEqual(0, process.ExitCode, "空闲消费者应正常响应 SIGTERM。");
            }
        }
        finally
        {
            await StopProcessAsync(process, output, errors);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InFlightDeliveryCannotCommitAfterGroupRebalance(bool batchMode)
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var source = "fullnet.integration.logging.inflight-rebalance." + suffix;
        var group = "fullnet.integration.logging.inflight-group." + suffix;
        await kafka.EnsureTopicsAsync(source);
        var id = Guid.CreateVersion7().ToString("D");
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiry = occurred.AddDays(30);
        var json = $$"""
            {"@t":"{{occurred:O}}","@mt":"inflight-rebalance","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
            """;
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        await producer.ProduceAsync(source, new Message<string, byte[]>
        {
            Key = id,
            Value = Encoding.UTF8.GetBytes(json),
        });
        ConsumerConfig Config(int maxPollMs) => new()
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SessionTimeoutMs = 6000,
            HeartbeatIntervalMs = 1000,
            MaxPollIntervalMs = maxPollMs,
        };
        using var previous = new ConsumerBuilder<byte[], byte[]>(Config(9000)).Build();
        previous.Subscribe(source);
        var first = previous.Consume(TimeSpan.FromSeconds(20));
        Assert.IsNotNull(first);
        Assert.AreEqual(0L, first.Offset.Value);
        var blockingSink = new BlockingSink();
        var routes = new Dictionary<int, int> { [2] = 30 };
        KafkaLogInput Input(ConsumeResult<byte[], byte[]> result) =>
            new(result.Topic, result.Partition.Value, result.Offset.Value,
                KafkaLogKeyDecoder.Decode(result.Message.Key), result.Message.Value,
                result.Message.Key);
        var stale = new SequentialLogDeliveryProcessor(blockingSink, new UnexpectedDlq(),
            new KafkaLogOffsetCommitter(previous), routes, 1024);
        var batch = new BatchLogDeliveryProcessor(blockingSink, new UnexpectedDlq(),
            new KafkaLogOffsetCommitter(previous), routes, 1024);
        // 故意不 Poll 更新 epoch，验证 SDK/Broker 对失去成员资格的迟到提交仍会拒绝。
        Task inFlight = batchMode ? batch.ProcessAsync([Input(first)], () => true) : stale.ProcessAsync(Input(first));
        await blockingSink.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var replacement = new ConsumerBuilder<byte[], byte[]>(Config(300000)).Build();
            replacement.Subscribe(source);
            var replay = replacement.Consume(TimeSpan.FromSeconds(30));
            Assert.IsNotNull(replay, "旧成员超过 MaxPoll 后，新成员应取得分区。");
            Assert.AreEqual(0L, replay.Offset.Value);
            blockingSink.Complete();
            await Assert.ThrowsAsync<KafkaException>(() => inFlight);
            Assert.IsTrue(replacement.Committed(
                [new TopicPartition(source, new Partition(0))],
                TimeSpan.FromSeconds(5))[0].Offset.Value < 0,
                "被撤销分区的迟到完成不得推进 Offset。");
            var current = new SequentialLogDeliveryProcessor(new ConfirmingSink([]),
                new UnexpectedDlq(), new KafkaLogOffsetCommitter(replacement), routes, 1024);
            Assert.AreEqual(LogDeliveryDisposition.Completed,
                await current.ProcessAsync(Input(replay)));
            Assert.AreEqual(1L, replacement.Committed(
                [new TopicPartition(source, new Partition(0))],
                TimeSpan.FromSeconds(5))[0].Offset.Value);
            replacement.Close();
        }
        finally
        {
            blockingSink.Complete();
            previous.Close();
        }
    }

    [TestMethod]
    public async Task DepartedGroupMemberCannotCommitAndReplacementReplays()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var source = "fullnet.integration.logging.rebalance." + suffix;
        var group = "fullnet.integration.logging.rebalance-group." + suffix;
        await kafka.EnsureTopicsAsync(source);
        var id = Guid.CreateVersion7().ToString("D");
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiry = occurred.AddDays(30);
        var json = $$"""
            {"@t":"{{occurred:O}}","@mt":"rebalance","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
            """;
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        await producer.ProduceAsync(source, new Message<string, byte[]>
        {
            Key = id,
            Value = Encoding.UTF8.GetBytes(json),
        });
        ConsumerConfig Config() => new()
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
        using var previous = new ConsumerBuilder<byte[], byte[]>(Config()).Build();
        previous.Subscribe(source);
        var first = previous.Consume(TimeSpan.FromSeconds(20));
        Assert.IsNotNull(first);
        Assert.AreEqual(0L, first.Offset.Value);
        using var replacement = new ConsumerBuilder<byte[], byte[]>(Config()).Build();
        replacement.Subscribe(source);
        previous.Close();
        var replay = replacement.Consume(TimeSpan.FromSeconds(20));
        Assert.IsNotNull(replay);
        Assert.AreEqual(0L, replay.Offset.Value,
            "分区撤销后新 Group 成员应取得未提交的原记录。");
        var routes = new Dictionary<int, int> { [2] = 30 };
        KafkaLogInput Input(ConsumeResult<byte[], byte[]> result) =>
            new(result.Topic, result.Partition.Value, result.Offset.Value,
                KafkaLogKeyDecoder.Decode(result.Message.Key), result.Message.Value,
                result.Message.Key);
        var stale = new SequentialLogDeliveryProcessor(new ConfirmingSink([]), new UnexpectedDlq(),
            new KafkaLogOffsetCommitter(previous), routes, 1024);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => stale.ProcessAsync(Input(first)));
        Assert.IsTrue(replacement.Committed(
            [new TopicPartition(source, new Partition(0))],
            TimeSpan.FromSeconds(5))[0].Offset.Value < 0,
            "旧 Group 成员的迟到完成不得提交来源位点。");
        var current = new SequentialLogDeliveryProcessor(new ConfirmingSink([]), new UnexpectedDlq(),
            new KafkaLogOffsetCommitter(replacement), routes, 1024);
        Assert.AreEqual(LogDeliveryDisposition.Completed,
            await current.ProcessAsync(Input(replay)));
        Assert.AreEqual(1L, replacement.Committed(
            [new TopicPartition(source, new Partition(0))],
            TimeSpan.FromSeconds(5))[0].Offset.Value);
        replacement.Close();
    }

    [TestMethod]
    public async Task IndependentProcessRetainsOffsetOnDlqFailureThenReplaysAfterRestart()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var source = "fullnet.integration.logging.process." + suffix;
        var dlqTopic = "fullnet.integration.logging.process-dlq." + suffix;
        var group = "fullnet.integration.logging.process-group." + suffix;
        await kafka.EnsureTopicsAsync(source, dlqTopic);
        using var sourceProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        await sourceProducer.ProduceAsync(source,
            new Message<string, byte[]> { Key = "invalid", Value = Encoding.UTF8.GetBytes("not-json") });

        var hostDll = Path.Combine(AppContext.BaseDirectory, "Full.NET.Host.LogConsumer.dll");
        Assert.IsTrue(File.Exists(hostDll), "独立消费者程序集必须随测试构建输出。");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(hostDll);
        var healthPort = FreeHealthPort();
        start.Environment["FULLNET_LOG_CONSUMER_HEALTH_PORT"] = healthPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["FULLNET_LOG_CONSUMER_EXPERIMENTAL"] = "true";
        start.Environment["FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS"] = kafka.BootstrapServers;
        start.Environment["FULLNET_LOG_CONSUMER_TOPICS"] = source;
        start.Environment["FULLNET_LOG_CONSUMER_GROUP_ID"] = group;
        start.Environment["FULLNET_LOG_CONSUMER_DLQ_TOPIC"] = dlqTopic;
        start.Environment["FULLNET_LOG_CONSUMER_ROUTES"] = "2:30";
        start.Environment["FULLNET_LOG_CONSUMER_MAX_EVENT_BYTES"] = "1024";
        start.Environment["FULLNET_LOG_CONSUMER_ES_URL"] = "https://127.0.0.1:1/";
        start.Environment["FULLNET_LOG_CONSUMER_ES_API_KEY"] = "test-only-unused";
        start.Environment["FULLNET_LOG_CONSUMER_SECURITY_PROTOCOL"] = "Ssl";
        start.Environment["FULLNET_LOG_CONSUMER_SSL_CA_LOCATION"] = kafka.CaPath;
        start.Environment.Remove("FULLNET_LOG_CONSUMER_ES_CA_PATH");
        start.Environment["FULLNET_LOG_CONSUMER_DLQ_TOPIC"] = dlqTopic + ".missing";
        using (var failedProcess = Process.Start(start))
        {
            Task<string>? failedOutput = null;
            Task<string>? failedErrors = null;
            try
            {
                Assert.IsNotNull(failedProcess);
                failedOutput = failedProcess.StandardOutput.ReadToEndAsync();
                failedErrors = failedProcess.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await failedProcess.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(1, failedProcess.ExitCode,
                    "DLQ Topic 不存在时进程应报告投递异常。");
                StringAssert.Contains(await failedOutput, "Log consumer started");
                StringAssert.Contains(await failedErrors, "Log consumer stopped: ProduceException");
            }
            finally
            {
                await StopProcessAsync(failedProcess, failedOutput, failedErrors);
            }
        }
        using (var beforeRestart = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
               {
                   BootstrapServers = kafka.BootstrapServers,
                   SecurityProtocol = SecurityProtocol.Ssl,
                   SslCaLocation = kafka.CaPath,
                   GroupId = group,
                   EnableAutoCommit = false,
               }).Build())
        {
            var offset = beforeRestart.Committed(
                [new TopicPartition(source, new Partition(0))],
                TimeSpan.FromSeconds(5))[0].Offset;
            Assert.IsTrue(offset.Value < 0, "DLQ 投递失败时不得提交来源 Offset。");
        }

        start.Environment["FULLNET_LOG_CONSUMER_DLQ_TOPIC"] = dlqTopic;
        using var process = Process.Start(start);
        Task<string>? output = null;
        Task<string>? errors = null;
        try
        {
            Assert.IsNotNull(process);
            output = process.StandardOutput.ReadToEndAsync();
            errors = process.StandardError.ReadToEndAsync();
            using var dlqReader = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                SecurityProtocol = SecurityProtocol.Ssl,
                SslCaLocation = kafka.CaPath,
                GroupId = group + ".dlq",
                AutoOffsetReset = AutoOffsetReset.Earliest,
            }).Build();
            dlqReader.Subscribe(dlqTopic);
            var isolated = dlqReader.Consume(TimeSpan.FromSeconds(30));
            Assert.IsNotNull(isolated, "独立进程未把无效记录写入 TLS Kafka DLQ。");
            Assert.AreEqual(source + ":0:0", isolated.Message.Key);
            Assert.AreEqual("not-json", Encoding.UTF8.GetString(isolated.Message.Value));
            Assert.AreEqual("InvalidRecord", Encoding.UTF8.GetString(
                isolated.Message.Headers.GetLastBytes("isolation-reason")));
            dlqReader.Close();

            using var offsetReader = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                SecurityProtocol = SecurityProtocol.Ssl,
                SslCaLocation = kafka.CaPath,
                GroupId = group,
                EnableAutoCommit = false,
            }).Build();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            var committed = Offset.Unset;
            while (DateTimeOffset.UtcNow < deadline)
            {
                committed = offsetReader.Committed(
                    [new TopicPartition(source, new Partition(0))],
                    TimeSpan.FromSeconds(5))[0].Offset;
                if (committed.Value == 1) break;
                await Task.Delay(250);
            }
            Assert.AreEqual(1L, committed.Value,
                "独立进程应在 DLQ Broker 确认后提交下一 Offset。");
            using var health = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{healthPort}"),
                Timeout = TimeSpan.FromSeconds(2) };
            await WaitForHealthAsync(health, "/health/ready", HttpStatusCode.OK);
            var metrics = await health.GetStringAsync("/metrics");
            StringAssert.Contains(metrics, "fullnet_log_consumer_committed_records_total 1");
            Assert.IsFalse(metrics.Contains(source, StringComparison.Ordinal));
            Assert.IsFalse(metrics.Contains("not-json", StringComparison.Ordinal));
            if (OperatingSystem.IsLinux())
            {
                await SendTerminationAsync(process);
                Assert.AreEqual(0, process.ExitCode, "提交后消费者应正常响应 SIGTERM。");
            }
        }
        finally
        {
            await StopProcessAsync(process, output, errors);
        }
    }

    private static async Task StopProcessAsync(Process? process,
        Task<string>? output, Task<string>? errors)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 子进程可能恰在检查退出状态后结束；仍须回收流与进程句柄。
        }
        await process.WaitForExitAsync();
        if (output is not null) _ = await output;
        if (errors is not null) _ = await errors;
    }

    private static int FreeHealthPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static ProcessStartInfo ConsumerStartInfo(int port)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Full.NET.Host.LogConsumer.dll"));
        start.Environment["FULLNET_LOG_CONSUMER_EXPERIMENTAL"] = "true";
        start.Environment["FULLNET_LOG_CONSUMER_TOPICS"] = "fullnet.test.general";
        start.Environment["FULLNET_LOG_CONSUMER_GROUP_ID"] = "fullnet.test.unassigned";
        start.Environment["FULLNET_LOG_CONSUMER_DLQ_TOPIC"] = "fullnet.test.dlq";
        start.Environment["FULLNET_LOG_CONSUMER_ROUTES"] = "2:30";
        start.Environment["FULLNET_LOG_CONSUMER_MAX_EVENT_BYTES"] = "1024";
        start.Environment["FULLNET_LOG_CONSUMER_ES_URL"] = "https://127.0.0.1:1/";
        start.Environment["FULLNET_LOG_CONSUMER_ES_API_KEY"] = "test-only-unused";
        start.Environment["FULLNET_LOG_CONSUMER_SECURITY_PROTOCOL"] = "Ssl";
        start.Environment["FULLNET_LOG_CONSUMER_HEALTH_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var name in new[] { "ES_CA_PATH", "ES_REVOCATION_MODE", "SSL_CA_LOCATION" })
            start.Environment.Remove("FULLNET_LOG_CONSUMER_" + name);
        return start;
    }

    private static async Task WaitForHealthAsync(HttpClient http, string path, HttpStatusCode expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(path);
                if (response.StatusCode == expected) return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                // 宿主未监听时允许启动等待；截止后由断言报告实际健康契约失败。
            }
            await Task.Delay(100);
        }
        Assert.Fail($"消费者 {path} 未返回预期状态 {(int)expected}。");
    }

    private static async Task SendTerminationAsync(Process process)
    {
        using var signal = Process.Start(new ProcessStartInfo("kill", $"-TERM {process.Id}")
            { UseShellExecute = false });
        Assert.IsNotNull(signal);
        await signal.WaitForExitAsync();
        Assert.AreEqual(0, signal.ExitCode);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
    }

    [TestMethod]
    public async Task EsConfirmationThenDlqAckAdvanceOffsetsInOrder()
    {
        await using var kafka = await KafkaLogTlsFixture.StartAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var source = "fullnet.integration.logging.consumer." + suffix;
        var dlqTopic = "fullnet.integration.logging.dlq." + suffix;
        var group = "fullnet.integration.logging.group." + suffix;
        await kafka.EnsureTopicsAsync(source, dlqTopic);
        using var sourceProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        var id = Guid.CreateVersion7().ToString("D");
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expiry = occurred.AddDays(30);
        var json = $$"""
            {"@t":"{{occurred:O}}","@mt":"ok","LogEventId":"{{id}}","log.class":"diagnostic","OccurredAtUtc":"{{occurred:O}}","ExpiresAtUtc":"{{expiry:O}}","IndexRouteVersion":2}
            """;
        await sourceProducer.ProduceAsync(source,
            new Message<string, byte[]> { Key = id, Value = Encoding.UTF8.GetBytes(json) });
        await sourceProducer.ProduceAsync(source,
            new Message<string, byte[]> { Key = "bad", Value = Encoding.UTF8.GetBytes("not-json") });
        using var rawProducer = new ProducerBuilder<byte[], byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
        }).Build();
        await rawProducer.ProduceAsync(source,
            new Message<byte[], byte[]> { Key = [0xff], Value = Encoding.UTF8.GetBytes(json) });

        using var consumer = new ConsumerBuilder<byte[], byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            GroupId = group,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        using var dlqProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaLocation = kafka.CaPath,
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
        consumer.Subscribe(source);
        var indexes = new List<string>();
        var processor = new SequentialLogDeliveryProcessor(
            new ConfirmingSink(indexes),
            new KafkaLogDeadLetterSink(dlqProducer, dlqTopic),
            new KafkaLogOffsetCommitter(consumer),
            new Dictionary<int, int> { [2] = 30 }, 1024);
        var positions = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var result = consumer.Consume(TimeSpan.FromSeconds(20));
            Assert.IsNotNull(result);
            positions.Add(result.Offset.Value);
            Assert.AreEqual(LogDeliveryDisposition.Completed,
                await processor.ProcessAsync(new KafkaLogInput(result.Topic,
                    result.Partition.Value, result.Offset.Value,
                    KafkaLogKeyDecoder.Decode(result.Message.Key), result.Message.Value,
                    result.Message.Key)));
        }

        CollectionAssert.AreEqual(new long[] { 0, 1, 2 }, positions);
        Assert.HasCount(1, indexes);
        StringAssert.StartsWith(indexes[0], "fn-logs-2-diagnostic-");
        var committed = consumer.Committed([new TopicPartition(source, new Partition(0))],
            TimeSpan.FromSeconds(5));
        Assert.AreEqual(3L, committed[0].Offset.Value);
        consumer.Close();

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
        Assert.AreEqual(source + ":0:1", isolated.Message.Key);
        Assert.AreEqual("not-json", Encoding.UTF8.GetString(isolated.Message.Value));
        var invalidKey = dlqReader.Consume(TimeSpan.FromSeconds(20));
        Assert.IsNotNull(invalidKey);
        Assert.AreEqual(source + ":0:2", invalidKey.Message.Key);
        CollectionAssert.AreEqual(new byte[] { 0xff },
            invalidKey.Message.Headers.GetLastBytes("source-key-bytes"));
        dlqReader.Close();
    }

    private sealed class ConfirmingSink(List<string> indexes) : ILogDocumentSink
    {
        public Task<BulkItemOutcome> WriteAsync(ParsedLogRecord record, string indexName,
            CancellationToken cancellationToken)
        {
            indexes.Add(indexName);
            return Task.FromResult(BulkItemOutcome.Succeeded);
        }
    }

    private sealed class UnexpectedDlq : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason,
            CancellationToken cancellationToken) => throw new AssertFailedException(
            $"Valid record should not be isolated: {reason}");
    }

    private sealed class BlockingSink : ILogDocumentSink, ILogDocumentBatchSink
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Complete() => _complete.TrySetResult();

        public async Task<BulkItemOutcome> WriteAsync(ParsedLogRecord record, string indexName,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return BulkItemOutcome.Succeeded;
        }

        public async Task<BulkItemOutcome[]> WriteBatchAsync(IReadOnlyList<LogDocumentWrite> writes,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return Enumerable.Repeat(BulkItemOutcome.Succeeded, writes.Count).ToArray();
        }
    }
}
