using System.Text.Json;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Messaging.Kafka;

/// <summary>持久回退协议的控制面适配器；由模块在跨实例会话锁内调用。</summary>
internal sealed class KafkaConnectEventDeliveryRollbackReadinessReader(
    IServiceScopeFactory scopeFactory, IOptions<KafkaConnectRollbackOptions> rollbackOptions,
    IOptions<KafkaMessagingOptions> kafkaOptions, IKafkaConnectAdminClient connectAdmin, KafkaConsumerLagObserver lagObserver, IHostEnvironment environment)
    : IEventDeliveryRollbackReadinessReader
{
    public async Task<EventDeliveryRollbackReadiness> PrepareAsync(string eventType, int schemaVersion,
        Guid rollbackGeneration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        var options = rollbackOptions.Value;
        if (!options.Enabled || !kafkaOptions.Value.Enabled) return EventDeliveryRollbackReadiness.Unavailable;
        var binding = options.Streams.SingleOrDefault(item => item.EventType == eventType && item.SchemaVersion == schemaVersion);
        if (binding is null) return EventDeliveryRollbackReadiness.Unavailable;
        await using var scope = scopeFactory.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IEventDeliveryRollbackJournal>();
        var operation = await journal.FindAsync(rollbackGeneration, cancellationToken).ConfigureAwait(false);
        if (operation is null || operation.EventType != eventType || operation.SchemaVersion != schemaVersion
            || operation.Phase != EventDeliveryRollbackPhase.Prepared) return EventDeliveryRollbackReadiness.Unavailable;
        var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var topic = catalog.GetTopicRequired(eventType, schemaVersion);
        var groups = catalog.GetAllSubscriptions().Where(item => item.EventType == eventType && item.SchemaVersion == schemaVersion)
            .Select(item => item.ConsumerName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        // 单流回退不隐式暂停其他正式流；共享 Connector 需要另行设计成整体变更。
        var owners = await scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>().ListAsync(cancellationToken).ConfigureAwait(false);
        if (topic.TopicCode != binding.TopicName || groups.Length == 0
            || !groups.Contains(binding.ConsumerGroupId, StringComparer.Ordinal)
            || options.Streams.Count(item => item.ConnectorName == binding.ConnectorName) != 1
            || owners.Any(item => item.CurrentOwner == EventDeliveryOwner.CdcKafka && (item.MessageType != eventType || item.SchemaVersion != schemaVersion)))
            return EventDeliveryRollbackReadiness.Unavailable;
        var registeredTopics = scope.ServiceProvider.GetServices<IntegrationEventTopicDefinition>().ToArray();
        var effective = await scope.ServiceProvider.GetRequiredService<IEffectiveEventDeliveryOwnerResolver>()
            .GetDeliveryOwnersAsync(registeredTopics, cancellationToken).ConfigureAwait(false);
        if (effective.Any(item => item.Value == EventDeliveryOwner.CdcKafka && (item.Key.EventType != eventType || item.Key.SchemaVersion != schemaVersion)))
            return EventDeliveryRollbackReadiness.Unavailable;
        if (!await connectAdmin.WaitForConnectorHealthyAsync(binding.ConnectorName, TimeSpan.FromSeconds(options.PrepareTimeoutSeconds), cancellationToken).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        var configuration = await connectAdmin.TryReadConnectorConfigurationAsync(binding.ConnectorName, cancellationToken).ConfigureAwait(false);
        if (configuration is null || configuration.Count == 0 || !KafkaConnectResourceIdentity.IsTargetRoute(configuration, binding))
            return EventDeliveryRollbackReadiness.Unavailable;
        var clusterId = await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, cancellationToken).ConfigureAwait(false);
        if (clusterId is null || clusterId != await connectAdmin.TryReadKafkaClusterIdAsync(cancellationToken).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        var configurationFingerprint = KafkaConnectResourceIdentity.Compute(configuration);
        var resources = new RollbackControlResources(new Uri(options.ConnectBaseUri!).AbsoluteUri, binding.ConnectorName,
            binding.TopicName, groups, kafkaOptions.Value.RetryStages.ToArray(), environment.EnvironmentName, configurationFingerprint, clusterId);
        if (!await journal.BindResourcesAsync(rollbackGeneration,
                JsonSerializer.Serialize(resources, KafkaMessagingJsonSerializerContext.Default.RollbackControlResources), cancellationToken).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.PrepareTimeoutSeconds + options.DrainTimeoutSeconds));
        var token = deadline.Token;
        var poll = TimeSpan.FromMilliseconds(options.DrainPollIntervalMilliseconds);
        var fenceReader = scope.ServiceProvider.GetRequiredService<IEventDeliveryProducerFencePositionReader>();
        EventDeliveryProducerFenceSnapshot? fence;
        while ((fence = await fenceReader.TryReadAsync(eventType, schemaVersion, rollbackGeneration, token).ConfigureAwait(false)) is null)
            await Task.Delay(poll, token).ConfigureAwait(false);
        // CDC 必须先追上冻结的边界；先暂停会把落后的 Connector 永久卡在边界之后。
        CdcDeliveryPosition? position;
        while (true)
        {
            position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, token).ConfigureAwait(false);
            if (position is not null && CdcDeliveryPosition.ConnectorCoversProducerFence(fence.ProducerFencePosition, position)) break;
            await Task.Delay(poll, token).ConfigureAwait(false);
        }
        var currentConfiguration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (currentConfiguration is null || KafkaConnectResourceIdentity.Compute(currentConfiguration) != resources.ConfigurationFingerprint)
            return EventDeliveryRollbackReadiness.Unavailable;
        if (resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        if (!await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.Prepared,
                EventDeliveryRollbackPhase.PauseRequested, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        await connectAdmin.PauseConnectorAsync(resources.ConnectorName, token).ConfigureAwait(false);
        while (!await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false))
            await Task.Delay(poll, token).ConfigureAwait(false);
        if (!await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.PauseRequested,
                EventDeliveryRollbackPhase.Paused, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;

        // Offset 仅在 Inbox/处理完成后提交；连续两轮核对全部组和重试 Topic，未知值不等同于零。
        var stableRounds = 0;
        var topics = new[] { resources.TopicName }.Concat(resources.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(resources.TopicName, stage))).ToArray();
        while (stableRounds < 2)
        {
            var drained = true;
            foreach (var group in resources.ConsumerGroups)
                foreach (var target in topics)
                    drained &= await lagObserver.ObserveLagAsync(kafkaOptions.Value, target, group, token).ConfigureAwait(false) is { TotalLagMessages: 0 };
            stableRounds = drained ? stableRounds + 1 : 0;
            if (stableRounds < 2) await Task.Delay(poll, token).ConfigureAwait(false);
        }
        position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (position is null || !CdcDeliveryPosition.ConnectorCoversProducerFence(fence.ProducerFencePosition, position)
            || resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(token).ConfigureAwait(false)
            || resources.KafkaClusterId != await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, token).ConfigureAwait(false)
            || !await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        return new(rollbackGeneration, true, true, true, fence.ProducerFencePosition.ToJson(), position.ToJson(),
            rollbackGeneration.ToString("N"), fence.LastPublishedEventId, DateTimeOffset.UtcNow);
    }

    public async Task AbortAsync(string eventType, int schemaVersion, Guid rollbackGeneration, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IEventDeliveryRollbackJournal>();
        var operation = await journal.FindAsync(rollbackGeneration, cancellationToken).ConfigureAwait(false);
        if (operation is null || operation.EventType != eventType || operation.SchemaVersion != schemaVersion
            || operation.Phase != EventDeliveryRollbackPhase.Recovering)
            throw new InvalidOperationException("恢复必须持有当前代次的持久 Recovering 意图。");
        var owner = await scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>().FindAsync(eventType, schemaVersion, cancellationToken).ConfigureAwait(false);
        if (owner?.CurrentOwner != EventDeliveryOwner.CdcKafka) throw new InvalidOperationException("当前所有者不允许恢复旧 Connector。");
        if (operation.ResourcesJson is null)
        {
            if (operation.HasPauseIntent) throw new InvalidOperationException("已发出暂停意图但资源记录缺失；保留保护。");
            return;
        }
        var resources = JsonSerializer.Deserialize(operation.ResourcesJson, KafkaMessagingJsonSerializerContext.Default.RollbackControlResources)
            ?? throw new InvalidOperationException("持久控制资源无法解析。");
        var options = rollbackOptions.Value;
        if (!options.Enabled || !Uri.TryCreate(options.ConnectBaseUri, UriKind.Absolute, out var currentUri)
            || resources.ConnectBaseUri != currentUri.AbsoluteUri || resources.EnvironmentName != environment.EnvironmentName)
            throw new InvalidOperationException("控制面地址已改变；必须恢复原配置后才能恢复原操作。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.PrepareTimeoutSeconds));
        if (resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(deadline.Token).ConfigureAwait(false))
            throw new InvalidOperationException("原 Connect 的 Kafka 集群身份已改变；保留保护。");
        var configuration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (configuration is null || KafkaConnectResourceIdentity.Compute(configuration) != resources.ConfigurationFingerprint)
            throw new InvalidOperationException("原 Connector 配置已改变；禁止恢复同名但不同资源的发布者。");
        if (operation.HasPauseIntent)
            await connectAdmin.ResumeConnectorAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (!await connectAdmin.WaitForConnectorHealthyAsync(resources.ConnectorName,
                TimeSpan.FromSeconds(options.PrepareTimeoutSeconds), deadline.Token).ConfigureAwait(false))
            throw new InvalidOperationException("恢复请求已提交，但未确认全部 Connector 任务 RUNNING；保留生产者保护。");
    }
}

/// <summary>暂停前冻结资源；恢复时读取原快照，不根据修改后的映射猜测目标。</summary>
internal sealed record RollbackControlResources(string ConnectBaseUri, string ConnectorName, string TopicName,
    string[] ConsumerGroups, string[] RetryStages, string EnvironmentName, string ConfigurationFingerprint, string KafkaClusterId);

/// <summary>控制资源身份使用有长度边界的稳定摘要，绝不保存配置或凭据正文。</summary>
internal static class KafkaConnectResourceIdentity
{
    internal static string Compute(IReadOnlyDictionary<string, string> configuration)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var item in configuration.Where(item => IsIdentityKey(item.Key)).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            Append(item.Key); Append(item.Value);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
        void Append(string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
    }

    /// <summary>当前单流控制协议只接受固定目标 Topic 的专用 Outbox 路由，拒绝任意健康 Connector。</summary>
    internal static bool IsTargetRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding)
        => IsRoute(config, binding, shadow: false);

    internal static bool IsShadowRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding)
        => IsRoute(config, binding, shadow: true);

    private static bool IsRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding, bool shadow)
    {
        if (string.IsNullOrWhiteSpace(binding.SourceDatabaseName) || !string.IsNullOrWhiteSpace(config.GetValueOrDefault("producer.override.bootstrap.servers"))) return false;
        if (string.IsNullOrWhiteSpace(config.GetValueOrDefault("topic.heartbeat.prefix"))
            || config.ContainsKey("heartbeat.topics.prefix")
            || !string.IsNullOrWhiteSpace(config.GetValueOrDefault("heartbeat.action.query"))) return false;
        var sqlServer = config.GetValueOrDefault("connector.class") == "io.debezium.connector.sqlserver.SqlServerConnector";
        var mySql = config.GetValueOrDefault("connector.class") == "io.debezium.connector.mysql.MySqlConnector";
        // MySQL 冻结边界包含控制表提交；捕获表静默时仍须用心跳提交已读位点，不能永远停在最后一条业务事件。
        if (mySql && config.GetValueOrDefault("heartbeat.interval.ms") != "1000") return false;
        if ((!sqlServer && !mySql) || config.GetValueOrDefault(sqlServer ? "database.names" : "database.include.list") != binding.SourceDatabaseName
            || config.GetValueOrDefault("table.include.list") != (sqlServer ? "dbo" : binding.SourceDatabaseName)
                + (shadow ? ".fn_messaging_shadow_event" : ".fn_messaging_outbox_event")) return false;
        // 业务字节原样传输，结构化元数据交给显式 delegate；两种编码均参与就绪和资源身份校验。
        var required = new Dictionary<string, string>
        {
            ["key.converter"] = "org.apache.kafka.connect.storage.StringConverter",
            ["value.converter"] = "io.debezium.converters.BinaryDataConverter",
            ["value.converter.delegate.converter.type"] = "org.apache.kafka.connect.json.JsonConverter",
            ["value.converter.delegate.converter.type.schemas.enable"] = "false",
            ["transforms"] = "outbox", ["tasks.max"] = "1", ["snapshot.mode"] = "no_data",
            ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
            ["transforms.outbox.table.field.event.id"] = "Id", ["transforms.outbox.table.field.event.key"] = "PartitionKey",
            ["transforms.outbox.table.field.event.type"] = "MessageType", ["transforms.outbox.table.field.event.payload"] = "Payload",
            ["transforms.outbox.route.by.field"] = "MessageType", ["transforms.outbox.route.topic.replacement"] = shadow ? binding.ShadowTopicName : binding.TopicName,
            ["transforms.outbox.table.op.invalid.behavior"] = "fatal",
        };
        if (required.Any(item => config.GetValueOrDefault(item.Key) != item.Value)) return false;
        var headers = config.GetValueOrDefault("transforms.outbox.table.fields.additional.placement", "").Split(',', StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        // SQL Server datetimeoffset 是 ZonedTimestamp 字符串，不能作为 EventRouter 的整数时间字段。
        // 两库均保留精确业务时间头；MySQL 使用发生时间定位，SQL Server 保持默认 CDC 处理时间。
        if (sqlServer && config.ContainsKey("transforms.outbox.table.field.event.timestamp")) return false;
        if (shadow && (!headers.Contains("CaptureGeneration:header:capture_generation")
            || (mySql && config.GetValueOrDefault("transforms.outbox.table.field.event.timestamp") != "OccurredAtUtc"))) return false;
        return new[] { "Id:header:event_id", "MessageType:header:message_type", "SchemaVersion:header:schema_version", "ContentType:header:content_type",
            "TenantId:header:tenant_id", "CorrelationId:header:correlation_id", "CausationId:header:causation_id", "TraceParent:header:trace_parent",
            "Producer:header:producer", "OccurredAtUtc:header:occurred_at_utc" }.All(headers.Contains);
    }

    private static bool IsIdentityKey(string key) => key.StartsWith("transforms.outbox.", StringComparison.Ordinal)
        || key.StartsWith("value.converter.delegate.", StringComparison.Ordinal)
        || key.StartsWith("heartbeat.", StringComparison.Ordinal)
        || key is "topic.heartbeat.prefix"
        || key is "connector.class" or "tasks.max" or "database.hostname" or "database.port" or "database.names"
            or "database.include.list" or "database.server.id" or "database.user" or "table.include.list" or "topic.prefix"
            or "snapshot.mode" or "capture.instance" or "transforms" or "key.converter" or "key.converter.schemas.enable"
            or "value.converter" or "value.converter.schemas.enable" or "schema.history.internal.kafka.topic"
            or "schema.history.internal.kafka.bootstrap.servers" or "producer.override.bootstrap.servers";
}
