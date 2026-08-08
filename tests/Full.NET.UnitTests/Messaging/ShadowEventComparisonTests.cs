using Full.NET.Messaging.Abstractions;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class ShadowEventComparisonTests
{
    [TestMethod]
    public void CompareExpectedToObserved_returns_match_for_identical_fingerprints()
    {
        var payload = new byte[] { 0x10, 0x20 };
        var fingerprint = ShadowEventFingerprint.Create(
            Guid.CreateVersion7(),
            "fullnet.messaging.shadow.test.event",
            1,
            "partition-a",
            payload,
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"));

        var comparator = new ShadowEventComparator();
        var result = comparator.CompareExpectedToObserved(
            fingerprint,
            fingerprint,
            new ShadowSourcePosition("kafka", "topic:0", 1));

        Assert.IsTrue(result.IsMatch);
    }

    [TestMethod]
    public void CompareExpectedToObserved_detects_partition_key_mismatch()
    {
        var eventId = Guid.CreateVersion7();
        var expected = ShadowEventFingerprint.Create(
            eventId,
            "fullnet.messaging.shadow.test.event",
            1,
            "partition-a",
            [0x01],
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"));
        var observed = ShadowEventFingerprint.Create(
            eventId,
            "fullnet.messaging.shadow.test.event",
            1,
            "partition-b",
            [0x01],
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"));

        var comparator = new ShadowEventComparator();
        var result = comparator.CompareExpectedToObserved(expected, observed, null);

        Assert.AreEqual(ShadowComparisonOutcome.FieldMismatch, result.Outcome);
        Assert.AreEqual(nameof(ShadowEventFingerprint.PartitionKey), result.MismatchField);
    }

    [TestMethod]
    public void ValidateMonotonicPosition_rejects_non_increasing_sequence()
    {
        var comparator = new ShadowEventComparator();
        var previous = new ShadowSourcePosition("sqlserver", "fullnet_fn_messaging_outbox_event", 100);
        var current = new ShadowSourcePosition("sqlserver", "fullnet_fn_messaging_outbox_event", 99);
        var result = comparator.ValidateMonotonicPosition(previous, current);

        Assert.AreEqual(ShadowComparisonOutcome.PositionRegression, result.Outcome);
    }
}
