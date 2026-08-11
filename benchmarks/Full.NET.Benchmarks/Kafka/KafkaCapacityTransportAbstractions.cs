using Full.NET.Messaging.Kafka;

namespace Full.NET.Benchmarks.Kafka;

/// <summary>
/// 表示非阻塞 Produce 的最终 Broker 交付结果。
/// </summary>
public sealed record KafkaCapacityDeliveryReport(
    long GlobalSequence,
    bool Persisted,
    string? ErrorCode);

/// <summary>
/// 表示独立 Consumer 从真实 Partition 收到的消息值。
/// </summary>
public sealed record KafkaCapacityConsumedMessage(
    int Partition,
    byte[] Value);

/// <summary>
/// 表示从 Broker 高水位与当前消费位置计算出的真实 Offset 积压。
/// </summary>
public sealed record KafkaCapacityBrokerBacklogSnapshot(long MessageCount);

public interface IKafkaCapacityProducerFactory
{
    IKafkaCapacityProducer Create(
        KafkaMessagingOptions options,
        string clientId,
        Action<string> statisticsHandler);
}

public interface IKafkaCapacityProducer : IAsyncDisposable
{
    void Produce(
        string topicName,
        int partition,
        string key,
        byte[] value,
        long globalSequence,
        Action<KafkaCapacityDeliveryReport> deliveryHandler);

    int Flush(TimeSpan timeout);
}

public interface IKafkaCapacityConsumerFactory
{
    IKafkaCapacityConsumer Create(
        KafkaMessagingOptions options,
        string consumerGroupId,
        string clientId,
        int expectedPartitions,
        Action<string> statisticsHandler);
}

public interface IKafkaCapacityConsumer : IAsyncDisposable
{
    Task Completion { get; }

    Task StartAsync(
        string topicName,
        Func<KafkaCapacityConsumedMessage, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken);

    Task WaitForAssignmentAsync(CancellationToken cancellationToken);

    Task<KafkaCapacityBrokerBacklogSnapshot> CaptureBacklogAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IKafkaCapacityCheckpointStore
{
    Task<KafkaCapacityCheckpoint> SaveAsync(
        string path,
        KafkaCapacityCheckpoint checkpoint,
        KafkaCapacitySampleEvidence evidence,
        CancellationToken cancellationToken);
}
