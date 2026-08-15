using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 复用生产分区 Scheduler 与连续 Offset 水位执行容量 Consumer 循环。
/// </summary>
public sealed class KafkaCapacityWorkerConsumerLoop : IAsyncDisposable
{
    private readonly IConsumer<string, byte[]> consumer;
    private readonly KafkaConsumerPartitionCoordinator coordinator;
    private readonly KafkaPartitionWorkScheduler scheduler;
    private readonly KafkaConsumerMessageProcessor processor;
    private readonly KafkaEnvelopeReader envelopeReader;
    private readonly WorkerRoutePlan plan = new();
    private readonly Action<ConsumeResult<string, byte[]>>? onPolled;

    public KafkaCapacityWorkerConsumerLoop(
        KafkaMessagingOptions options,
        ConsumerConfig consumerConfig,
        string topicName,
        int partitions,
        IServiceProvider serviceProvider,
        Action<ConsumeResult<string, byte[]>>? onPolled = null,
        bool assignAtHighWatermark = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(consumerConfig);
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        this.onPolled = onPolled;
        processor = serviceProvider.GetRequiredService<KafkaConsumerMessageProcessor>();
        envelopeReader = serviceProvider.GetRequiredService<KafkaEnvelopeReader>();
        consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        scheduler = new KafkaPartitionWorkScheduler(
            (result, token) => processor.ProcessScheduledMessageAsync(plan, result, token),
            options);
        coordinator = new KafkaConsumerPartitionCoordinator(
            consumer,
            scheduler,
            options,
            KafkaCapacityWorkerContracts.ConsumerName,
            serviceProvider.GetRequiredService<ILogger<KafkaCapacityWorkerConsumerLoop>>());
        var assignments = Enumerable.Range(0, partitions)
            .Select(partition => new TopicPartitionOffset(
                topicName,
                new Partition(partition),
                assignAtHighWatermark
                    ? QueryHighWatermark(consumer, topicName, partition, options)
                    : Offset.Beginning))
            .ToArray();
        consumer.Assign(assignments);
        coordinator.OnAssigned(assignments.Select(static item => item.TopicPartition));
    }

    public int BufferDepth => scheduler.BufferDepth;

    public int InFlightCount => scheduler.InFlightCount;

    public void PollAvailable(
        KafkaCapacityIntegrityTracker? tracker,
        KafkaCapacitySampleContext context)
    {
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
        coordinator.ResumeDuePartitions(DateTimeOffset.UtcNow);
        var result = consumer.Consume(TimeSpan.Zero);
        if (result?.Message is null || result.IsPartitionEOF)
        {
            return;
        }

        var dispatchResult = UnwrapConnectPayload(result);
        onPolled?.Invoke(dispatchResult);

        if (tracker is not null)
        {
            if (TryDecodeCapacityEnvelope(dispatchResult, out var envelope))
            {
                var logicalPartition = checked((int)(envelope.GlobalSequence
                    % context.TopicIdentity.Partitions));
                tracker.OnConsumed(
                    envelope.GlobalSequence,
                    logicalPartition,
                    envelope.PartitionSequence,
                    envelope.RunHash == context.RunHash
                    && envelope.SampleHash == context.SampleHash);
            }
            else
            {
                tracker.OnCorrupted();
            }
        }

        if (!coordinator.TryDispatch(dispatchResult))
        {
            throw new InvalidOperationException(
                "Kafka capacity production scheduler rejected a polled record.");
        }
    }

    public void ProcessCompletions(DateTimeOffset now) =>
        coordinator.ProcessCompletions(now);

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await scheduler.StopAsync(timeout).ConfigureAwait(false);
        coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
        coordinator.OnRevoked(consumer.Assignment);
        consumer.Close();
    }

    public ValueTask DisposeAsync() => scheduler.DisposeAsync();

    private static ConsumeResult<string, byte[]> UnwrapConnectPayload(
        ConsumeResult<string, byte[]> result)
    {
        if (result.Message?.Value is null
            || KafkaCapacityEnvelopeCodec.TryDecode(result.Message.Value, out _))
        {
            return result;
        }

        if (!KafkaCapacityEnvelopePayloadDecoder.TryUnwrapEnvelopeBytes(
                result.Message.Value,
                out var payload))
        {
            return result;
        }

        return new ConsumeResult<string, byte[]>
        {
            TopicPartitionOffset = result.TopicPartitionOffset,
            Message = new Message<string, byte[]>
            {
                Key = result.Message.Key,
                Value = payload,
                Headers = result.Message.Headers,
                Timestamp = result.Message.Timestamp,
            },
            IsPartitionEOF = result.IsPartitionEOF,
            Topic = result.Topic,
            Partition = result.Partition,
            Offset = result.Offset,
        };
    }

    private bool TryDecodeCapacityEnvelope(
        ConsumeResult<string, byte[]> result,
        out KafkaCapacityEnvelope envelope)
    {
        if (KafkaCapacityEnvelopePayloadDecoder.TryDecode(result.Message.Value, out envelope))
        {
            return true;
        }

        if (envelopeReader.TryRead(result, out var integration, out _)
            && integration is not null
            && KafkaCapacityEnvelopePayloadDecoder.TryDecode(integration.Payload, out envelope))
        {
            return true;
        }

        envelope = default!;
        return false;
    }

    private static Offset QueryHighWatermark(
        IConsumer<string, byte[]> consumer,
        string topic,
        int partition,
        KafkaMessagingOptions options) =>
        consumer.QueryWatermarkOffsets(
            new TopicPartition(topic, new Partition(partition)),
            TimeSpan.FromMilliseconds(options.DeliveryTimeoutMilliseconds)).High;

    private sealed class WorkerRoutePlan : IKafkaConsumerRoutePlan
    {
        private int revoked;

        public string ConsumerName => KafkaCapacityWorkerContracts.ConsumerName;

        public bool HasOwnershipRevoked => Volatile.Read(ref revoked) != 0;

        public bool ContainsRoute(string eventType, int schemaVersion) =>
            string.Equals(
                eventType,
                KafkaCapacityWorkerContracts.EventType,
                StringComparison.Ordinal)
            && schemaVersion == KafkaCapacityWorkerContracts.SchemaVersion;

        public void SetOwnershipRevoked(
            string eventType,
            int schemaVersion,
            bool isRevoked) =>
            Volatile.Write(ref revoked, isRevoked ? 1 : 0);

        public string ResolveTopicCode(string topic) =>
            KafkaCapacityWorkerContracts.TopicCode;
    }
}
