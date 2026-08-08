using Full.NET.Messaging.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class CdcCrashPointTests
{
    [TestMethod]
    public void Duplicate_shadow_delivery_is_classified_without_invoking_handlers()
    {
        var payload = new byte[] { 0x31, 0x32 };
        var fingerprint = ShadowEventFingerprint.Create(
            Guid.CreateVersion7(),
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            "crash-duplicate",
            payload,
            DateTimeOffset.Parse("2026-08-08T08:00:00Z"));

        var comparator = new ShadowEventComparator();
        var position = new ShadowSourcePosition("kafka", "fullnet.dev.shadow.test:0", 7);
        var first = comparator.CompareExpectedToObserved(
            fingerprint,
            fingerprint,
            position);
        var duplicate = comparator.CompareExpectedToObserved(
            fingerprint,
            fingerprint,
            position,
            duplicateObserved: true);

        Assert.IsTrue(first.IsMatch);
        Assert.AreEqual(ShadowComparisonOutcome.DuplicateObserved, duplicate.Outcome);
    }

    [TestMethod]
    public void Offset_window_regression_is_detected_for_monotonic_positions()
    {
        var comparator = new ShadowEventComparator();
        var first = new ShadowSourcePosition("kafka", "fullnet.dev.shadow.test:0", 10);
        var second = new ShadowSourcePosition("kafka", "fullnet.dev.shadow.test:0", 11);
        var regression = comparator.ValidateMonotonicPosition(first, second);
        var invalid = comparator.ValidateMonotonicPosition(second, first);

        Assert.IsTrue(regression.IsMatch);
        Assert.AreEqual(ShadowComparisonOutcome.PositionRegression, invalid.Outcome);
    }

    [TestMethod]
    public void Missing_authoritative_outbox_row_is_reported_for_shadow_observation()
    {
        var observed = ShadowEventFingerprint.Create(
            Guid.CreateVersion7(),
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            "crash-missing",
            [0x01],
            DateTimeOffset.Parse("2026-08-08T08:01:00Z"));
        var comparator = new ShadowEventComparator();
        var result = comparator.CompareExpectedToObserved(
            expected: null,
            observed,
            new ShadowSourcePosition("kafka", "fullnet.dev.shadow.test:0", 3));

        Assert.AreEqual(ShadowComparisonOutcome.MissingExpected, result.Outcome);
    }

    [TestMethod]
    public void Same_event_id_with_different_payload_hash_is_payload_mismatch()
    {
        var eventId = Guid.CreateVersion7();
        var expected = ShadowEventFingerprint.Create(
            eventId,
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            "crash-payload",
            [0x01],
            DateTimeOffset.Parse("2026-08-08T08:02:00Z"));
        var observed = ShadowEventFingerprint.Create(
            eventId,
            MessagingOutboxTestSupport.TestEventType,
            MessagingOutboxTestSupport.TestSchemaVersion,
            "crash-payload",
            [0x02],
            DateTimeOffset.Parse("2026-08-08T08:02:00Z"));

        var comparator = new ShadowEventComparator();
        var result = comparator.CompareExpectedToObserved(
            expected,
            observed,
            new ShadowSourcePosition("kafka", "fullnet.dev.shadow.test:0", 4));

        Assert.AreEqual(ShadowComparisonOutcome.PayloadMismatch, result.Outcome);
    }
}
