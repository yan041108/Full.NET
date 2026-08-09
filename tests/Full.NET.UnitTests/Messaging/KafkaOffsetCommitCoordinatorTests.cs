using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaOffsetCommitCoordinatorTests
{
    [TestMethod]
    public void Per_message_mode_exposes_safe_watermark_immediately()
    {
        var now = DateTimeOffset.UtcNow;
        var partition = new TopicPartition("fullnet.test.events.v1", 0);
        var coordinator = CreateCoordinator(KafkaOffsetCommitMode.PerMessage);

        coordinator.Offer(new TopicPartitionOffset(partition, 11));

        var ready = coordinator.GetReady(now);
        Assert.HasCount(1, ready);
        Assert.AreEqual(new TopicPartitionOffset(partition, 11), ready[0]);
    }

    [TestMethod]
    public void Periodic_mode_coalesces_to_latest_safe_watermark_per_partition()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new TopicPartition("fullnet.test.events.v1", 0);
        var second = new TopicPartition("fullnet.test.events.v1", 1);
        var coordinator = CreateCoordinator(
            KafkaOffsetCommitMode.PeriodicWatermark,
            interval: TimeSpan.FromSeconds(1),
            batchSize: 3);

        coordinator.Offer(new TopicPartitionOffset(first, 11));
        coordinator.Offer(new TopicPartitionOffset(first, 12));
        Assert.IsEmpty(coordinator.GetReady(now));

        coordinator.Offer(new TopicPartitionOffset(second, 7));
        var ready = coordinator.GetReady(now);

        Assert.HasCount(2, ready);
        CollectionAssert.Contains(ready.ToArray(), new TopicPartitionOffset(first, 12));
        CollectionAssert.Contains(ready.ToArray(), new TopicPartitionOffset(second, 7));
    }

    [TestMethod]
    public void Periodic_mode_flushes_on_interval_and_only_acknowledges_committed_watermarks()
    {
        var now = DateTimeOffset.UtcNow;
        var partition = new TopicPartition("fullnet.test.events.v1", 2);
        var coordinator = CreateCoordinator(
            KafkaOffsetCommitMode.PeriodicWatermark,
            interval: TimeSpan.FromSeconds(1),
            initialUtc: now);
        coordinator.Offer(new TopicPartitionOffset(partition, 21));

        Assert.IsEmpty(coordinator.GetReady(now.AddMilliseconds(999)));
        var ready = coordinator.GetReady(now.AddSeconds(1));
        Assert.HasCount(1, ready);

        coordinator.RecordFailure(now.AddSeconds(1));
        Assert.AreEqual(1, coordinator.PendingPartitionCount);
        Assert.IsEmpty(coordinator.GetReady(now.AddMilliseconds(1_999)));

        var retry = coordinator.GetReady(now.AddSeconds(2));
        coordinator.Acknowledge(retry, now.AddSeconds(2));
        Assert.AreEqual(0, coordinator.PendingPartitionCount);
    }

    [TestMethod]
    public void Rebalance_force_flush_returns_only_requested_partitions()
    {
        var now = DateTimeOffset.UtcNow;
        var revoked = new TopicPartition("fullnet.test.events.v1", 3);
        var retained = new TopicPartition("fullnet.test.events.v1", 4);
        var coordinator = CreateCoordinator(KafkaOffsetCommitMode.PeriodicWatermark);
        coordinator.Offer(new TopicPartitionOffset(revoked, 31));
        coordinator.Offer(new TopicPartitionOffset(retained, 41));

        var ready = coordinator.GetReadyForPartitions([revoked], now);

        Assert.HasCount(1, ready);
        Assert.AreEqual(new TopicPartitionOffset(revoked, 31), ready[0]);
        coordinator.Acknowledge(ready, now);
        Assert.AreEqual(1, coordinator.PendingPartitionCount);
    }

    [TestMethod]
    public void Lost_partitions_are_discarded_without_becoming_committable()
    {
        var now = DateTimeOffset.UtcNow;
        var lost = new TopicPartition("fullnet.test.events.v1", 5);
        var coordinator = CreateCoordinator(KafkaOffsetCommitMode.PeriodicWatermark);
        coordinator.Offer(new TopicPartitionOffset(lost, 51));

        coordinator.Discard([lost]);

        Assert.IsEmpty(coordinator.GetReady(now, force: true));
        Assert.AreEqual(0, coordinator.PendingPartitionCount);
    }

    [TestMethod]
    public void Per_message_commit_failure_uses_bounded_retry_interval()
    {
        var now = DateTimeOffset.UtcNow;
        var partition = new TopicPartition("fullnet.test.events.v1", 6);
        var coordinator = CreateCoordinator(
            KafkaOffsetCommitMode.PerMessage,
            interval: TimeSpan.FromSeconds(1));
        coordinator.Offer(new TopicPartitionOffset(partition, 61));
        Assert.HasCount(1, coordinator.GetReady(now));

        coordinator.RecordFailure(now);

        Assert.IsEmpty(coordinator.GetReady(now.AddMilliseconds(999)));
        Assert.HasCount(1, coordinator.GetReady(now.AddSeconds(1)));
    }

    private static KafkaOffsetCommitCoordinator CreateCoordinator(
        KafkaOffsetCommitMode mode,
        TimeSpan? interval = null,
        int batchSize = 100,
        DateTimeOffset? initialUtc = null) =>
        new(
            mode,
            interval ?? TimeSpan.FromSeconds(1),
            batchSize,
            initialUtc ?? DateTimeOffset.UtcNow);
}
