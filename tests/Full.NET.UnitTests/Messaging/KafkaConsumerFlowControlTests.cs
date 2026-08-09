using Confluent.Kafka;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaConsumerFlowControlTests
{
    [TestMethod]
    public async Task Long_handler_keeps_polling_while_assignment_is_paused_then_commits()
    {
        var topicPartition = new TopicPartition("fullnet.test.events.v1", 0);
        var consumeResult = CreateConsumeResult(topicPartition, 17);
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Assignment.Returns([topicPartition]);
        consumer.Consume(Arg.Any<TimeSpan>()).Returns((ConsumeResult<string, byte[]>?)null);
        var processing = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var flow = Task.Run(() => KafkaConsumerFlowControl.ProcessAsync(
            consumer,
            consumeResult,
            CreateOptions(),
            NullLogger.Instance,
            () => processing.Task,
            CancellationToken.None));
        await WaitUntilAsync(
            () => consumer.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IConsumer<string, byte[]>.Consume)));

        processing.SetResult(true);
        await flow;

        consumer.Received().Pause(Arg.Is<IEnumerable<TopicPartition>>(
            items => items != null && items.Contains(topicPartition)));
        consumer.Received().Commit(consumeResult);
        consumer.Received().Resume(Arg.Is<IEnumerable<TopicPartition>>(
            items => items != null && items.Contains(topicPartition)));
        consumer.DidNotReceiveWithAnyArgs().Seek(default);
    }

    [TestMethod]
    public async Task Uncommitted_result_seeks_current_offset_before_resuming()
    {
        var topicPartition = new TopicPartition("fullnet.test.events.v1", 0);
        var consumeResult = CreateConsumeResult(topicPartition, 23);
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        consumer.Assignment.Returns([topicPartition]);
        consumer.Consume(Arg.Any<TimeSpan>()).Returns((ConsumeResult<string, byte[]>?)null);

        await KafkaConsumerFlowControl.ProcessAsync(
            consumer,
            consumeResult,
            CreateOptions(),
            NullLogger.Instance,
            () => Task.FromResult(false),
            CancellationToken.None);

        consumer.Received().Seek(consumeResult.TopicPartitionOffset);
        consumer.DidNotReceiveWithAnyArgs().Commit(default(ConsumeResult<string, byte[]>)!);
    }

    private static KafkaMessagingOptions CreateOptions() =>
        new()
        {
            HandlerHeartbeatMilliseconds = 10,
            UncommittedRetryBackoffMilliseconds = 100,
            ShutdownDrainSeconds = 1,
        };

    private static ConsumeResult<string, byte[]> CreateConsumeResult(
        TopicPartition topicPartition,
        long offset) =>
        new()
        {
            Topic = topicPartition.Topic,
            Partition = topicPartition.Partition,
            Offset = offset,
            Message = new Message<string, byte[]>
            {
                Key = "aggregate-1",
                Value = [0x01],
                Headers = new Headers(),
            },
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }
}
