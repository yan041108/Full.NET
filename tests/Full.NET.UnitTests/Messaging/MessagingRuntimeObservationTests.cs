using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

/// <summary>覆盖真实观测值的生命周期，禁止缺失、失败或陈旧样本变成健康。</summary>
[TestClass]
[DoNotParallelize]
public sealed partial class MessagingRuntimeObservationTests
{
    [TestMethod]
    public void Composed_host_registrations_bind_custom_retry_stages_once()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Kafka:RetryStages:0"] = "1s",
            ["Messaging:Kafka:RetryStages:1"] = "2s",
            ["Messaging:Kafka:RetryStages:2"] = "3s",
            ["Messaging:KafkaConnectRollback:Streams:0:EventType"] = "fullnet.organization.unit.changed",
        }).Build();
        var services = new ServiceCollection();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        services.AddSingleton(environment);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFullNetKafkaMessaging(configuration, "Development");
        services.AddFullNetKafkaDeliveryControl(configuration);
        services.AddFullNetKafkaReplayOperations(configuration);
        services.AddFullNetCdcMonitoring(configuration);
        using var provider = services.BuildServiceProvider();
        CollectionAssert.AreEqual(new[] { "1s", "2s", "3s" }, provider.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value.RetryStages);
        Assert.AreEqual(1, provider.GetRequiredService<IOptions<KafkaConnectRollbackOptions>>().Value.Streams.Length,
            "监控与控制面装配不能重复绑定同一资源映射。");
    }

    [TestMethod]
    public void Unobserved_components_have_no_health_values()
    {
        var observations = new MessagingRuntimeObservation(TimeProvider.System, TimeSpan.FromSeconds(120));
        Assert.AreEqual(0, observations.ObserveValue("capture_job").Count());
        Assert.AreEqual(0, observations.ObserveStatus().Count());
    }

    [TestMethod]
    public void Failure_removes_previous_health_and_recovery_requires_a_new_sample()
    {
        var observations = new MessagingRuntimeObservation(TimeProvider.System, TimeSpan.FromSeconds(120));
        observations.Set("capture_job", "sqlserver", "healthy", 1);
        Assert.AreEqual(1d, observations.ObserveValue("capture_job").Single().Value);
        observations.Set("capture_job", "sqlserver", "error");
        Assert.AreEqual(0, observations.ObserveValue("capture_job").Count());
        Assert.AreEqual("error", Tag(observations.ObserveStatus().Single(), "result"));
        observations.Set("capture_job", "sqlserver", "unhealthy", 0);
        Assert.AreEqual(0d, observations.ObserveValue("capture_job").Single().Value);
    }

    [TestMethod]
    public void Old_samples_expire_even_when_exporter_continues_scraping()
    {
        var time = new ManualTime();
        var observations = new MessagingRuntimeObservation(time, TimeSpan.FromSeconds(120));
        observations.Set("capture_job", "sqlserver", "healthy", 1);
        time.Advance(121);
        Assert.AreEqual("stale", Tag(observations.ObserveStatus().Single(), "result"));
        Assert.AreEqual(0, observations.ObserveValue("capture_job").Count());
        Assert.AreEqual(120d, observations.ObserveTimestamps(ageLimit: true).Single().Value);
        Assert.IsTrue(observations.ObserveTimestamps().Single().Value > 0, "缓存的 OTLP 样本必须保留真实采集时间，供平台判定过期。");
    }

    [TestMethod]
    public void Disabled_collection_stays_distinct_from_stale_or_healthy()
    {
        var time = new ManualTime();
        var observations = new MessagingRuntimeObservation(time, TimeSpan.FromSeconds(120));
        observations.Set("collector", "cdc", "disabled");
        time.Advance(121);
        Assert.AreEqual("disabled", Tag(observations.ObserveStatus().Single(), "result"));
        Assert.AreEqual(0, observations.ObserveTimestamps().Count());
    }

    [TestMethod]
    [DataRow("{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"RUNNING\"}]}", true)]
    [DataRow("{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"FAILED\"}]}", false)]
    [DataRow("{\"connector\":{\"state\":\"PAUSED\"},\"tasks\":[{\"state\":\"PAUSED\"}]}", false)]
    public void Connector_requires_actual_running_tasks(string status, bool expected) =>
        Assert.AreEqual(expected, KafkaCdcMonitoringWorker.ReadConnectorRunning(status));

    [TestMethod]
    [DataRow(null)]
    [DataRow("{}")]
    [DataRow("{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[]}")]
    public void Missing_connector_tasks_are_unknown(string? status) =>
        Assert.IsNull(KafkaCdcMonitoringWorker.ReadConnectorRunning(status));

    [TestMethod]
    public async Task Worker_uses_platform_reader_and_removes_values_after_permission_failure()
    {
        using var capture = new Capture();
        var source = Substitute.For<ICdcPlatformHealthReader>();
        source.ReadAsync(Arg.Any<CancellationToken>()).Returns(new CdcPlatformHealthSnapshot("sqlserver", true, null, false));
        await using var services = Services(source);
        using var worker = new KafkaCdcMonitoringWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions { Enabled = true }), Options.Create(new KafkaConnectRollbackOptions()),
            Options.Create(new KafkaMessagingOptions()), Substitute.For<IKafkaConnectAdminClient>(), new KafkaConsumerLagObserver());
        await worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.IsTrue(capture.Values.Any(item => item.Name == "fullnet.messaging.cdc.sqlserver.capture_job_running" && item.Value == 1));
        capture.Values.Clear();
        source.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<CdcPlatformHealthSnapshot>(new UnauthorizedAccessException("must not appear in metrics")));
        await worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.IsFalse(capture.Values.Any(item => item.Name == "fullnet.messaging.cdc.sqlserver.capture_job_running"));
        Assert.IsTrue(capture.Values.Any(item => item.Name == "fullnet.messaging.observation.status" && item.Result == "error"));
    }

    [TestMethod]
    public async Task Disabled_worker_does_not_touch_database_or_connect()
    {
        var source = Substitute.For<ICdcPlatformHealthReader>();
        await using var services = Services(source);
        var connect = Substitute.For<IKafkaConnectAdminClient>();
        using var worker = new KafkaCdcMonitoringWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions()), Options.Create(new KafkaConnectRollbackOptions()),
            Options.Create(new KafkaMessagingOptions()), connect, new KafkaConsumerLagObserver());
        await worker.StartAsync(default);
        await worker.StopAsync(default);
        await source.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
        await connect.DidNotReceive().TryGetConnectorStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Running_connector_with_wrong_route_is_not_reported_healthy()
    {
        using var capture = new Capture();
        var source = Substitute.For<ICdcPlatformHealthReader>();
        source.ReadAsync(Arg.Any<CancellationToken>()).Returns(new CdcPlatformHealthSnapshot("mysql", null, 48, false));
        await using var services = Services(source);
        var connect = Substitute.For<IKafkaConnectAdminClient>();
        KafkaMessagingTelemetry.UpdateConsumerLag("kafka", "route-invalid-group", 0, 0);
        connect.TryReadConnectorConfigurationAsync("pilot", Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string> { ["tasks.max"] = "1" });
        connect.TryGetConnectorStatusAsync("pilot", Arg.Any<CancellationToken>()).Returns("{\"connector\":{\"state\":\"RUNNING\"},\"tasks\":[{\"state\":\"RUNNING\"}]}");
        using var worker = new KafkaCdcMonitoringWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions { Enabled = true }), Options.Create(TestControl("pilot", "route-invalid-group")),
            Options.Create(new KafkaMessagingOptions { Enabled = true }), connect, new KafkaConsumerLagObserver());
        await worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.IsFalse(capture.Values.Any(item => item.Name == "fullnet.messaging.connector.running"));
        Assert.IsTrue(capture.Values.Any(item => item.Component == "connector" && item.Result == "error"));
        Assert.IsFalse(capture.Values.Any(item => item.Code == "route-invalid-group" && item.Name is "fullnet.messaging.kafka.observed_topics.lag" or "fullnet.messaging.kafka.observed_topics.lag_retention_ratio"));
        await connect.DidNotReceive().TryGetConnectorStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Inflight_connector_refresh_preserves_fresh_alert_sample_until_a_real_failure_is_known()
    {
        using var capture = new Capture();
        var source = Substitute.For<ICdcPlatformHealthReader>();
        source.ReadAsync(Arg.Any<CancellationToken>()).Returns(new CdcPlatformHealthSnapshot("mysql", null, 48, false));
        await using var services = Services(source);
        var pending = new TaskCompletionSource<IReadOnlyDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connect = Substitute.For<IKafkaConnectAdminClient>();
        connect.TryReadConnectorConfigurationAsync("pilot", Arg.Any<CancellationToken>()).Returns(pending.Task);
        using var worker = new KafkaCdcMonitoringWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions { Enabled = true }), Options.Create(TestControl("pilot")),
            Options.Create(new KafkaMessagingOptions { Enabled = true }), connect, new KafkaConsumerLagObserver());
        KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", "pilot", null, true);
        var sampling = worker.SampleAsync(default);
        try
        {
            Assert.IsFalse(sampling.IsCompleted);
            capture.Listener.RecordObservableInstruments();
            Assert.IsTrue(capture.Values.Any(item => item.Code == "pilot" && item.Name == "fullnet.messaging.connector.offset.unrecoverable" && item.Value == 1),
                "正常刷新不能造成短暂空序列，反复重置 Prometheus 告警计时。");
        }
        finally { pending.TrySetResult(new Dictionary<string, string> { ["tasks.max"] = "1" }); await sampling; }
        capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
        Assert.IsFalse(capture.Values.Any(item => item.Code == "pilot" && item.Name == "fullnet.messaging.connector.offset.unrecoverable"),
            "正式资源校验失败后必须立即撤下旧数值。");
    }

    [TestMethod]
    public async Task Consumer_lag_respects_configured_sample_age_and_expires()
    {
        const string code = "fullnet.observation.lag-expiry";
        Assert.IsTrue(KafkaMessagingTelemetry.LagSampleFresh(1000, 360_000, 301_000));
        Assert.IsFalse(KafkaMessagingTelemetry.LagSampleFresh(1000, 120_000, 301_000));
        Assert.IsFalse(KafkaMessagingTelemetry.LagSampleFresh(1000, 360_000, 361_001));
        using var capture = new Capture();
        try
        {
            KafkaMessagingTelemetry.UpdateConsumerLag("kafka", code, 42, 0.9, TimeSpan.FromMilliseconds(100));
            capture.Listener.RecordObservableInstruments();
            Assert.IsTrue(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag"));
            Assert.IsTrue(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag_retention_ratio"));
            await Task.Delay(150);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code && (item.Name == "fullnet.messaging.kafka.observed_topics.lag" || item.Name == "fullnet.messaging.kafka.observed_topics.lag_retention_ratio")));
        }
        finally { KafkaMessagingTelemetry.RemoveConsumerState(code); }
    }

    [TestMethod]
    public async Task Inflight_consumer_refresh_preserves_fresh_value_but_failed_query_removes_it()
    {
        const string code = "fullnet.observation.pending-lag";
        using var capture = new Capture();
        var pending = new TaskCompletionSource<IReadOnlyList<Confluent.Kafka.TopicPartition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync("topic").Returns(pending.Task);
        KafkaMessagingTelemetry.UpdateConsumerLag("kafka", code, 7, null);
        var sampling = new KafkaConsumerLagObserver(_ => client).ObserveLagAsync(new(), "topic", code, default, publishTelemetry: true);
        try
        {
            Assert.IsFalse(sampling.IsCompleted);
            capture.Listener.RecordObservableInstruments();
            Assert.IsTrue(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag" && item.Value == 7));
        }
        finally { pending.TrySetException(new IOException("不得进入指标的错误文本")); await sampling; }
        capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
        Assert.IsFalse(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag"));
        KafkaMessagingTelemetry.RemoveConsumerState(code);
    }

    [TestMethod]
    public void Consumer_without_lag_sample_does_not_report_zero_or_retention_health()
    {
        const string code = "fullnet.observation.no-sample";
        using var capture = new Capture();
        KafkaMessagingTelemetry.UpdateConsumerState("kafka", code, 0, 0, 1, 0);
        try
        {
            capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code && item.Name is "fullnet.messaging.kafka.observed_topics.lag" or "fullnet.messaging.kafka.observed_topics.lag_retention_ratio"));
            KafkaMessagingTelemetry.UpdateConsumerLag("kafka", code, 42, null);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsTrue(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag" && item.Value == 42));
            Assert.IsFalse(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag_retention_ratio"));
            KafkaMessagingTelemetry.InvalidateConsumerLag(code);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.kafka.observed_topics.lag"));
        }
        finally { KafkaMessagingTelemetry.RemoveConsumerState(code); }
    }

    [TestMethod]
    public async Task Connector_unknown_values_are_absent_and_real_samples_expire_or_invalidate()
    {
        const string code = "fullnet.observation.source-expiry";
        using var capture = new Capture();
        try
        {
            KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", code, null, null);
            capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code));
            KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", code, null, false);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsTrue(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.connector.offset.unrecoverable" && item.Value == 0));
            Assert.IsFalse(capture.Values.Any(item => item.Code == code && item.Name == "fullnet.messaging.connector.lag"));
            KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", code, null, false, TimeSpan.FromMilliseconds(1));
            await Task.Delay(30);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code), "旧位点健康不能无限留在 exporter。");
            KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", code, 12, true);
            KafkaMessagingTelemetry.InvalidateConnectorHealth(code);
            capture.Values.Clear(); capture.Listener.RecordObservableInstruments();
            Assert.IsFalse(capture.Values.Any(item => item.Code == code));
        }
        finally { KafkaMessagingTelemetry.InvalidateConnectorHealth(code); }
    }

    [TestMethod]
    public void Observation_budget_rejects_stale_before_next_poll()
    {
        var validator = new KafkaCdcMonitoringOptionsValidator();
        Assert.IsTrue(validator.Validate(null, new() { MaximumSampleAgeSeconds = 10 }).Failed);
        Assert.IsTrue(validator.Validate(null, new()).Succeeded);
    }

    private static ServiceProvider Services(ICdcPlatformHealthReader source) => new ServiceCollection()
        .AddSingleton(source).AddSingleton(Substitute.For<ICurrentTenantContextWriter>()).BuildServiceProvider();

    private static KafkaConnectRollbackOptions TestControl(string connector, string? consumer = null)
    {
        var control = KafkaDeliveryControlTests.MonitoringOptions();
        control.Streams[0].ConnectorName = connector;
        if (consumer is not null) control.Streams[0].ConsumerGroupId = consumer;
        return control;
    }

    [TestMethod]
    public async Task Optional_source_time_permission_failure_retains_offset_health_and_removes_old_lag()
    {
        using var capture = new Capture();
        var source = Substitute.For<ICdcPlatformHealthReader>();
        source.ReadAsync(Arg.Any<CancellationToken>()).Returns(new CdcPlatformHealthSnapshot("mysql", null, 48, false));
        source.ReadSourcePositionAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>())
            .Returns(new CdcSourcePositionHealthSnapshot(true, null));
        source.ReadSourceTimeLagAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<double?>(new UnauthorizedAccessException()));
        await using var services = Services(source);
        var connect = Substitute.For<IKafkaConnectAdminClient>();
        connect.TryReadConnectorConfigurationAsync("orders", Arg.Any<CancellationToken>()).Returns(KafkaDeliveryControlTests.MonitoringConfiguration());
        connect.TryGetConnectorStatusAsync("orders", Arg.Any<CancellationToken>()).Returns("""{"connector":{"state":"RUNNING"},"tasks":[{"state":"RUNNING"}]}""");
        connect.TryReadConnectorPositionAsync("orders", Arg.Any<CancellationToken>()).Returns(CdcDeliveryPosition.ForMySql(null, "mysql-bin.000001", 950));
        var client = Substitute.For<IKafkaLagQueryClient>();
        client.ReadPartitionsAsync(Arg.Any<string>()).Returns(Array.Empty<Confluent.Kafka.TopicPartition>());
        using var worker = new KafkaCdcMonitoringWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions { Enabled = true }), Options.Create(KafkaDeliveryControlTests.MonitoringOptions()),
            Options.Create(new KafkaMessagingOptions { Enabled = true }), connect, new KafkaConsumerLagObserver(_ => client));
        KafkaMessagingTelemetry.UpdateConnectorHealth("debezium", "pilot", 99, false);
        await worker.SampleAsync(default);
        capture.Listener.RecordObservableInstruments();
        Assert.IsTrue(capture.Values.Any(item => item.Component == "connector_offset" && item.Result == "healthy"));
        Assert.IsTrue(capture.Values.Any(item => item.Component == "connector_lag" && item.Result == "error"));
        Assert.IsFalse(capture.Values.Any(item => item.Name == "fullnet.messaging.connector.lag" && item.Code == "pilot"));
        Assert.IsTrue(capture.Values.Any(item => item.Name == "fullnet.messaging.connector.offset.unrecoverable" && item.Code == "pilot" && item.Value == 0));
        await source.Received(1).ReadSourceTimeLagAsync("test", Arg.Any<CdcDeliveryPosition>(), Arg.Any<CancellationToken>());
    }

    private static object? Tag(Measurement<long> sample, string name) => sample.Tags.ToArray().Single(item => item.Key == name).Value;

    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _timestamp;
        public void Advance(long seconds) => _timestamp += seconds;
    }

    private sealed class Capture : IDisposable
    {
        internal MeterListener Listener { get; } = new();
        internal List<(string Name, double Value, string? Result, string? Code, string? Component)> Values { get; } = [];
        public Capture()
        {
            Listener.InstrumentPublished = (instrument, listener) =>
            { if (instrument.Meter.Name == KafkaMessagingTelemetry.MeterName) listener.EnableMeasurementEvents(instrument); };
            Listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
            Listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            Listener.Start();
        }
        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = tags.ToArray().ToDictionary(item => item.Key, item => item.Value?.ToString());
            Values.Add((instrument.Name, value, map.GetValueOrDefault("result"), map.GetValueOrDefault("consumer_code") ?? map.GetValueOrDefault("connector_code") ?? map.GetValueOrDefault("resource_code"), map.GetValueOrDefault("component")));
            foreach (var key in map.Keys) CollectionAssert.Contains(KafkaMessagingTelemetry.AllowedTagKeys.ToArray(), key);
        }
        public void Dispose() => Listener.Dispose();
    }
}
