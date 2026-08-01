using System.Diagnostics.Metrics;
using Full.NET.Caching.Fusion;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace Full.NET.UnitTests.Caching;

[TestClass]
[DoNotParallelize]
public sealed class CacheReliabilityTelemetryTests
{
    [TestMethod]
    public void Invalidation_metrics_use_only_bounded_scope_and_outcome_tags()
    {
        using var capture = new MetricCapture();

        CacheReliabilityTelemetry.RecordLocalInvalidation(
            TimeSpan.FromMilliseconds(12),
            succeeded: true);
        CacheReliabilityTelemetry.RecordDistributedInvalidation(
            TimeSpan.FromMilliseconds(34),
            succeeded: false);

        var durations = capture.DoubleMeasurements
            .Where(item => item.Name == "fullnet.cache.invalidation.duration")
            .ToArray();
        Assert.HasCount(2, durations);
        Assert.AreEqual(12d, durations[0].Value, 0.001d);
        Assert.AreEqual(34d, durations[1].Value, 0.001d);
        AssertBoundedTags(durations[0].Tags, "local", "success");
        AssertBoundedTags(durations[1].Tags, "distributed", "failure");

        var failures = capture.LongMeasurements
            .Where(item => item.Name == "fullnet.cache.invalidation.failures")
            .ToArray();
        Assert.HasCount(1, failures);
        Assert.AreEqual(1L, failures[0].Value);
        AssertBoundedTags(failures[0].Tags, "distributed", "failure");

        using var throwingListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        throwingListener.SetMeasurementEventCallback<double>(
            (_, _, _, _) =>
                throw new InvalidOperationException("模拟指标消费者失败。"));
        throwingListener.Start();
        try
        {
            CacheReliabilityTelemetry.RecordLocalInvalidation(
                TimeSpan.FromMilliseconds(1),
                succeeded: true);
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail($"指标消费者失败不得改变缓存语义：{exception.Message}");
        }
    }

    [TestMethod]
    public void Policy_events_use_only_owner_consistency_operation_and_result_tags()
    {
        using var capture = new MetricCapture();

        CacheReliabilityTelemetry.RecordPolicyEvent(
            "tenancy",
            "s1",
            "invalidate_local",
            "success");
        CacheReliabilityTelemetry.RecordPolicyEvent(
            "tenancy",
            "s1",
            "bypass",
            "version_mismatch");

        var events = capture.LongMeasurements
            .Where(item => item.Name == "fullnet.cache.policy.events")
            .ToArray();
        Assert.HasCount(2, events);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "owner_module",
                "consistency_class",
                "operation",
                "result",
            },
            events[0].Tags.Select(tag => tag.Key).ToArray());
        Assert.AreEqual("tenancy", events[0].Tags.Single(tag => tag.Key == "owner_module").Value);
        Assert.AreEqual("s1", events[0].Tags.Single(tag => tag.Key == "consistency_class").Value);
        Assert.AreEqual(
            "invalidate_local",
            events[0].Tags.Single(tag => tag.Key == "operation").Value);
        Assert.AreEqual("success", events[0].Tags.Single(tag => tag.Key == "result").Value);
        Assert.AreEqual(
            "version_mismatch",
            events[1].Tags.Single(tag => tag.Key == "result").Value);
    }

    [TestMethod]
    public void Recovery_related_policy_events_stay_low_cardinality()
    {
        using var capture = new MetricCapture();

        CacheReliabilityTelemetry.RecordPolicyEvent(
            "tenancy",
            "s1",
            "invalidate_after_commit",
            "success");
        CacheReliabilityTelemetry.RecordPolicyEvent(
            "tenancy",
            "s0_l2",
            "bypass",
            "authority_refill");
        CacheReliabilityTelemetry.RecordLocalInvalidation(
            TimeSpan.FromMilliseconds(2),
            succeeded: true);
        CacheReliabilityTelemetry.RecordDistributedInvalidation(
            TimeSpan.FromMilliseconds(8),
            succeeded: false);

        var policyEvents = capture.LongMeasurements
            .Where(item => item.Name == "fullnet.cache.policy.events")
            .ToArray();
        Assert.HasCount(2, policyEvents);
        Assert.IsTrue(
            policyEvents.All(item =>
                item.Tags.Select(tag => tag.Key)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .SequenceEqual(
                    [
                        "consistency_class",
                        "operation",
                        "owner_module",
                        "result",
                    ])));

        var durations = capture.DoubleMeasurements
            .Where(item => item.Name == "fullnet.cache.invalidation.duration")
            .ToArray();
        Assert.IsTrue(
            durations.All(item =>
                item.Tags.Select(tag => tag.Key)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .SequenceEqual(["outcome", "scope"])));
    }

    [TestMethod]
    public void Fusion_events_record_stale_hits_and_backplane_recovery()
    {
        using var capture = new MetricCapture();
        var monitor = new FusionCacheReliabilityMonitor(
            Substitute.For<IFusionCache>());

        monitor.HandleHit(
            sender: null,
            new FusionCacheEntryHitEventArgs("tenant-key", isStale: true));
        monitor.HandleHit(
            sender: null,
            new FusionCacheEntryHitEventArgs("tenant-key", isStale: false));
        monitor.HandleBackplaneCircuitBreakerChange(
            sender: null,
            new FusionCacheCircuitBreakerChangeEventArgs(isClosed: false));
        monitor.HandleBackplaneCircuitBreakerChange(
            sender: null,
            new FusionCacheCircuitBreakerChangeEventArgs(isClosed: true));

        var staleHits = capture.LongMeasurements
            .Where(item => item.Name == "fullnet.cache.hits.stale")
            .ToArray();
        Assert.HasCount(1, staleHits);
        Assert.AreEqual(1L, staleHits[0].Value);
        Assert.HasCount(0, staleHits[0].Tags);

        var transitions = capture.LongMeasurements
            .Where(item =>
                item.Name == "fullnet.cache.backplane.circuit.transitions")
            .ToArray();
        Assert.HasCount(2, transitions);
        CollectionAssert.AreEqual(
            new[] { "open", "closed" },
            transitions
                .Select(item => (string)item.Tags.Single().Value!)
                .ToArray());
        Assert.IsTrue(transitions.All(item => item.Tags.Single().Key == "state"));

        var recoveries = capture.LongMeasurements
            .Where(item => item.Name == "fullnet.cache.backplane.recoveries")
            .ToArray();
        Assert.HasCount(1, recoveries);
        Assert.AreEqual(1L, recoveries[0].Value);
        Assert.HasCount(0, recoveries[0].Tags);
    }

    private static void AssertBoundedTags(
        KeyValuePair<string, object?>[] tags,
        string scope,
        string outcome)
    {
        CollectionAssert.AreEquivalent(
            new[] { "scope", "outcome" },
            tags.Select(tag => tag.Key).ToArray());
        Assert.AreEqual(scope, tags.Single(tag => tag.Key == "scope").Value);
        Assert.AreEqual(outcome, tags.Single(tag => tag.Key == "outcome").Value);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CacheReliabilityTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) =>
                    DoubleMeasurements.Add(
                        new DoubleMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                    LongMeasurements.Add(
                        new LongMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.Start();
        }

        public List<DoubleMeasurement> DoubleMeasurements { get; } = [];

        public List<LongMeasurement> LongMeasurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record DoubleMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed record LongMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);
}
