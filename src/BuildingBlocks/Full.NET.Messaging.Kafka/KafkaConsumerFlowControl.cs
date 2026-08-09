using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 把 Pause、Heartbeat Poll、Commit/Seek 与 Resume 固定在 Consumer 所属线程，
/// Handler 只返回是否可以确认当前 Offset。
/// </summary>
internal static class KafkaConsumerFlowControl
{
    public static async Task ProcessAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> consumeResult,
        KafkaMessagingOptions options,
        ILogger logger,
        Func<Task<bool>> processMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(consumeResult);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(processMessage);

        var paused = new HashSet<TopicPartition>();
        PauseCurrentAssignment();
        try
        {
            if (KafkaDeliveryHeaders.TryReadRetryNotBeforeUtc(
                    consumeResult.Message.Headers,
                    out var retryNotBeforeUtc))
            {
                await PollUntilAsync(retryNotBeforeUtc).ConfigureAwait(false);
            }

            var processing = processMessage();
            try
            {
                while (!processing.IsCompleted)
                {
                    PollHeartbeat();
                }
            }
            catch
            {
                var drained = await KafkaInFlightProcessingDrain
                    .DrainAsync(
                        processing,
                        TimeSpan.FromSeconds(options.ShutdownDrainSeconds))
                    .ConfigureAwait(false);
                if (!drained)
                {
                    logger.LogWarning(
                        "Kafka in-flight processing exceeded the shutdown drain timeout; its eventual fault remains observed.");
                }

                throw;
            }

            var shouldCommit = await processing.ConfigureAwait(false);
            if (shouldCommit)
            {
                try
                {
                    consumer.Commit(consumeResult);
                }
                catch (KafkaException exception) when (!exception.Error.IsFatal)
                {
                    // Inbox 已提交但 Offset 未确认时允许重投；Inbox 唯一键会消除业务副作用。
                    logger.LogWarning(
                        exception,
                        "Kafka offset commit failed; Inbox idempotency protects a later redelivery.");
                }

                return;
            }

            await PollForAsync(
                    TimeSpan.FromMilliseconds(
                        options.UncommittedRetryBackoffMilliseconds))
                .ConfigureAwait(false);
            // 失败消息仍由当前实例持有时必须回退当前位置；否则后续消息提交会越过该
            // Offset。Rebalance 已撤销分区时由新持有者从未提交 Offset 重投。
            if (consumer.Assignment.Contains(consumeResult.TopicPartition))
            {
                consumer.Seek(consumeResult.TopicPartitionOffset);
            }
            else
            {
                logger.LogInformation(
                    "Kafka partition {TopicPartition} was revoked before retry seek; the new owner will redeliver the uncommitted offset.",
                    consumeResult.TopicPartition);
            }
        }
        finally
        {
            var assigned = consumer.Assignment.ToHashSet();
            var resumable = paused.Where(assigned.Contains).ToArray();
            if (resumable.Length > 0)
            {
                consumer.Resume(resumable);
            }
        }

        void PauseCurrentAssignment()
        {
            var newAssignments = consumer.Assignment
                .Where(partition => paused.Add(partition))
                .ToArray();
            if (newAssignments.Length > 0)
            {
                consumer.Pause(newAssignments);
            }
        }

        void PollHeartbeat()
        {
            cancellationToken.ThrowIfCancellationRequested();
            PauseCurrentAssignment();
            try
            {
                var unexpected = consumer.Consume(
                    TimeSpan.FromMilliseconds(
                        options.HandlerHeartbeatMilliseconds));
                if (unexpected?.Message is not null)
                {
                    throw new InvalidOperationException(
                        "Kafka returned another message while all assigned partitions were paused; "
                        + "the current offset remains uncommitted to preserve delivery.");
                }
            }
            catch (ConsumeException exception) when (!exception.Error.IsFatal)
            {
                logger.LogWarning(
                    exception,
                    "Kafka heartbeat poll reported a recoverable error.");
            }
        }

        async Task PollUntilAsync(DateTimeOffset notBeforeUtc)
        {
            while (DateTimeOffset.UtcNow < notBeforeUtc)
            {
                PollHeartbeat();
                await Task.Yield();
            }
        }

        async Task PollForAsync(TimeSpan duration)
        {
            var until = DateTimeOffset.UtcNow.Add(duration);
            await PollUntilAsync(until).ConfigureAwait(false);
        }
    }
}
