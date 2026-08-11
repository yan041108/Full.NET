using System.Collections.Concurrent;
using System.Diagnostics;
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
        string clientId,
        Action<string> statisticsHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(statisticsHandler);
        var config = options.BuildProducerConfig();
        config.ClientId = clientId;
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
        string clientId,
        int expectedPartitions,
        Action<string> statisticsHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedPartitions);
        ArgumentNullException.ThrowIfNull(statisticsHandler);
        var config = options.BuildConsumerConfig(consumerGroupId);
        // 每个样本都使用从未提交 Offset 的独立 Group，并在 Producer 启动前完成分配；
        // 从当前末端起读可避免反复扫描同一 Run Topic 中先前样本的数据。
        config.AutoOffsetReset = AutoOffsetReset.Latest;
        config.ClientId = clientId;
        config.StatisticsIntervalMs = 1_000;
        return new ConfluentKafkaCapacityConsumer(
            config,
            expectedPartitions,
            TimeSpan.FromMilliseconds(options.DeliveryTimeoutMilliseconds),
            statisticsHandler);
    }

    private sealed class ConfluentKafkaCapacityConsumer
        : IKafkaCapacityConsumer
    {
        private readonly CancellationTokenSource stopCancellation = new();
        private readonly TaskCompletionSource assigned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IConsumer<string, byte[]> consumer;
        private readonly ConcurrentQueue<BacklogRequest> backlogRequests = new();
        private readonly int expectedPartitions;
        private readonly TimeSpan assignmentTimeout;
        private TopicPartition[] assignedPartitions = [];
        private Task? pollingTask;
        private int disposed;
        private Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask>?
            messageHandler;

        public ConfluentKafkaCapacityConsumer(
            ConsumerConfig config,
            int expectedPartitions,
            TimeSpan assignmentTimeout,
            Action<string> statisticsHandler)
        {
            this.expectedPartitions = expectedPartitions;
            this.assignmentTimeout = assignmentTimeout;
            consumer = new ConsumerBuilder<string, byte[]>(config)
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
            var started = Stopwatch.GetTimestamp();
            var assignments = new TopicPartitionOffset[expectedPartitions];
            for (var partition = 0; partition < expectedPartitions; partition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = assignmentTimeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        "Kafka capacity consumer assignment watermark query timed out.");
                }

                var topicPartition = new TopicPartition(
                    topicName,
                    new Partition(partition));
                var watermark = consumer.QueryWatermarkOffsets(
                    topicPartition,
                    remaining);
                assignments[partition] = new TopicPartitionOffset(
                    topicPartition,
                    watermark.High);
            }

            consumer.Assign(assignments);
            assignedPartitions = assignments
                .Select(static assignment => assignment.TopicPartition)
                .ToArray();
            assigned.TrySetResult();
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

        public async Task<KafkaCapacityBrokerBacklogSnapshot> CaptureBacklogAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (pollingTask is null)
            {
                throw new InvalidOperationException(
                    "Kafka capacity consumer has not started.");
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new TimeoutException(
                    "Kafka capacity backlog capture has no remaining budget.");
            }

            var request = new BacklogRequest(timeout);
            backlogRequests.Enqueue(request);
            return await request.Completion.Task.WaitAsync(timeout, cancellationToken);
        }

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
            if (pollingTask is not null && !pollingTask.IsCompleted)
            {
                _ = pollingTask.ContinueWith(
                    static (_, state) =>
                        ((ConfluentKafkaCapacityConsumer)state!).DisposeResources(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return;
            }

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

            DisposeResources();
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
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
                    CompleteBacklogRequests();
                    ConsumeResult<string, byte[]> result;
                    try
                    {
                        result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                    }

                    catch (ConsumeException)
                    {
                        throw;
                    }

                    if (result is null)
                    {
                        continue;
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
                while (backlogRequests.TryDequeue(out var request))
                {
                    request.Completion.TrySetException(new InvalidOperationException(
                        "Kafka capacity consumer stopped before backlog capture completed."));
                }

                consumer.Close();
            }
        }

        private void CompleteBacklogRequests()
        {
            while (backlogRequests.TryDequeue(out var request))
            {
                try
                {
                    long backlog = 0;
                    foreach (var partition in assignedPartitions)
                    {
                        var remaining = request.Remaining;
                        if (remaining <= TimeSpan.Zero)
                        {
                            throw new TimeoutException(
                                "Kafka capacity backlog watermark query timed out.");
                        }

                        var high = consumer.QueryWatermarkOffsets(partition, remaining).High;
                        var position = consumer.Position(partition);
                        if (high == Offset.Unset || position == Offset.Unset)
                        {
                            throw new InvalidDataException(
                                "Kafka capacity backlog watermark or position is unset.");
                        }

                        backlog = checked(backlog + Math.Max(0, high.Value - position.Value));
                    }

                    request.Completion.TrySetResult(
                        new KafkaCapacityBrokerBacklogSnapshot(backlog));
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }
        }

        private sealed class BacklogRequest(TimeSpan timeout)
        {
            private readonly long started = Stopwatch.GetTimestamp();

            public TaskCompletionSource<KafkaCapacityBrokerBacklogSnapshot> Completion
            {
                get;
            } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TimeSpan Remaining
            {
                get
                {
                    var remaining = timeout - Stopwatch.GetElapsedTime(started);
                    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }
        }
    }
}
