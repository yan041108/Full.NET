using Confluent.Kafka;

namespace Full.NET.Messaging.Kafka;

/// <summary>观察消费组滞后；未知或失败不能作为回退排空证明。</summary>
internal sealed class KafkaConsumerLagObserver
{
    private readonly Func<KafkaMessagingOptions, IKafkaLagQueryClient> _createClient;

    public KafkaConsumerLagObserver() : this(options => new KafkaLagQueryClient(options)) { }

    internal KafkaConsumerLagObserver(Func<KafkaMessagingOptions, IKafkaLagQueryClient> createClient) =>
        _createClient = createClient;

    public async Task<bool> WaitUntilDrainedAsync(
        KafkaMessagingOptions kafkaOptions, string topicName, string consumerGroupId,
        TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerGroupId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var lag = await ObserveLagAsync(kafkaOptions, topicName, consumerGroupId, deadline.Token).ConfigureAwait(false);
                if (lag is { TotalLagMessages: 0 }) return true;
                await Task.Delay(pollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>采样后写低基数指标；调用取消传播，查询失败返回 null。</summary>
    /// <remarks>SDK 查询不支持 CancellationToken，单次请求最多等待 10 秒；等待其结束再释放客户端，禁止提前释放仍在使用的 native handle。</remarks>
    public async Task<KafkaConsumerLagSnapshot?> ObserveLagAsync(
        KafkaMessagingOptions kafkaOptions, string topicName, string consumerGroupId,
        CancellationToken cancellationToken, double? lagRetentionRatioOverride = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var client = _createClient(kafkaOptions);
            var partitions = await client.ReadPartitionsAsync(topicName).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (partitions.Count == 0) return null;
            var committed = await client.ReadCommittedOffsetsAsync(consumerGroupId, partitions).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var ends = await client.ReadEndOffsetsAsync(partitions).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            long totalLag = 0;
            foreach (var partition in partitions)
            {
                // 缺失或错误的高水位不是零积压；未知提交位置则从起点保守计算。
                if (!ends.TryGetValue(partition, out var end) || end < 0) return null;
                var offset = committed.TryGetValue(partition, out var value) && value >= 0 ? value : 0;
                totalLag = checked(totalLag + Math.Max(0, end - offset));
            }
            var ratio = lagRetentionRatioOverride ?? 0d;
            KafkaMessagingTelemetry.UpdateConsumerLag("kafka", consumerGroupId, totalLag, ratio);
            return new KafkaConsumerLagSnapshot(totalLag, ratio);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // 探针失败按未排空处理，不能放行回退切流。
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}

/// <summary>单次 Consumer lag 采样结果。</summary>
/// <param name="TotalLagMessages">各分区滞后消息数之和。</param>
/// <param name="LagRetentionRatio">相对保留窗口的近似占比。</param>
internal sealed record KafkaConsumerLagSnapshot(long TotalLagMessages, double LagRetentionRatio);
