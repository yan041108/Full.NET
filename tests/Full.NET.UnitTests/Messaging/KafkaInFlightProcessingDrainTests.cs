using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaInFlightProcessingDrainTests
{
    [TestMethod]
    public async Task Drain_waits_for_inflight_processing_that_finishes_within_timeout()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var drain = KafkaInFlightProcessingDrain.DrainAsync(
            completion.Task,
            TimeSpan.FromSeconds(1));
        Assert.IsFalse(drain.IsCompleted);

        completion.SetResult();

        Assert.IsTrue(await drain);
    }

    [TestMethod]
    public async Task Drain_returns_false_when_handler_ignores_cancellation_past_timeout()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var drained = await KafkaInFlightProcessingDrain.DrainAsync(
            completion.Task,
            TimeSpan.FromMilliseconds(20));

        Assert.IsFalse(drained);
        completion.SetCanceled();
    }
}
