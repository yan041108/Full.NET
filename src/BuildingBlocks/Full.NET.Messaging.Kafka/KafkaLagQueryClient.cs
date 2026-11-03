using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>一次采样拥有一个客户端；每次异步请求有 SDK 超时，调用方须等待在途请求结束后释放。</summary>
internal interface IKafkaLagQueryClient : IDisposable
{
    Task<string?> ReadClusterIdAsync();
    Task<IReadOnlyList<TopicPartition>> ReadPartitionsAsync(string topic);
    Task<IReadOnlyDictionary<TopicPartition, long>> ReadCommittedOffsetsAsync(string group, IReadOnlyList<TopicPartition> partitions);
    Task<IReadOnlyDictionary<TopicPartition, long>> ReadEndOffsetsAsync(IReadOnlyList<TopicPartition> partitions);
    Task<CdcKafkaTopicBinding[]?> ReadTopicBindingsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken);
    Task<double?> ReadLagRetentionRatioAsync(string topic, IReadOnlyDictionary<TopicPartition, long> committed,
        IReadOnlyDictionary<TopicPartition, long> ends, KafkaRetentionTimeProbe probe, CancellationToken cancellationToken);
}

/// <summary>使用原生 Admin 异步接口批量读取，不创建临时 Consumer 或线程池阻塞任务。</summary>
internal sealed class KafkaLagQueryClient(KafkaMessagingOptions options) : IKafkaLagQueryClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly IAdminClient _admin = new AdminClientBuilder(options.BuildClientConfig()).Build();

    public async Task<string?> ReadClusterIdAsync() => (await _admin.DescribeClusterAsync(
        new DescribeClusterOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false)).ClusterId;

    public async Task<IReadOnlyList<TopicPartition>> ReadPartitionsAsync(string topic)
    {
        var result = await _admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]),
            new DescribeTopicsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var description = result.TopicDescriptions.SingleOrDefault(item => item.Name == topic);
        if (description is null || description.Error.IsError) return [];
        return description.Partitions.Select(partition => new TopicPartition(topic, partition.Partition)).ToArray();
    }

    public async Task<IReadOnlyDictionary<TopicPartition, long>> ReadCommittedOffsetsAsync(string group, IReadOnlyList<TopicPartition> partitions)
    {
        var result = await _admin.ListConsumerGroupOffsetsAsync(
            [new ConsumerGroupTopicPartitions(group, partitions.ToList())],
            new ListConsumerGroupOffsetsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var offsets = result.SingleOrDefault(item => item.Group == group)?.Partitions;
        if (offsets is null) return new Dictionary<TopicPartition, long>();
        if (offsets.Any(item => item.Error.IsError)) throw new InvalidOperationException("消费组提交位置查询失败。");
        return offsets.ToDictionary(item => item.TopicPartition, item => item.Offset.Value);
    }

    public async Task<IReadOnlyDictionary<TopicPartition, long>> ReadEndOffsetsAsync(IReadOnlyList<TopicPartition> partitions)
    {
        var result = await _admin.ListOffsetsAsync(partitions.Select(partition => new TopicPartitionOffsetSpec
        {
            TopicPartition = partition, OffsetSpec = OffsetSpec.Latest(),
        }), new ListOffsetsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        var offsets = result.ResultInfos.Select(item => item.TopicPartitionOffsetError).ToArray();
        if (offsets.Any(item => item.Error.IsError)) throw new InvalidOperationException("分区高水位查询失败。");
        return offsets.ToDictionary(item => item.TopicPartition, item => item.Offset.Value);
    }

    public async Task<CdcKafkaTopicBinding[]?> ReadTopicBindingsAsync(IReadOnlyList<string> topics, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (topics.Count is 0 or > 32 || topics.Distinct(StringComparer.Ordinal).Count() != topics.Count) return null;
        if (!await HasDeleteOnlyPolicyAsync(topics, cancellationToken).ConfigureAwait(false)) return null;
        var description = await _admin.DescribeTopicsAsync(TopicCollection.OfTopicNames(topics),
            new DescribeTopicsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (description.TopicDescriptions.Count != topics.Count || description.TopicDescriptions.Any(item => item.Error.IsError
            || item.Partitions.Count is 0 or > 256 || string.IsNullOrWhiteSpace(item.TopicId.ToString()))) return null;
        var partitions = description.TopicDescriptions.SelectMany(item => item.Partitions.Select(p => new TopicPartition(item.Name, p.Partition))).ToArray();
        var low = await ReadTimesAsync(partitions.Select(p => (p, OffsetSpec.Earliest())), cancellationToken).ConfigureAwait(false);
        var high = await ReadEndOffsetsAsync(partitions).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (partitions.Any(p => !low.TryGetValue(p, out var start) || !high.TryGetValue(p, out var end) || start.Offset < 0 || end < start.Offset)) return null;
        if (!await HasDeleteOnlyPolicyAsync(topics, cancellationToken).ConfigureAwait(false)) return null;
        // 水位按名称读取，采样期间同名重建会混入新 Topic 的位置；重查身份与分区后才组合快照。
        var finalDescription = await _admin.DescribeTopicsAsync(TopicCollection.OfTopicNames(topics),
            new DescribeTopicsOptions { RequestTimeout = RequestTimeout }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (finalDescription.TopicDescriptions.Count != topics.Count || finalDescription.TopicDescriptions.Any(item => item.Error.IsError)
            || !SameTopicIdentities(
                description.TopicDescriptions.Select(item => (item.Name, item.TopicId.ToString(), item.Partitions.Select(p => p.Partition).ToArray())),
                finalDescription.TopicDescriptions.Select(item => (item.Name, item.TopicId.ToString(), item.Partitions.Select(p => p.Partition).ToArray())))) return null;
        return description.TopicDescriptions.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => new CdcKafkaTopicBinding(
            item.Name, item.TopicId.ToString(), item.Partitions.OrderBy(p => p.Partition).Select(p =>
            {
                var partition = new TopicPartition(item.Name, p.Partition);
                return new CdcKafkaPartitionBinding(p.Partition, low[partition].Offset, high[partition]);
            }).ToArray())).ToArray();
    }

    internal static bool SameTopicIdentities(IEnumerable<(string Name, string Id, int[] Partitions)> before,
        IEnumerable<(string Name, string Id, int[] Partitions)> after)
    {
        var left = before.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var right = after.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        return left.Length == right.Length && left.Zip(right).All(pair => pair.First.Name == pair.Second.Name
            && pair.First.Id == pair.Second.Id && pair.First.Partitions.Order().SequenceEqual(pair.Second.Partitions.Order()));
    }

    public void Dispose() => _admin.Dispose();

    // 可靠事件按实体复用 key；compact 会在 Low 不变时删除中间事件，不能用当前组位点证明完整交付。
    private async Task<bool> HasDeleteOnlyPolicyAsync(IReadOnlyList<string> topics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var configurations = await _admin.DescribeConfigsAsync(topics.Select(topic => new ConfigResource { Type = ResourceType.Topic, Name = topic }),
            new DescribeConfigsOptions { RequestTimeout = TimeSpan.FromSeconds(2) }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (configurations.Count != topics.Count) return false;
        foreach (var topic in topics)
        {
            var configuration = configurations.SingleOrDefault(item => item.ConfigResource.Type == ResourceType.Topic && item.ConfigResource.Name == topic);
            if (configuration is null || !configuration.Entries.TryGetValue("cleanup.policy", out var cleanup) || string.IsNullOrWhiteSpace(cleanup.Value))
                return false;
            var policies = cleanup.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
            if (policies.Length != 1 || policies[0] != "delete") return false;
        }
        return true;
    }

    public async Task<double?> ReadLagRetentionRatioAsync(string topic, IReadOnlyDictionary<TopicPartition, long> committed,
        IReadOnlyDictionary<TopicPartition, long> ends, KafkaRetentionTimeProbe probe, CancellationToken cancellationToken)
    {
        if (ends.Count is 0 or > 32 || probe.MaximumClockSkewSeconds is < 0 or > 30) return null;
        var config = await ReadTimePolicyAsync(topic, cancellationToken).ConfigureAwait(false);
        if (config is null || !long.TryParse(config.GetValueOrDefault("retention.ms"), out var retention) || retention <= 0
            || config.GetValueOrDefault("message.timestamp.type") != "LogAppendTime"
            || config.GetValueOrDefault("cleanup.policy") != "delete" || config.GetValueOrDefault("retention.bytes") != "-1"
            || !await MatchesTopicAsync(topic, probe.TopicId, cancellationToken).ConfigureAwait(false)) return null;
        var partitions = ends.Keys.ToArray();
        var earliest = await ReadTimesAsync(partitions.Select(p => (p, OffsetSpec.Earliest())), cancellationToken).ConfigureAwait(false);
        if (partitions.Any(p => !committed.TryGetValue(p, out var offset) || offset < 0 || offset > ends[p]
            || !earliest.TryGetValue(p, out var first) || first.Offset < 0 || offset < first.Offset)) return null;
        var pending = partitions.Where(p => committed[p] < ends[p]).ToArray();
        var lower = new Dictionary<TopicPartition, long>();
        var upper = new Dictionary<TopicPartition, long>();
        if (pending.Length > 0)
        {
            var first = await ReadTimesAsync(pending.Select(p => (p, OffsetSpec.ForTimestamp(0))), cancellationToken).ConfigureAwait(false);
            var last = await ReadTimesAsync(pending.Select(p => (p, OffsetSpec.MaxTimestamp())), cancellationToken).ConfigureAwait(false);
            foreach (var p in pending)
            {
                if (!first.TryGetValue(p, out var start) || !last.TryGetValue(p, out var end)
                    || start.Timestamp < 0 || end.Timestamp < start.Timestamp || start.Offset < 0 || end.Offset < 0 || start.Offset > committed[p]
                    || end.Timestamp > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + probe.MaximumClockSkewSeconds * 2000L
                    || end.Timestamp - start.Timestamp > 30L * 86400000) return null;
                lower[p] = start.Timestamp; upper[p] = end.Timestamp;
            }
            // 首个时间戳 >= t 的位置随 t 单调，二分得到截至提交位置的时间戳前缀最大值；不假设 Broker 墙钟不会回拨。
            // 每轮至多 32 分区一个异步请求，最多 22 轮；超过 30 天跨度不猜测。
            for (var round = 0; round < 22; round++)
            {
                var active = pending.Where(p => upper[p] - lower[p] > 1000).ToArray();
                if (active.Length == 0) break;
                var mid = active.ToDictionary(p => p, p => lower[p] + (upper[p] - lower[p] + 1) / 2);
                var offsets = await ReadTimesAsync(active.Select(p => (p, OffsetSpec.ForTimestamp(mid[p]))), cancellationToken).ConfigureAwait(false);
                foreach (var p in active)
                {
                    if (!offsets.TryGetValue(p, out var item) || item.Offset < 0) return null;
                    if (item.Offset <= committed[p]) lower[p] = mid[p]; else upper[p] = mid[p] - 1;
                }
            }
        }
        var finalEarliest = await ReadTimesAsync(partitions.Select(p => (p, OffsetSpec.Earliest())), cancellationToken).ConfigureAwait(false);
        if (partitions.Any(p => !finalEarliest.TryGetValue(p, out var first) || first.Offset < 0 || committed[p] < first.Offset)
            || !await MatchesTopicAsync(topic, probe.TopicId, cancellationToken).ConfigureAwait(false)) return null;
        var finalConfig = await ReadTimePolicyAsync(topic, cancellationToken).ConfigureAwait(false);
        if (finalConfig is null || config.Any(item => finalConfig.GetValueOrDefault(item.Key) != item.Value)) return null;
        return pending.Length == 0 ? 0 : Math.Max(0,
            // 历史前缀与下一记录的回拨最多 2s，当前两节点相对偏差最多 2s，合计加入 4 倍绝对 UTC 误差预算。
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + probe.MaximumClockSkewSeconds * 4000L - lower.Values.Min()) / (double)retention;
    }

    private async Task<bool> MatchesTopicAsync(string topic, string topicId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await _admin.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]),
            new DescribeTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(2) }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result.TopicDescriptions.SingleOrDefault() is { } description && !description.Error.IsError
            && description.TopicId.ToString() == topicId;
    }

    private async Task<Dictionary<string, string>?> ReadTimePolicyAsync(string topic, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await _admin.DescribeConfigsAsync([new ConfigResource { Type = ResourceType.Topic, Name = topic }],
            new DescribeConfigsOptions { RequestTimeout = TimeSpan.FromSeconds(2) }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var config = result.SingleOrDefault();
        if (config is null) return null;
        string[] names = ["retention.ms", "retention.bytes", "message.timestamp.type", "cleanup.policy"];
        return names.All(config.Entries.ContainsKey) ? names.ToDictionary(name => name, name => config.Entries[name].Value) : null;
    }

    private async Task<Dictionary<TopicPartition, (long Offset, long Timestamp)>> ReadTimesAsync(
        IEnumerable<(TopicPartition Partition, OffsetSpec Spec)> requests, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await _admin.ListOffsetsAsync(requests.Select(item => new TopicPartitionOffsetSpec
        { TopicPartition = item.Partition, OffsetSpec = item.Spec }), new ListOffsetsOptions { RequestTimeout = TimeSpan.FromSeconds(2) }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (result.ResultInfos.Any(item => item.TopicPartitionOffsetError.Error.IsError)) throw new InvalidOperationException("分区时间索引查询失败。");
        return result.ResultInfos.ToDictionary(item => item.TopicPartitionOffsetError.TopicPartition,
            item => (item.TopicPartitionOffsetError.Offset.Value, item.Timestamp));
    }
}

/// <summary>运维确认从创建起使用 LogAppendTime 的不可变 Topic ID，以及受外部时钟同步保证的最大偏差。</summary>
internal sealed record KafkaRetentionTimeProbe(string TopicId, int MaximumClockSkewSeconds);
