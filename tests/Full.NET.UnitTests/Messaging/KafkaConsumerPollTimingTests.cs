using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerPollTimingTests
{
    [TestMethod]
    public void Active_partition_uses_short_completion_poll_interval()
    {
        var options = new KafkaMessagingOptions
        {
            HandlerHeartbeatMilliseconds = 250,
            CompletionPollMilliseconds = 5,
        };

        Assert.AreEqual(
            TimeSpan.FromMilliseconds(5),
            KafkaConsumerPollTiming.Resolve(
                options,
                inFlightCount: 1,
                hasPendingCompletion: false));
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(250),
            KafkaConsumerPollTiming.Resolve(
                options,
                inFlightCount: 0,
                hasPendingCompletion: false));
    }

    [TestMethod]
    public void Pending_completion_keeps_the_consumer_command_loop_responsive()
    {
        var options = new KafkaMessagingOptions
        {
            HandlerHeartbeatMilliseconds = 250,
            CompletionPollMilliseconds = 5,
        };

        Assert.AreEqual(
            TimeSpan.FromMilliseconds(5),
            KafkaConsumerPollTiming.Resolve(
                options,
                inFlightCount: 0,
                hasPendingCompletion: true));
    }

    [TestMethod]
    public void Completion_poll_interval_must_be_positive_and_not_exceed_heartbeat_interval()
    {
        var options = new KafkaMessagingOptions
        {
            Enabled = true,
            CompletionPollMilliseconds = 0,
        };

        var result = KafkaMessagingOptionsValidation.Validate(options, "Development");

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "CompletionPollMilliseconds");
    }
}
