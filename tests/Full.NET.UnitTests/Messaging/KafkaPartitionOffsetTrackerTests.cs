using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaPartitionOffsetTrackerTests
{
    [TestMethod]
    public void Completion_does_not_cross_an_earlier_unfinished_delivery()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 0);
        var tracker = new KafkaPartitionOffsetTracker();
        tracker.Assign(partition, assignmentEpoch: 5);
        tracker.Track(new TopicPartitionOffset(partition, 10), assignmentEpoch: 5);
        tracker.Track(new TopicPartitionOffset(partition, 12), assignmentEpoch: 5);

        var later = tracker.Complete(
            new TopicPartitionOffset(partition, 12),
            assignmentEpoch: 5,
            shouldCommit: true);
        var earlier = tracker.Complete(
            new TopicPartitionOffset(partition, 10),
            assignmentEpoch: 5,
            shouldCommit: true);

        Assert.IsNull(later.CommitOffset);
        Assert.AreEqual(new TopicPartitionOffset(partition, 13), earlier.CommitOffset);
        Assert.IsNull(earlier.RetryOffset);
    }

    [TestMethod]
    public void Failed_delivery_blocks_and_discards_later_success_watermark()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 1);
        var tracker = new KafkaPartitionOffsetTracker();
        tracker.Assign(partition, assignmentEpoch: 3);
        tracker.Track(new TopicPartitionOffset(partition, 21), assignmentEpoch: 3);
        tracker.Track(new TopicPartitionOffset(partition, 22), assignmentEpoch: 3);
        _ = tracker.Complete(
            new TopicPartitionOffset(partition, 22),
            assignmentEpoch: 3,
            shouldCommit: true);

        var failed = tracker.Complete(
            new TopicPartitionOffset(partition, 21),
            assignmentEpoch: 3,
            shouldCommit: false);

        Assert.IsNull(failed.CommitOffset);
        Assert.AreEqual(new TopicPartitionOffset(partition, 21), failed.RetryOffset);
    }

    [TestMethod]
    public void Completion_from_revoked_epoch_is_ignored()
    {
        var partition = new TopicPartition("fullnet.test.events.v1", 2);
        var tracker = new KafkaPartitionOffsetTracker();
        tracker.Assign(partition, assignmentEpoch: 8);
        tracker.Track(new TopicPartitionOffset(partition, 30), assignmentEpoch: 8);
        tracker.Revoke(partition, assignmentEpoch: 8);
        tracker.Assign(partition, assignmentEpoch: 9);

        var stale = tracker.Complete(
            new TopicPartitionOffset(partition, 30),
            assignmentEpoch: 8,
            shouldCommit: true);

        Assert.IsTrue(stale.IsStale);
        Assert.IsNull(stale.CommitOffset);
    }
}
