using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class PartitionDeliveryWatermarkTests
{
    [TestMethod]
    public void LaterSuccessCannotSkipEarlierRetryEvenWhenNumericOffsetsHaveGaps()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 7, maxPending: 3);
        Assert.IsTrue(watermark.TryTrack(10, 7));
        Assert.IsTrue(watermark.TryTrack(12, 7));

        Assert.IsTrue(watermark.TryApplySinkResult(12, 7, BulkItemOutcome.Succeeded));
        Assert.IsNull(watermark.GetSafeNextOffset(7));
        Assert.IsTrue(watermark.TryApplySinkResult(10, 7, BulkItemOutcome.Retry));
        Assert.IsNull(watermark.GetSafeNextOffset(7));

        Assert.IsTrue(watermark.TryApplySinkResult(10, 7, BulkItemOutcome.Succeeded));
        Assert.AreEqual(13L, watermark.GetSafeNextOffset(7));
    }

    [TestMethod]
    public void PermanentBulkFailureNeedsSeparateDurableIsolationAck()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 3, maxPending: 2);
        Assert.IsTrue(watermark.TryTrack(20, 3));
        Assert.IsTrue(watermark.TryTrack(21, 3));

        Assert.IsFalse(watermark.TryConfirmIsolation(20, 3));
        Assert.IsTrue(watermark.TryApplySinkResult(20, 3, BulkItemOutcome.Isolate));
        Assert.IsTrue(watermark.TryApplySinkResult(21, 3, BulkItemOutcome.Succeeded));
        Assert.IsNull(watermark.GetSafeNextOffset(3));

        Assert.IsTrue(watermark.TryConfirmIsolation(20, 3));
        Assert.AreEqual(22L, watermark.GetSafeNextOffset(3));
    }

    [TestMethod]
    public void BrokerCommitFailureKeepsSafeWatermarkForRetry()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 1, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(30, 1));
        Assert.IsTrue(watermark.TryApplySinkResult(30, 1, BulkItemOutcome.Succeeded));

        Assert.AreEqual(31L, watermark.GetSafeNextOffset(1));
        Assert.IsNull(watermark.GetCommittedNextOffset(1));
        Assert.AreEqual(31L, watermark.GetSafeNextOffset(1));
        Assert.IsFalse(watermark.TryConfirmCommit(32, 1));
        Assert.IsTrue(watermark.TryConfirmCommit(31, 1));
        Assert.AreEqual(31L, watermark.GetCommittedNextOffset(1));
    }

    [TestMethod]
    public void RevokedEpochCannotAcceptLateAckOrExposeOldCommitWatermark()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 9, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(40, 9));
        Assert.IsTrue(watermark.TryApplySinkResult(40, 9, BulkItemOutcome.Isolate));
        Assert.IsTrue(watermark.TryRevoke(9));

        Assert.IsFalse(watermark.TryConfirmIsolation(40, 9));
        Assert.IsFalse(watermark.TryApplySinkResult(40, 9, BulkItemOutcome.Succeeded));
        Assert.IsFalse(watermark.TryConfirmCommit(41, 9));
        Assert.IsNull(watermark.GetSafeNextOffset(9));
    }

    [TestMethod]
    public void PendingBudgetAndObservationOrderAreBounded()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 1, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(100, 1));
        Assert.IsFalse(watermark.TryTrack(101, 1));
        Assert.IsTrue(watermark.TryApplySinkResult(100, 1, BulkItemOutcome.Succeeded));
        Assert.IsTrue(watermark.TryTrack(101, 1));
    }

    [TestMethod]
    public void InvalidOffsetsCannotCorruptSafeWatermark()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 2, maxPending: 1);
        Assert.IsFalse(watermark.TryTrack(long.MaxValue, 2));
        Assert.IsTrue(watermark.TryTrack(8, 2));
        Assert.IsTrue(watermark.TryApplySinkResult(8, 2, BulkItemOutcome.Succeeded));
        Assert.IsFalse(watermark.TryConfirmCommit(-1, 2));
        Assert.AreEqual(9L, watermark.GetSafeNextOffset(2));
        Assert.IsNull(watermark.GetCommittedNextOffset(2));
    }

    [TestMethod]
    public void AwaitingIsolationCannotBeOverwrittenByLaterSinkCallback()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 5, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(50, 5));
        Assert.IsTrue(watermark.TryApplySinkResult(50, 5, BulkItemOutcome.Isolate));

        Assert.IsFalse(watermark.TryApplySinkResult(50, 5, BulkItemOutcome.Succeeded));
        Assert.IsFalse(watermark.TryApplySinkResult(50, 5, BulkItemOutcome.Retry));
        Assert.IsNull(watermark.GetSafeNextOffset(5));
        Assert.IsTrue(watermark.TryConfirmIsolation(50, 5));
        Assert.AreEqual(51L, watermark.GetSafeNextOffset(5));
    }

    [TestMethod]
    public void CapacityRejectedDeliveryMustBeRetriedBeforeLaterOffset()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 1, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(100, 1));
        Assert.IsFalse(watermark.TryTrack(101, 1));
        Assert.IsTrue(watermark.TryApplySinkResult(100, 1, BulkItemOutcome.Succeeded));

        Assert.AreEqual(101L, watermark.GetSafeNextOffset(1));
        Assert.IsTrue(watermark.TryTrack(101, 1));
        Assert.IsTrue(watermark.TryApplySinkResult(101, 1, BulkItemOutcome.Succeeded));
        Assert.IsTrue(watermark.TryTrack(102, 1));
        Assert.IsTrue(watermark.TryApplySinkResult(102, 1, BulkItemOutcome.Succeeded));
        Assert.AreEqual(103L, watermark.GetSafeNextOffset(1));
    }

    [TestMethod]
    public void NonMonotonicDeliveryInvalidatesAssignmentUntilRecreated()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 4, maxPending: 2);
        Assert.IsTrue(watermark.TryTrack(100, 4));
        Assert.IsFalse(watermark.TryTrack(99, 4));
        Assert.IsFalse(watermark.TryApplySinkResult(100, 4, BulkItemOutcome.Succeeded));
        Assert.IsFalse(watermark.TryTrack(101, 4));
        Assert.IsNull(watermark.GetSafeNextOffset(4));
        Assert.IsTrue(watermark.TryRevoke(4));
    }

    [TestMethod]
    public void NewDeliveryWhilePreviousCapacityRejectionIsUnresolvedInvalidatesAssignment()
    {
        var watermark = new PartitionDeliveryWatermark(assignmentEpoch: 6, maxPending: 1);
        Assert.IsTrue(watermark.TryTrack(100, 6));
        Assert.IsFalse(watermark.TryTrack(101, 6));
        Assert.IsFalse(watermark.TryTrack(102, 6));

        Assert.IsFalse(watermark.TryApplySinkResult(100, 6, BulkItemOutcome.Succeeded));
        Assert.IsFalse(watermark.TryTrack(101, 6));
        Assert.IsNull(watermark.GetSafeNextOffset(6));
        Assert.IsTrue(watermark.TryRevoke(6));
    }
}
