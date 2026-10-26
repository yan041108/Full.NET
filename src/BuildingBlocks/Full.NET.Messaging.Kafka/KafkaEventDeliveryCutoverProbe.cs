using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>切流前实际探测 Connector、订阅实例及完整影子样本，不使用遥测占位值。</summary>
internal sealed class KafkaEventDeliveryCutoverProbe(IOptions<KafkaConnectRollbackOptions> controlOptions,
    IOptions<KafkaMessagingOptions> kafkaOptions, IHostEnvironment environment,
    IIntegrationEventSubscriptionCatalog catalog, KafkaConnectAdminClient connect,
    IKafkaReplayConsumerFactory consumers) : IEventDeliveryCutoverProbe
{
    public async Task<string?> ReadTargetFingerprintAsync(string eventType, int schemaVersion, CancellationToken cancellationToken = default)
    {
        try { return await ReadTargetCoreAsync(eventType, schemaVersion, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception exception) when (exception is KafkaException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<string?> ReadTargetCoreAsync(string eventType, int schemaVersion, CancellationToken cancellationToken)
    {
        var options = kafkaOptions.Value;
        var binding = Resolve(eventType, schemaVersion);
        if (binding is null || !MessagingNames.TopicCodePattern.IsMatch(binding.ShadowTopicName)
            || binding.ShadowTopicName == binding.TopicName || binding.ShadowTopicName.Length > MessagingNames.TopicCodeMaxLength
            || string.IsNullOrWhiteSpace(binding.ShadowConnectorName) || binding.ShadowConnectorName == binding.ConnectorName
            || binding.ShadowConnectorName.Length > 128
            || binding.ShadowConnectorName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))) return null;
        var workerClientId = string.IsNullOrWhiteSpace(binding.WorkerClientId) ? options.ClientId : binding.WorkerClientId;
        if (string.IsNullOrWhiteSpace(workerClientId)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        if (!await connect.WaitForConnectorHealthyAsync(binding.ConnectorName, TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(false)) return null;
        var config = await connect.TryReadConnectorConfigurationAsync(binding.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (config is null || config.Count == 0 || !KafkaConnectResourceIdentity.IsTargetRoute(config, binding)) return null;
        if (!await connect.WaitForConnectorHealthyAsync(binding.ShadowConnectorName, TimeSpan.FromSeconds(5), deadline.Token).ConfigureAwait(false)) return null;
        var shadowConfig = await connect.TryReadConnectorConfigurationAsync(binding.ShadowConnectorName, deadline.Token).ConfigureAwait(false);
        if (shadowConfig is null || !KafkaConnectResourceIdentity.IsShadowRoute(shadowConfig, binding)) return null;
        var groups = catalog.GetAllSubscriptions().Where(item => item.EventType == eventType && item.SchemaVersion == schemaVersion)
            .Select(item => item.ConsumerName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (groups.Length == 0 || !groups.Contains(binding.ConsumerGroupId, StringComparer.Ordinal)) return null;
        using var admin = new AdminClientBuilder(options.BuildClientConfig()).Build();
        var cluster = await admin.DescribeClusterAsync(new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(5) }).ConfigureAwait(false);
        var clusterId = cluster.ClusterId;
        if (string.IsNullOrWhiteSpace(clusterId)) return null;
        if (clusterId != await connect.TryReadKafkaClusterIdAsync(deadline.Token).ConfigureAwait(false)) return null;
        deadline.Token.ThrowIfCancellationRequested();
        var topics = new[] { binding.TopicName }.Concat(options.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(binding.TopicName, stage))).ToArray();
        var observedTopics = topics.Append(binding.ShadowTopicName).ToArray();
        var metadata = await admin.DescribeTopicsAsync(TopicCollection.OfTopicNames(observedTopics),
            new DescribeTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(5) }).ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested();
        if (metadata.TopicDescriptions.Count != observedTopics.Length || metadata.TopicDescriptions.Any(item => item.Error.IsError || item.Partitions.Count == 0)) return null;
        var required = metadata.TopicDescriptions.Where(item => item.Name != binding.ShadowTopicName).SelectMany(item => item.Partitions.Select(partition => new TopicPartition(item.Name, partition.Partition))).ToHashSet();
        var live = await admin.DescribeConsumerGroupsAsync(groups, new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(5) }).ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested();
        var instances = new List<string>();
        foreach (var group in groups)
        {
            var description = live.ConsumerGroupDescriptions.SingleOrDefault(item => item.GroupId == group);
            if (description is null || description.Error.IsError || description.State != ConsumerGroupState.Stable || description.Members.Count == 0) return null;
            var expectedClientId = new KafkaMessagingOptions { ClientId = workerClientId }.BuildConsumerConfig(group).ClientId;
            var assignments = description.Members.SelectMany(item => item.Assignment.TopicPartitions).Where(required.Contains).ToArray();
            if (!required.SetEquals(assignments) || assignments.Distinct().Count() != assignments.Length
                || description.Members.Any(item => item.ClientId != expectedClientId)) return null;
            instances.AddRange(description.Members.Select(item => group + ":" + item.ClientId + ":" + item.ConsumerId + ":" + item.GroupInstanceId));
        }
        // 配置只在内存中散列；数据库、日志和 HTTP 均不保存 Connector 原配置或凭据。
        var resourceParts = new[] { clusterId, environment.EnvironmentName, options.BootstrapServers ?? "", controlOptions.Value.ConnectBaseUri ?? "",
            eventType, schemaVersion.ToString(CultureInfo.InvariantCulture), binding.TopicName, binding.ShadowTopicName, binding.ConnectorName,
            binding.ShadowConnectorName, binding.SourceDatabaseName, workerClientId }
            .Concat([KafkaConnectResourceIdentity.Compute(config), KafkaConnectResourceIdentity.Compute(shadowConfig)])
            .Concat(metadata.TopicDescriptions.OrderBy(item => item.Name, StringComparer.Ordinal).Select(item =>
                item.Name + ":" + item.TopicId + ":" + string.Join(',', item.Partitions.Select(partition => partition.Partition).Order())))
            .Concat(topics).Concat(instances.Order(StringComparer.Ordinal));
        return Digest(resourceParts);
    }

    public async Task<string?> CompareShadowAsync(string eventType, int schemaVersion, IReadOnlyList<CutoverShadowEvent> expected,
        CancellationToken cancellationToken = default)
    {
        var binding = Resolve(eventType, schemaVersion);
        if (binding is null || expected.Count is < 1 or > 10000 || !MessagingNames.TopicCodePattern.IsMatch(binding.ShadowTopicName)
            || binding.ShadowTopicName == binding.TopicName) return null;
        var generation = expected[0].CaptureGeneration;
        if (generation == Guid.Empty || expected.Any(item => item.CaptureGeneration != generation)
            || expected.Sum(item => item.Payload.LongLength) > 33_554_432) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var consumer = consumers.Create(kafkaOptions.Value.BuildConsumerConfig("fullnet.messaging.cutover-evidence." + Guid.NewGuid().ToString("N")));
        var partitions = consumer.GetPartitions(binding.ShadowTopicName, TimeSpan.FromSeconds(5));
        if (partitions.Count is < 1 or > 256) return null;
        var ranges = new Dictionary<TopicPartition, WatermarkOffsets>();
        foreach (var partition in partitions)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var watermark = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(5));
            if (watermark.Low.Value < 0 || watermark.High < watermark.Low) return null;
            ranges.Add(partition, watermark);
        }
        // 使用源集合最早发生时间定位有界窗口，避免每代重复扫描整个长期保留 Topic。
        // MySQL 使用发生时间作 Record timestamp；SQL Server ZonedTimestamp 保留精确头并使用 CDC 处理时间。
        // 时间查找只缩小窗口，完整性仍逐 ID/代次/元数据核验；时钟偏差导致样本落在窗口外时拒绝证明。
        var firstTime = expected.Min(item => item.OccurredAtUtc).UtcDateTime;
        var timeOffsets = consumer.OffsetsForTimes(partitions.Select(item => new TopicPartitionTimestamp(item, new Timestamp(firstTime))).ToArray(), TimeSpan.FromSeconds(5));
        if (timeOffsets.Count != partitions.Count || timeOffsets.Select(item => item.TopicPartition).Distinct().Count() != partitions.Count) return null;
        var starts = new Dictionary<TopicPartition, long>();
        foreach (var offset in timeOffsets)
        {
            if (!ranges.TryGetValue(offset.TopicPartition, out var range)) return null;
            var start = offset.Offset == Offset.Unset ? range.High.Value : offset.Offset.Value;
            if (start < range.Low.Value || start > range.High.Value) return null;
            starts.Add(offset.TopicPartition, start);
        }
        var lastOffsets = new Dictionary<TopicPartition, long>();
        if (ranges.Sum(item => item.Value.High.Value - starts[item.Key]) > 50000) return null;
        var pending = ranges.Where(item => item.Value.High.Value > starts[item.Key]).Select(item => item.Key).ToHashSet();
        consumer.Assign(pending.Select(item => new TopicPartitionOffset(item, starts[item])).ToArray());
        var expectedById = expected.ToDictionary(item => item.Id);
        var seen = new HashSet<Guid>();
        var envelopeReader = new KafkaEnvelopeReader();
        var comparator = new ShadowEventComparator();
        long scannedBytes = 0;
        var scanned = 0;
        while (pending.Count > 0)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var record = consumer.Consume(TimeSpan.FromMilliseconds(100));
            if (record is null) continue;
            if (!pending.Contains(record.TopicPartition)) continue;
            if (record.IsPartitionEOF)
            {
                pending.Remove(record.TopicPartition);
                consumer.Pause([record.TopicPartition]);
                continue;
            }
            if (record.Offset.Value < starts[record.TopicPartition] || record.Offset >= ranges[record.TopicPartition].High
                || lastOffsets.TryGetValue(record.TopicPartition, out var previous) && record.Offset.Value <= previous) return null;
            lastOffsets[record.TopicPartition] = record.Offset.Value;
            scannedBytes += record.Message?.Value?.LongLength ?? 0;
            if (++scanned > 50000 || scannedBytes > 67_108_864 || !TryReadGeneration(record, out var observedGeneration)) return null;
            if (observedGeneration == generation)
            {
                if (!envelopeReader.TryRead(record, out var envelope, out _) || envelope is null
                    || !expectedById.TryGetValue(envelope.EventId, out var sample)
                    || envelope.OccurredAtUtc != sample.OccurredAtUtc || envelope.TenantId != sample.TenantId
                    || envelope.Producer != sample.Producer || envelope.ContentType != sample.ContentType
                    || envelope.CorrelationId != sample.CorrelationId || envelope.CausationId != sample.CausationId
                    || envelope.TraceParent != sample.TraceParent
                    || !comparator.CompareExpectedToObserved(ShadowEventFingerprint.Create(sample.Id, eventType, schemaVersion,
                        sample.PartitionKey, sample.Payload, sample.OccurredAtUtc), ShadowEventFingerprint.FromEnvelope(envelope), null).IsMatch)
                    return null;
                // 至少一次传输允许同代完全相同的副本；重复不会增加源集合的确认数量。
                seen.Add(envelope.EventId);
            }
            if (record.Offset.Value + 1 == ranges[record.TopicPartition].High.Value)
            {
                pending.Remove(record.TopicPartition);
                consumer.Pause([record.TopicPartition]);
            }
        }
        if (seen.Count != expected.Count) return null;
        var finalPartitions = consumer.GetPartitions(binding.ShadowTopicName, TimeSpan.FromSeconds(5));
        if (!partitions.ToHashSet().SetEquals(finalPartitions)) return null;
        foreach (var range in ranges)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var current = consumer.QueryWatermarkOffsets(range.Key, TimeSpan.FromSeconds(5));
            if (current.Low != range.Value.Low || current.High != range.Value.High) return null;
        }
        return Digest(ranges.OrderBy(item => item.Key.Partition.Value).Select(item =>
            $"{generation:D}:{item.Key.Topic}:{item.Key.Partition.Value}:{starts[item.Key]}:{item.Value.High.Value}"));
    }

    private static bool TryReadGeneration(ConsumeResult<string, byte[]> record, out Guid generation)
    {
        generation = default;
        var headers = record.Message?.Headers?.Where(item => item.Key == "capture_generation").ToArray();
        if (headers is not { Length: 1 } || headers[0].GetValueBytes() is not { Length: > 0 and <= 128 } bytes) return false;
        return KafkaEnvelopeHeaderParsers.TryParseGuidHeader(Encoding.UTF8.GetString(bytes), out generation) && generation != Guid.Empty;
    }

    private KafkaConnectRollbackStreamBinding? Resolve(string eventType, int schemaVersion)
    {
        if (!controlOptions.Value.Enabled || !kafkaOptions.Value.Enabled) return null;
        var binding = controlOptions.Value.Streams.SingleOrDefault(item => item.EventType == eventType && item.SchemaVersion == schemaVersion);
        try { return binding is not null && catalog.GetTopicRequired(eventType, schemaVersion).TopicCode == binding.TopicName ? binding : null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static string Digest(IEnumerable<string> parts) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Concat(parts.Select(item => item.Length.ToString(CultureInfo.InvariantCulture) + ":" + item)))));
}
