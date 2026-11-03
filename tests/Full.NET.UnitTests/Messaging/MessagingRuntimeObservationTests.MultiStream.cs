using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

public sealed partial class MessagingRuntimeObservationTests
{
    [TestMethod]
    public async Task Shared_group_reports_the_sum_and_an_independent_query_cannot_replace_it()
    {
        using var capture = new Capture();
        await using var fixture = new MonitoringFixture();
        await fixture.Worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.AreEqual(10d, capture.Values.Single(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group).Value);
        Assert.IsFalse(capture.Values.Any(value => value.Name == "fullnet.messaging.kafka.consumer.lag" && value.Code == MonitoringFixture.Group),
            "映射正式流的部分样本不能冒充包含重试与其它订阅的完整消费组积压。");
        Assert.AreEqual(4, capture.Values.Count(value => value.Component == "connector_offset" && value.Result == "healthy"));
        capture.Values.Clear();
        Assert.AreEqual(1L, (await fixture.Lag.ObserveLagAsync(new(), fixture.Control.Streams[0].TopicName, MonitoringFixture.Group, default))!.TotalLagMessages);
        capture.Listener.RecordObservableInstruments();
        Assert.AreEqual(10d, capture.Values.Single(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group).Value);
        await fixture.Source.Received(1).ReadAsync(Arg.Any<CancellationToken>());
        Assert.IsFalse(capture.Values.Any(value => value.Code?.StartsWith("private-connector-", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public async Task A_wrong_route_keeps_other_streams_visible_and_withdraws_the_shared_sum()
    {
        using var capture = new Capture();
        await using var fixture = new MonitoringFixture();
        await fixture.Worker.SampleAsync(default);
        fixture.Configurations[1]["transforms.outbox.route.topic.replacement"] = "unrelated.topic.v1";
        await fixture.Worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.AreEqual(3, capture.Values.Count(value => value.Component == "connector_offset" && value.Result == "healthy"));
        Assert.IsTrue(capture.Values.Any(value => value.Component == "connector" && value.Code == fixture.Control.Streams[1].TopicName && value.Result == "error"));
        Assert.IsFalse(capture.Values.Any(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group));
    }

    [TestMethod]
    public async Task A_source_position_failure_keeps_other_positions_and_independent_message_counts()
    {
        using var capture = new Capture();
        await using var fixture = new MonitoringFixture();
        fixture.Connect.TryReadConnectorPositionAsync(fixture.Control.Streams[2].ConnectorName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CdcDeliveryPosition?>(new UnauthorizedAccessException()));
        await fixture.Worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.AreEqual(3, capture.Values.Count(value => value.Component == "connector_offset" && value.Result == "healthy"));
        Assert.IsTrue(capture.Values.Any(value => value.Component == "connector_offset" && value.Code == fixture.Control.Streams[2].TopicName && value.Result == "error"));
        Assert.AreEqual(10d, capture.Values.Single(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group).Value);
    }

    [TestMethod]
    public async Task Retention_identity_is_bound_to_each_topic_and_a_failed_ratio_does_not_erase_counts()
    {
        using var capture = new Capture();
        var options = new KafkaCdcMonitoringOptions { Enabled = true };
        for (var index = 0; index < 4; index++) options.RetentionTimeTopics.Add("monitor.stream" + index + ".v1", "topic-id-" + index);
        await using var fixture = new MonitoringFixture(options);
        fixture.Client.ReadLagRetentionRatioAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(),
            Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(), Arg.Any<KafkaRetentionTimeProbe>(), Arg.Any<CancellationToken>()).Returns((double?)0.25);
        await fixture.Worker.SampleAsync(default);
        foreach (var binding in fixture.Control.Streams)
            await fixture.Client.Received(1).ReadLagRetentionRatioAsync(binding.TopicName, Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(),
                Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(), Arg.Is<KafkaRetentionTimeProbe>(probe => probe != null && probe.TopicId == options.RetentionTimeTopics[binding.TopicName]), Arg.Any<CancellationToken>());
        fixture.Client.ReadLagRetentionRatioAsync(fixture.Control.Streams[0].TopicName, Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(),
            Arg.Any<IReadOnlyDictionary<TopicPartition, long>>(), Arg.Any<KafkaRetentionTimeProbe>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<double?>(new UnauthorizedAccessException()));
        await fixture.Worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.AreEqual(10d, capture.Values.Single(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group).Value);
        Assert.IsFalse(capture.Values.Any(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag_retention_ratio" && value.Code == MonitoringFixture.Group));
        Assert.IsTrue(capture.Values.Any(value => value.Component == "lag_retention" && value.Code == fixture.Control.Streams[0].TopicName && value.Result == "error"));
    }

    [TestMethod]
    public async Task Cancellation_withdraws_every_stream_and_the_shared_group()
    {
        using var capture = new Capture();
        await using var fixture = new MonitoringFixture();
        await fixture.Worker.SampleAsync(default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Connect.TryReadConnectorConfigurationAsync(fixture.Control.Streams[1].ConnectorName, Arg.Any<CancellationToken>())
            .Returns(call => BlockAsync(call.ArgAt<CancellationToken>(1)));
        using var cancelled = new CancellationTokenSource();
        var sampling = fixture.Worker.SampleAsync(cancelled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sampling);
        capture.Listener.RecordObservableInstruments();
        Assert.IsFalse(capture.Values.Any(value => value.Name == "fullnet.messaging.kafka.observed_topics.lag" && value.Code == MonitoringFixture.Group));
        Assert.AreEqual(4, capture.Values.Count(value => value.Component == "connector" && value.Result == "unknown"));

        async Task<IReadOnlyDictionary<string, string>?> BlockAsync(CancellationToken token)
        {
            entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return null;
        }
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("too_many")]
    [DataRow("ambiguous_time")]
    [DataRow("unmapped_time")]
    public async Task Invalid_multi_stream_boundaries_are_rejected_before_connect(string scenario)
    {
        using var capture = new Capture();
        var options = new KafkaCdcMonitoringOptions { Enabled = true };
        await using var fixture = new MonitoringFixture(options, scenario == "too_many" ? 65 : 4);
        if (scenario == "duplicate") fixture.Control.Streams[1].TopicName = fixture.Control.Streams[0].TopicName;
        if (scenario == "ambiguous_time") options.RetentionTimeTopicId = "only-one-topic-id";
        if (scenario == "unmapped_time") options.RetentionTimeTopics.Add("unknown.topic.v1", "unknown-id");
        await fixture.Worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.IsTrue(capture.Values.Any(value => value.Component == "control" && value.Result == "error"));
        await fixture.Connect.DidNotReceiveWithAnyArgs().TryReadConnectorConfigurationAsync(default!, default);
    }

    private sealed class MonitoringFixture : IAsyncDisposable
    {
        internal const string Group = "fullnet.monitor.shared";
        internal ICdcPlatformHealthReader Source { get; } = Substitute.For<ICdcPlatformHealthReader>();
        internal IKafkaConnectAdminClient Connect { get; } = Substitute.For<IKafkaConnectAdminClient>();
        internal IKafkaLagQueryClient Client { get; } = Substitute.For<IKafkaLagQueryClient>();
        internal KafkaConsumerLagObserver Lag { get; }
        internal KafkaConnectRollbackOptions Control { get; }
        internal Dictionary<string, string>[] Configurations { get; }
        internal KafkaCdcMonitoringWorker Worker { get; }
        private readonly ServiceProvider _services;

        internal MonitoringFixture(KafkaCdcMonitoringOptions? options = null, int count = 4)
        {
            Control = new() { Enabled = true, ConnectBaseUri = "http://connect:8083", Streams = Enumerable.Range(0, count).Select(index => new KafkaConnectRollbackStreamBinding {
                EventType = "fullnet.monitor.stream" + index + ".changed", SchemaVersion = 1, TopicName = "monitor.stream" + index + ".v1",
                ConnectorName = "private-connector-" + index, ConsumerGroupId = Group, SourceDatabaseName = "test" }).ToArray() };
            Configurations = Control.Streams.Select(binding => new Dictionary<string, string>(KafkaDeliveryControlTests.MonitoringConfiguration()) {
                ["transforms.outbox.route.topic.replacement"] = binding.TopicName,
                ["transforms.fullnet_filter.event.type"] = binding.EventType }).ToArray();
            Source.ReadAsync(Arg.Any<CancellationToken>()).Returns(new CdcPlatformHealthSnapshot("mysql", null, 48, false));
            Source.ReadSourcePositionAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>()).Returns(new CdcSourcePositionHealthSnapshot(true, 2));
            for (var index = 0; index < count; index++)
            {
                var config = Configurations[index];
                Connect.TryReadConnectorConfigurationAsync(Control.Streams[index].ConnectorName, Arg.Any<CancellationToken>()).Returns(call => {
                    call.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested(); return config; });
            }
            Connect.TryGetConnectorStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("""{"connector":{"state":"RUNNING"},"tasks":[{"state":"RUNNING"}]}""");
            Connect.TryReadConnectorPositionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 100));
            Client.ReadPartitionsAsync(Arg.Any<string>()).Returns(call => (IReadOnlyList<TopicPartition>)[new(call.ArgAt<string>(0), 0)]);
            Client.ReadCommittedOffsetsAsync(Group, Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                (IReadOnlyDictionary<TopicPartition, long>)call.ArgAt<IReadOnlyList<TopicPartition>>(1).ToDictionary(partition => partition, _ => 0L));
            Client.ReadEndOffsetsAsync(Arg.Any<IReadOnlyList<TopicPartition>>()).Returns(call =>
                (IReadOnlyDictionary<TopicPartition, long>)call.ArgAt<IReadOnlyList<TopicPartition>>(0).ToDictionary(partition => partition,
                    partition => (long)Array.FindIndex(Control.Streams, binding => binding.TopicName == partition.Topic) + 1));
            _services = Services(Source); Lag = new(_ => Client);
            Worker = new(_services.GetRequiredService<IServiceScopeFactory>(), Options.Create(options ?? new() { Enabled = true }),
                Options.Create(Control), Options.Create(new KafkaMessagingOptions { Enabled = true }), Connect, Lag);
        }

        public async ValueTask DisposeAsync() { Worker.Dispose(); await _services.DisposeAsync(); }
    }
}
