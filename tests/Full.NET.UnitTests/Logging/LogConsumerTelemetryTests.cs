using System.Text.Json;
using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

/// <summary>消费统计只暴露聚合值；未知、过期和重平衡不得伪装成零积压。</summary>
[TestClass]
public sealed class LogConsumerTelemetryTests
{
    private const string Topic = "secret-source-topic";
    private static readonly (string Topic, int Partition)[] Assignment = [(Topic, 0)];

    [TestMethod]
    public void InitialAndUnassignedLagAreUnknown()
    {
        var state = new ConsumerRuntimeState();
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
        state.Assign(Assignment);
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_records -1"));
        state.Statistics(Stats(4));
        state.Assign([]);
        Assert.IsFalse(state.IsReady);
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
    }

    [TestMethod]
    public void CommittedLagDoesNotUseStoredLagOrLeakTopic()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        state.Statistics(Stats(42));
        var metrics = state.Metrics();
        Assert.IsTrue(metrics.Contains("fullnet_log_consumer_lag_records 42"));
        Assert.IsTrue(metrics.Contains("fullnet_log_consumer_lag_known 1"));
        Assert.IsFalse(metrics.Contains(Topic));
    }

    [TestMethod]
    public void UnknownPartitionLagInvalidatesWholeSample()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        state.Statistics(Stats(10));
        state.Statistics(Stats(-1));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
    }

    [TestMethod]
    public void StaleNativeTimestampCannotBeRefreshedByLateCallback()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        state.Statistics(Stats(10, DateTimeOffset.UtcNow.AddSeconds(-40).ToUnixTimeSeconds()));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
    }

    [TestMethod]
    public void DifferentAssignmentWithSameCountCannotReuseOldSample()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        state.Statistics(Stats(10));
        state.Assign([("replacement-topic", 0)]);
        state.Statistics(Stats(10));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
    }

    [TestMethod]
    public void MalformedOversizeAndDeepStatisticsFailClosedWithoutThrowing()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        foreach (var invalid in new[] { "{", "[]", new string('x', 262145),
            new string('[', 20) + "0" + new string(']', 20) })
        {
            state.Statistics(Stats(10));
            state.Statistics(invalid);
            Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
        }
    }

    [TestMethod]
    public void UnsupportedAssignmentSizeIsReadyButLagUnknown()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Enumerable.Range(0, 129).Select(i => (Topic, i)).ToArray());
        Assert.IsTrue(state.IsReady);
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_assigned_partitions 129"));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
    }

    [TestMethod]
    public void BrokerFailureDoesNotRefreshCachedZeroLag()
    {
        var state = new ConsumerRuntimeState();
        state.Assign(Assignment);
        state.Statistics(Stats(0));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 1"));
        state.Statistics(Stats(0, brokerState: "DOWN"));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_known 0"));
        Assert.IsTrue(state.Metrics().Contains("fullnet_log_consumer_lag_records -1"));
    }

    [TestMethod]
    public void ConcurrentResultsStayInFixedSeries()
    {
        var state = new ConsumerRuntimeState();
        Parallel.For(0, 1000, _ => state.Record(ConsumerDeliveryMetric.EsConfirmed));
        Assert.IsTrue(state.Metrics().Contains("stage=\"es\",outcome=\"confirmed\"} 1000"));
        Assert.AreEqual(9, state.Metrics().Split('\n').Count(line => line.StartsWith("fullnet_log_consumer_delivery_results_total{")));
    }

    [TestMethod]
    public async Task DocumentOutcomeAndTransportFailureKeepOriginalSemantics()
    {
        var state = new ConsumerRuntimeState();
        var sink = new ObservedLogDocumentSink(new DocumentSink(() => Task.FromResult(BulkItemOutcome.Retry)), state);
        Assert.AreEqual(BulkItemOutcome.Retry, await sink.WriteAsync(null!, "unused", default));
        var failing = new ObservedLogDocumentSink(new DocumentSink(() => throw new HttpRequestException("secret")), state);
        await Assert.ThrowsAsync<HttpRequestException>(() => failing.WriteAsync(null!, "unused", default));
        var metrics = state.Metrics();
        Assert.IsTrue(metrics.Contains("stage=\"es\",outcome=\"retry\"} 1"));
        Assert.IsTrue(metrics.Contains("stage=\"es\",outcome=\"error\"} 1"));
        Assert.IsFalse(metrics.Contains("secret"));
    }

    [TestMethod]
    public async Task ShutdownCancellationDoesNotCountAsDeliveryFailure()
    {
        var state = new ConsumerRuntimeState();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var sink = new ObservedLogDocumentSink(new DocumentSink(() => Task.FromCanceled<BulkItemOutcome>(canceled.Token)), state);
        await Assert.ThrowsAsync<TaskCanceledException>(() => sink.WriteAsync(null!, "unused", canceled.Token));
        Assert.IsTrue(state.Metrics().Contains("stage=\"es\",outcome=\"error\"} 0"));
    }

    [TestMethod]
    public async Task DeadLetterAndCommitterConfirmOnlyAfterDelegateReturns()
    {
        var state = new ConsumerRuntimeState();
        var dlq = new ObservedLogDeadLetterSink(new DeadLetterSink(), state);
        Assert.IsFalse(await dlq.PublishAsync(default, LogIsolationReason.InvalidRecord, default));
        var committer = new ObservedLogOffsetCommitter(new FailingCommitter(), state);
        Assert.Throws<InvalidOperationException>(() => committer.CommitNext(default));
        Assert.IsTrue(state.Metrics().Contains("stage=\"dlq\",outcome=\"retry\"} 1"));
        Assert.IsTrue(state.Metrics().Contains("stage=\"offset\",outcome=\"confirmed\"} 0"));
        Assert.IsTrue(state.Metrics().Contains("stage=\"offset\",outcome=\"error\"} 1"));
    }

    private static string Stats(long lag, long? time = null, string brokerState = "UP") => JsonSerializer.Serialize(new
    {
        time = time ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        cgrp = new { assignment_size = 1 },
        brokers = new Dictionary<string, object> { ["test-broker"] = new { nodeid = 1, state = brokerState } },
        topics = new Dictionary<string, object> { [Topic] = new { partitions = new Dictionary<string, object>
            { ["0"] = new { partition = 0, broker = 1, consumer_lag = lag, consumer_lag_stored = 0 } } } },
    });

    private sealed class DocumentSink(Func<Task<BulkItemOutcome>> response) : ILogDocumentSink
    {
        public Task<BulkItemOutcome> WriteAsync(ParsedLogRecord record, string indexName, CancellationToken token) => response();
    }
    private sealed class DeadLetterSink : ILogDeadLetterSink
    {
        public Task<bool> PublishAsync(KafkaLogInput input, LogIsolationReason reason, CancellationToken token) => Task.FromResult(false);
    }
    private sealed class FailingCommitter : ILogOffsetCommitter
    {
        public void CommitNext(KafkaLogInput input) => throw new InvalidOperationException();
    }
}
