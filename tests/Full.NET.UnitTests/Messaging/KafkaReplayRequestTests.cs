using Full.NET.Messaging.Abstractions;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaReplayRequestTests
{
    [TestMethod]
    public void Accepts_bounded_utc_time_range()
    {
        var request = new KafkaReplayRequest(
            "messaging.orders.v1",
            DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
            null,
            null,
            [0, 2],
            "fullnet.messaging.orders",
            10_000,
            "incident replay INC-42");

        Assert.IsTrue(request.UsesTimeRange);
        Assert.IsFalse(request.UsesOffsetRange);
    }

    [TestMethod]
    public void Accepts_bounded_offset_range()
    {
        var request = new KafkaReplayRequest(
            "messaging.orders.v1",
            null,
            null,
            100,
            199,
            [1],
            "fullnet.messaging.orders",
            100,
            "repair projection gap");

        Assert.IsTrue(request.UsesOffsetRange);
    }

    [TestMethod]
    public void Rejects_mixed_or_incomplete_ranges()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new KafkaReplayRequest(
            "messaging.orders.v1",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            1,
            2,
            [0],
            "fullnet.messaging.orders",
            100,
            "invalid mixed range"));
        Assert.ThrowsExactly<ArgumentException>(() => new KafkaReplayRequest(
            "messaging.orders.v1",
            null,
            null,
            1,
            null,
            [0],
            "fullnet.messaging.orders",
            100,
            "incomplete range"));
    }

    [TestMethod]
    public void Rejects_invalid_limits_partitions_consumer_or_reason()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOffsetRequest(maxMessages: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CreateOffsetRequest(maxMessages: 100_001));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOffsetRequest(partitions: [0, 0]));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOffsetRequest(partitions: [-1]));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOffsetRequest(
            partitions: Enumerable.Range(0, KafkaReplayRequest.MaximumPartitions + 1).ToArray()));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOffsetRequest(consumerName: "invalid consumer"));
        Assert.ThrowsExactly<ArgumentException>(() => CreateOffsetRequest(reason: " "));
    }

    private static KafkaReplayRequest CreateOffsetRequest(
        IReadOnlyList<int>? partitions = null,
        string consumerName = "fullnet.messaging.orders",
        int maxMessages = 100,
        string reason = "test replay") =>
        new(
            "messaging.orders.v1",
            null,
            null,
            1,
            10,
            partitions ?? [0],
            consumerName,
            maxMessages,
            reason);
}
