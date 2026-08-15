using System.Security.Cryptography;
using Full.NET.Benchmarks.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityMeasurementTests
{
    [TestMethod]
    public void Envelope_round_trips_fixed_fields_and_full_SHA256_hash()
    {
        var encoded = KafkaCapacityEnvelopeCodec.Encode(
            payloadSizeBytes: 256,
            runHash: 17,
            sampleHash: 23,
            globalSequence: 41,
            partitionSequence: 7,
            scheduledTimestamp: 100,
            enqueuedTimestamp: 120);

        Assert.AreEqual(256, encoded.Length);
        Assert.IsTrue(KafkaCapacityEnvelopeCodec.TryDecode(encoded, out var envelope));
        Assert.AreEqual(17U, envelope.RunHash);
        Assert.AreEqual(23U, envelope.SampleHash);
        Assert.AreEqual(41L, envelope.GlobalSequence);
        Assert.AreEqual(7L, envelope.PartitionSequence);
        Assert.AreEqual(100L, envelope.ScheduledTimestamp);
        Assert.AreEqual(120L, envelope.EnqueuedTimestamp);
        CollectionAssert.AreEqual(
            encoded,
            KafkaCapacityEnvelopeCodec.Encode(256, 17, 23, 41, 7, 100, 120));
        CollectionAssert.AreEqual(
            SHA256.HashData(encoded.AsSpan(0, encoded.Length - 32)),
            encoded[^32..]);
        Assert.IsFalse(KafkaCapacityEnvelopeCodec.TryDecode(encoded.AsSpan(0, 255), out _));

        encoded[100] ^= 0x5A;
        Assert.IsFalse(KafkaCapacityEnvelopeCodec.TryDecode(encoded, out _));
    }

    [TestMethod]
    public void Envelope_supports_minimum_payload_and_rejects_unrepresentable_values()
    {
        var encoded = KafkaCapacityEnvelopeCodec.Encode(64, 1, 2, 3, 4, 5, 6);

        Assert.IsTrue(KafkaCapacityEnvelopeCodec.TryDecode(encoded, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityEnvelopeCodec.Encode(63, 1, 2, 3, 4, 5, 6));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityEnvelopeCodec.Encode(64, 1, 2, 3, 4, 5, 20_000_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityEnvelopeCodec.Encode(
                64,
                1,
                2,
                3,
                4,
                long.MaxValue,
                long.MinValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            KafkaCapacityEnvelopeCodec.Encode(64, 1, 2, 3, 4, -1, 0));
    }

    [TestMethod]
    public void Latency_histogram_tracks_quantiles_within_one_percent()
    {
        var histogram = new KafkaCapacityLatencyHistogram();
        foreach (var value in Enumerable.Range(1, 10_000))
        {
            histogram.RecordMicroseconds(value);
        }

        var snapshot = histogram.Snapshot();

        Assert.AreEqual(10_000L, snapshot.Count);
        Assert.AreEqual(1L, snapshot.MinimumMicroseconds);
        Assert.AreEqual(10_000L, snapshot.MaximumMicroseconds);
        Assert.IsTrue(RelativeError(snapshot.P50Microseconds, 5_000) <= 0.01d);
        Assert.IsTrue(RelativeError(snapshot.P95Microseconds, 9_500) <= 0.01d);
        Assert.IsTrue(RelativeError(snapshot.P99Microseconds, 9_900) <= 0.01d);
        Assert.IsTrue(snapshot.IsValid);
    }

    [TestMethod]
    public void Latency_histogram_records_concurrently_merges_and_invalidates_overflow()
    {
        var first = new KafkaCapacityLatencyHistogram();
        Parallel.For(0, 10_000, index =>
            first.RecordMicroseconds((index % 1_000) + 1));
        var second = new KafkaCapacityLatencyHistogram();
        second.RecordMicroseconds(KafkaCapacityLatencyHistogram.MaximumMicroseconds);
        second.RecordMicroseconds(0);
        second.RecordMicroseconds(KafkaCapacityLatencyHistogram.MaximumMicroseconds + 1);

        first.Merge(second);
        var snapshot = first.Snapshot();

        Assert.AreEqual(10_001L, snapshot.Count);
        Assert.AreEqual(2L, snapshot.OverflowCount);
        Assert.AreEqual(
            KafkaCapacityLatencyHistogram.MaximumMicroseconds,
            snapshot.MaximumMicroseconds);
        Assert.IsFalse(snapshot.IsValid);
    }

    [TestMethod]
    public void Integrity_tracker_accepts_only_a_perfectly_drained_sample()
    {
        var tracker = new KafkaCapacityIntegrityTracker(
            maximumMessages: 4,
            partitionCount: 2);
        EnqueueAndAcknowledge(tracker, 0);
        EnqueueAndAcknowledge(tracker, 1);
        tracker.OnConsumed(0, partition: 0, partitionSequence: 0, payloadValid: true);
        tracker.OnConsumed(1, partition: 1, partitionSequence: 0, payloadValid: true);

        var evidence = tracker.Complete(drainCompleted: true);

        Assert.AreEqual(2L, evidence.Enqueued);
        Assert.AreEqual(2L, evidence.Acknowledged);
        Assert.AreEqual(2L, evidence.Consumed);
        Assert.IsTrue(evidence.CorrectnessPassed);
    }

    [TestMethod]
    public void Integrity_tracker_detects_loss_duplicate_corruption_order_and_unflushed_messages()
    {
        var tracker = new KafkaCapacityIntegrityTracker(
            maximumMessages: 8,
            partitionCount: 1);
        EnqueueAndAcknowledge(tracker, 0);
        EnqueueAndAcknowledge(tracker, 1);
        tracker.OnEnqueued(2);
        tracker.OnConsumed(0, 0, 1, payloadValid: true);
        tracker.OnConsumed(0, 0, 1, payloadValid: true);
        tracker.OnConsumed(1, 0, 0, payloadValid: false);

        var evidence = tracker.Complete(drainCompleted: true);

        Assert.AreEqual(1L, evidence.Duplicate);
        Assert.AreEqual(1L, evidence.Corrupted);
        Assert.IsGreaterThan(0L, evidence.OutOfOrder);
        Assert.AreEqual(1L, evidence.Unflushed);
        Assert.IsFalse(evidence.CorrectnessPassed);
    }

    [TestMethod]
    public void Integrity_tracker_detects_acknowledged_loss_and_incomplete_drain()
    {
        var tracker = new KafkaCapacityIntegrityTracker(2, 1);
        EnqueueAndAcknowledge(tracker, 0);

        var evidence = tracker.Complete(drainCompleted: false);

        Assert.AreEqual(1L, evidence.Lost);
        Assert.IsFalse(evidence.DrainCompleted);
        Assert.IsFalse(evidence.CorrectnessPassed);
    }

    [TestMethod]
    public void Integrity_tracker_fails_closed_on_out_of_range_sequence()
    {
        var tracker = new KafkaCapacityIntegrityTracker(2, 1);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            tracker.OnEnqueued(2));

        var evidence = tracker.Complete(drainCompleted: true);
        Assert.AreEqual(1L, evidence.InvalidSequence);
        Assert.IsFalse(evidence.CorrectnessPassed);
    }

    private static void EnqueueAndAcknowledge(
        KafkaCapacityIntegrityTracker tracker,
        long sequence)
    {
        tracker.OnEnqueued(sequence);
        tracker.OnAcknowledged(sequence);
    }

    private static double RelativeError(long actual, long expected) =>
        Math.Abs(actual - expected) / (double)expected;
}
