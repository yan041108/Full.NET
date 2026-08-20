using System.Diagnostics.Metrics;
using Full.NET.Host.Worker;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

/// <summary>
/// 验证消息链路指标标签白名单与关键低基数仪表；禁止 Secret/Payload/SQL/Tenant/User。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingCdcObservabilityTelemetryTests
{
    [TestMethod]
    public void OutboxBacklogTelemetry_records_empty_poll_backoff_without_tags()
    {
        using var capture = new MeterCapture(OutboxBacklogTelemetry.MeterName);

        OutboxBacklogTelemetry.RecordEmptyPollBackoff(TimeSpan.FromMilliseconds(800));

        Assert.IsTrue(
            capture.Measurements.Exists(item =>
                item.Name == "fullnet.outbox.legacy.empty_poll.backoff"
                && Math.Abs(item.Value - 0.8d) < 0.0001d
                && item.TagCount == 0));
    }

    [TestMethod]
    public void OutboxBacklogTelemetry_commit_to_capture_allows_only_database_provider()
    {
        using var capture = new MeterCapture(OutboxBacklogTelemetry.MeterName);

        OutboxBacklogTelemetry.RecordCommitToCapture(1.5d, "mysql");

        var sample = capture.TaggedMeasurements.Single(item =>
            item.Name == "fullnet.outbox.commit_to_capture");
        Assert.AreEqual(1.5d, sample.Value, 0.0001d);
        CollectionAssert.AreEquivalent(
            new[] { "database_provider" },
            sample.TagKeys.ToArray());
        Assert.AreEqual("mysql", sample.Tags["database_provider"]);
        AssertForbiddenKeysAbsent(sample.TagKeys);
    }

    [TestMethod]
    public void KafkaMessagingTelemetry_records_inbox_retry_dlq_ownership_and_connector_gauges()
    {
        using var capture = new MeterCapture(KafkaMessagingTelemetry.MeterName);

        KafkaMessagingTelemetry.RecordConsume(
            "kafka",
            "organization.unit-changed.v1",
            "fullnet.identity.organization-unit",
            "fullnet.organization.unit.changed",
            "already_processed");
        KafkaMessagingTelemetry.RecordConsume(
            "kafka",
            "organization.unit-changed.v1",
            "fullnet.identity.organization-unit",
            "fullnet.organization.unit.changed",
            "retry_routed",
            "messaging.transient.timeout");
        KafkaMessagingTelemetry.RecordConsume(
            "kafka",
            "organization.unit-changed.v1",
            "fullnet.identity.organization-unit",
            "fullnet.organization.unit.changed",
            "dead_lettered",
            "messaging.contract.invalid_payload");
        KafkaMessagingTelemetry.RecordPartitionFlow(
            "kafka",
            "organization.unit-changed.v1",
            "fullnet.identity.organization-unit",
            "retry_scheduled");
        KafkaMessagingTelemetry.RecordOwnershipWait(
            "kafka",
            "fullnet.identity.organization-unit",
            30d);
        KafkaMessagingTelemetry.RecordOwnershipTransition(
            "kafka",
            "fullnet.identity.organization-unit",
            "cutover");
        KafkaMessagingTelemetry.UpdateConnectorHealth(
            "debezium",
            "fullnet.outbox.mysql",
            lagSeconds: 12d,
            offsetUnrecoverable: true);
        KafkaMessagingTelemetry.RecordConnectorError(
            "debezium",
            "fullnet.outbox.mysql",
            "connector.offset.unrecoverable");
        KafkaMessagingTelemetry.UpdateConsumerLag(
            "kafka",
            "fullnet.identity.organization-unit",
            lagMessages: 42,
            lagRetentionRatio: 0.85d);
        KafkaMessagingTelemetry.UpdateCdcPlatformHealth(
            sqlServerCaptureJobRunning: false,
            mySqlBinlogRetentionHours: 12d);

        capture.Listener.RecordObservableInstruments();

        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.inbox.duplicates"));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.kafka.retry.routed"));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.kafka.dead_letter.published"));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.kafka.uncommitted.retry"));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.ownership.transitions"));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.ownership.wait"
            && Math.Abs(item.Value - 30d) < 0.0001d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.kafka.consumer.lag"
            && item.Value == 42d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.kafka.lag_retention_ratio"
            && Math.Abs(item.Value - 0.85d) < 0.0001d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.connector.lag"
            && Math.Abs(item.Value - 12d) < 0.0001d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.connector.offset.unrecoverable"
            && item.Value == 1d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.cdc.sqlserver.capture_job_running"
            && item.Value == 0d));
        Assert.IsTrue(capture.Measurements.Exists(item =>
            item.Name == "fullnet.messaging.cdc.mysql.binlog_retention_hours"
            && Math.Abs(item.Value - 12d) < 0.0001d));

        foreach (var sample in capture.TaggedMeasurements)
        {
            foreach (var key in sample.TagKeys)
            {
                CollectionAssert.Contains(
                    KafkaMessagingTelemetry.AllowedTagKeys.ToList(),
                    key);
            }

            AssertForbiddenKeysAbsent(sample.TagKeys);
        }

        KafkaMessagingTelemetry.RemoveConsumerState("fullnet.identity.organization-unit");
        KafkaMessagingTelemetry.UpdateCdcPlatformHealth(
            sqlServerCaptureJobRunning: true,
            mySqlBinlogRetentionHours: 168d);
    }

    [TestMethod]
    public void KafkaMessagingTelemetry_forbidden_tag_fragments_cover_secret_payload_sql_tenant_user()
    {
        CollectionAssert.Contains(
            KafkaMessagingTelemetry.ForbiddenTagKeyFragments.ToList(),
            "secret");
        CollectionAssert.Contains(
            KafkaMessagingTelemetry.ForbiddenTagKeyFragments.ToList(),
            "payload");
        CollectionAssert.Contains(
            KafkaMessagingTelemetry.ForbiddenTagKeyFragments.ToList(),
            "sql");
        CollectionAssert.Contains(
            KafkaMessagingTelemetry.ForbiddenTagKeyFragments.ToList(),
            "tenant");
        CollectionAssert.Contains(
            KafkaMessagingTelemetry.ForbiddenTagKeyFragments.ToList(),
            "user");
    }

    private static void AssertForbiddenKeysAbsent(IReadOnlyList<string> tagKeys)
    {
        foreach (var key in tagKeys)
        {
            foreach (var fragment in KafkaMessagingTelemetry.ForbiddenTagKeyFragments)
            {
                Assert.IsFalse(
                    key.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"Tag key '{key}' contains forbidden fragment '{fragment}'.");
            }
        }
    }

    private sealed class MeterCapture : IDisposable
    {
        public MeterCapture(string meterName)
        {
            Listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == meterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            Listener.SetMeasurementEventCallback<long>(OnMeasurement);
            Listener.SetMeasurementEventCallback<double>(OnMeasurement);
            Listener.Start();
        }

        public MeterListener Listener { get; }

        public List<(string Name, double Value, int TagCount)> Measurements { get; } = [];

        public List<TaggedMeasurement> TaggedMeasurements { get; } = [];

        public void Dispose() => Listener.Dispose();

        private void OnMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? _) =>
            OnMeasurement(instrument, measurement, tags);

        private void OnMeasurement(
            Instrument instrument,
            double measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? _) =>
            OnMeasurement(instrument, measurement, tags);

        private void OnMeasurement(
            Instrument instrument,
            double measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Measurements.Add((instrument.Name, measurement, tags.Length));
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                map[tag.Key] = tag.Value?.ToString();
            }

            TaggedMeasurements.Add(
                new TaggedMeasurement(instrument.Name, measurement, map));
        }
    }

    private sealed record TaggedMeasurement(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string?> Tags)
    {
        public IReadOnlyList<string> TagKeys => Tags.Keys.ToArray();
    }
}
