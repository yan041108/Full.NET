using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>观察消费组滞后；未知或失败不能作为回退排空证明。</summary>
internal sealed class KafkaConsumerLagObserver
{
    private readonly Func<KafkaMessagingOptions, IKafkaLagQueryClient> _createClient;

    public KafkaConsumerLagObserver() : this(options => new KafkaLagQueryClient(options)) { }

    internal KafkaConsumerLagObserver(Func<KafkaMessagingOptions, IKafkaLagQueryClient> createClient) =>
        _createClient = createClient;

    internal async Task<CdcKafkaTopicBinding[]?> ObserveTopicBindingsAsync(KafkaMessagingOptions options, IReadOnlyList<string> topics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var client = _createClient(options);
            var result = await client.ReadTopicBindingsAsync(topics, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { token.ThrowIfCancellationRequested(); return null; }
    }

    internal async Task<bool> IsDrainedAsync(KafkaMessagingOptions options, CdcKafkaTopicBinding expected, string group, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var client = _createClient(options);
            var current = await client.ReadTopicBindingsAsync([expected.TopicName], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!KafkaDeliveryTopology.Matches([expected], current)) return false;
            var partitions = current![0].Partitions.Select(p => new TopicPartition(expected.TopicName, p.Partition)).ToArray();
            var committed = await client.ReadCommittedOffsetsAsync(group, partitions).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            foreach (var p in current[0].Partitions)
            {
                var offset = committed.GetValueOrDefault(new TopicPartition(expected.TopicName, p.Partition), -1);
                // 从未写入的空分区可以没有提交；任何有过数据的分区都必须存在完整提交，且未越过保留窗口。
                if (offset < 0 ? p.Low != 0 || p.High != 0 : offset < p.Low || offset != p.High) return false;
            }
            var final = await client.ReadTopicBindingsAsync([expected.TopicName], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return KafkaDeliveryTopology.Matches(current, final) && final!.Single().Partitions.SequenceEqual(current[0].Partitions);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { token.ThrowIfCancellationRequested(); return false; }
    }

    /// <summary>排空证据必须绑定实际 Broker 集群；查询失败或未知返回 null。</summary>
    internal async Task<string?> ObserveClusterIdAsync(KafkaMessagingOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var client = _createClient(options);
            var id = await client.ReadClusterIdAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

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

    /// <summary>默认只返回单Topic样本；多流采集器在同组样本齐全后汇总，单Topic独占组可显式发布，调用取消传播。</summary>
    /// <remarks>SDK 查询不支持 CancellationToken，单次请求最多等待 10 秒；等待其结束再释放客户端，禁止提前释放仍在使用的 native handle。</remarks>
    public async Task<KafkaConsumerLagSnapshot?> ObserveLagAsync(
        KafkaMessagingOptions kafkaOptions, string topicName, string consumerGroupId,
        CancellationToken cancellationToken, KafkaRetentionTimeProbe? retentionTimeProbe = null, bool publishTelemetry = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var client = _createClient(kafkaOptions);
            var partitions = await client.ReadPartitionsAsync(topicName).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (partitions.Count == 0) return Unknown();
            var committed = await client.ReadCommittedOffsetsAsync(consumerGroupId, partitions).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var ends = await client.ReadEndOffsetsAsync(partitions).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            long totalLag = 0;
            foreach (var partition in partitions)
            {
                // 缺失或错误的高水位不是零积压；未知提交位置则从起点保守计算。
                if (!ends.TryGetValue(partition, out var end) || end < 0) return Unknown();
                var offset = committed.TryGetValue(partition, out var value) && value >= 0 ? value : 0;
                // 提交位置超出当前日志意味着 Topic 被截断、重建或位点失配；不能伪装成零积压。
                if (offset > end) return Unknown();
                totalLag = checked(totalLag + end - offset);
            }
            double? ratio = null;
            var timeFailed = false;
            if (retentionTimeProbe is not null)
            {
                try
                {
                    ratio = await client.ReadLagRetentionRatioAsync(topicName, committed, ends, retentionTimeProbe, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { timeFailed = true; /* 可选时间索引/配置权限失败仍保留独立的消息数样本。 */ }
            }
            if (publishTelemetry) KafkaMessagingTelemetry.UpdateConsumerLag("kafka", consumerGroupId, totalLag, ratio);
            return new KafkaConsumerLagSnapshot(totalLag, ratio, timeFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (publishTelemetry) KafkaMessagingTelemetry.InvalidateConsumerLag(consumerGroupId);
            throw;
        }
        catch (Exception)
        {
            // 探针失败按未排空处理，不能放行回退切流。
            var unknown = Unknown();
            cancellationToken.ThrowIfCancellationRequested();
            return unknown;
        }
        KafkaConsumerLagSnapshot? Unknown()
        {
            if (publishTelemetry) KafkaMessagingTelemetry.InvalidateConsumerLag(consumerGroupId);
            return null;
        }
    }
}

/// <summary>单次 Consumer lag 采样结果。</summary>
/// <param name="TotalLagMessages">各分区滞后消息数之和。</param>
/// <param name="LagRetentionRatio">下一未消费位置的 Broker 追加年龄相对时间保留窗口的保守占比；不满足前提时为 null。</param>
/// <param name="RetentionTimeFailed">可选时间查询发生错误；不支持的策略与未启用观测仍为 false。</param>
internal sealed record KafkaConsumerLagSnapshot(long TotalLagMessages, double? LagRetentionRatio, bool RetentionTimeFailed = false);
