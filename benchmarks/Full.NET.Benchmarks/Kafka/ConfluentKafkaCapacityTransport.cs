using Confluent.Kafka;
using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 使用生产 Kafka 配置构建线程安全的 Confluent Producer。
/// </summary>
public sealed class ConfluentKafkaCapacityProducerFactory
    : IKafkaCapacityProducerFactory
{
    public IKafkaCapacityProducer Create(
        KafkaMessagingOptions options,
        Action<string> statisticsHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(statisticsHandler);
        var config = options.BuildProducerConfig();
        config.StatisticsIntervalMs = 1_000;
        var producer = new ProducerBuilder<string, byte[]>(config)
            .SetStatisticsHandler((_, json) => statisticsHandler(json))
            .Build();
        return new ConfluentKafkaCapacityProducer(producer);
    }

    private sealed class ConfluentKafkaCapacityProducer(
        IProducer<string, byte[]> producer) : IKafkaCapacityProducer
    {
        public void Produce(
            string topicName,
            int partition,
            string key,
            byte[] value,
            long globalSequence,
            Action<KafkaCapacityDeliveryReport> deliveryHandler)
        {
            producer.Produce(
                new TopicPartition(topicName, new Partition(partition)),
                new Message<string, byte[]> { Key = key, Value = value },
                report => deliveryHandler(new KafkaCapacityDeliveryReport(
                    globalSequence,
                    !report.Error.IsError
                    && report.Status == PersistenceStatus.Persisted,
                    report.Error.IsError ? report.Error.Code.ToString() : null)));
        }

        public int Flush(TimeSpan timeout) => producer.Flush(timeout);

        public ValueTask DisposeAsync()
        {
            producer.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// 使用生产 Kafka 配置构建每样本独立 Group 的专用 Poll Consumer。
/// </summary>
public sealed class ConfluentKafkaCapacityConsumerFactory
    : IKafkaCapacityConsumerFactory
{
    public IKafkaCapacityConsumer Create(
        KafkaMessagingOptions options,
        string consumerGroupId,
        Action<string> statisticsHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroupId);
        ArgumentNullException.ThrowIfNull(statisticsHandler);
        var config = options.BuildConsumerConfig(consumerGroupId);
        config.StatisticsIntervalMs = 1_000;
        return new ConfluentKafkaCapacityConsumer(config, statisticsHandler);
    }

    private sealed class ConfluentKafkaCapacityConsumer
        : IKafkaCapacityConsumer
    {
        private readonly CancellationTokenSource stopCancellation = new();
        private readonly TaskCompletionSource assigned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IConsumer<string, byte[]> consumer;
        private Task? pollingTask;
        private Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask>?
            messageHandler;

        public ConfluentKafkaCapacityConsumer(
            ConsumerConfig config,
            Action<string> statisticsHandler)
        {
            consumer = new ConsumerBuilder<string, byte[]>(config)
                .SetPartitionsAssignedHandler((_, _) => assigned.TrySetResult())
                .SetStatisticsHandler((_, json) => statisticsHandler(json))
                .Build();
        }

        public Task Completion => pollingTask ?? Task.CompletedTask;

        public Task StartAsync(
            string topicName,
            Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask> handler,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
            ArgumentNullException.ThrowIfNull(handler);
            if (pollingTask is not null)
            {
                throw new InvalidOperationException(
                    "Kafka capacity consumer has already started.");
            }

            messageHandler = handler;
            consumer.Subscribe(topicName);
            pollingTask = Task.Factory.StartNew(
                Poll,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return Task.CompletedTask;
        }

        public async Task WaitForAssignmentAsync(
            CancellationToken cancellationToken) =>
            await assigned.Task.WaitAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            stopCancellation.Cancel();
            if (pollingTask is not null)
            {
                await pollingTask.WaitAsync(cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            stopCancellation.Cancel();
            if (pollingTask is not null)
            {
                try
                {
                    await pollingTask;
                }
                catch
                {
                    // 主执行链已经观察 Completion；释放阶段只保证 native 句柄关闭。
                }
            }

            consumer.Dispose();
            stopCancellation.Dispose();
        }

        private void Poll()
        {
            try
            {
                while (!stopCancellation.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]> result;
                    try
                    {
                        result = consumer.Consume(stopCancellation.Token);
                    }
                    catch (OperationCanceledException)
                        when (stopCancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    if (result.IsPartitionEOF)
                    {
                        continue;
                    }

                    messageHandler!(new KafkaCapacityConsumedMessage(
                            result.Partition.Value,
                            result.Message.Value),
                            stopCancellation.Token)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
            }
            finally
            {
                consumer.Close();
            }
        }
    }
}
