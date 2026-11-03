using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
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
        Guid rollbackGeneration, EventDeliveryControlLease lease, CancellationToken cancellationToken = default)
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
        var activeOwner = owners.SingleOrDefault(item => item.MessageType == eventType && item.SchemaVersion == schemaVersion);
        if (!CdcKafkaDeliveryBinding.TryParse(activeOwner?.CdcKafkaBindingJson, out var baseline) || baseline is null)
            return EventDeliveryRollbackReadiness.Unavailable;
        if (baseline.ConnectBaseUri != new Uri(options.ConnectBaseUri!).AbsoluteUri || baseline.EnvironmentName != environment.EnvironmentName)
            return EventDeliveryRollbackReadiness.Unavailable;
        if (topic.TopicCode != binding.TopicName || groups.Length == 0
            || !groups.Contains(binding.ConsumerGroupId, StringComparer.Ordinal)
            || options.Streams.Count(item => item.ConnectorName == binding.ConnectorName) != 1)
            return EventDeliveryRollbackReadiness.Unavailable;
        var registeredTopics = scope.ServiceProvider.GetServices<IntegrationEventTopicDefinition>().ToArray();
        var effective = await scope.ServiceProvider.GetRequiredService<IEffectiveEventDeliveryOwnerResolver>()
            .GetDeliveryOwnersAsync(registeredTopics, cancellationToken).ConfigureAwait(false);
        var others = owners.Where(item => item.CurrentOwner == EventDeliveryOwner.CdcKafka
            && (item.MessageType != eventType || item.SchemaVersion != schemaVersion)).ToArray();
        if (effective.Any(item => item.Value == EventDeliveryOwner.CdcKafka && (item.Key.EventType != eventType || item.Key.SchemaVersion != schemaVersion)
                && !others.Any(owner => owner.MessageType == item.Key.EventType && owner.SchemaVersion == item.Key.SchemaVersion))
            || others.Any(item => !CdcKafkaDeliveryBinding.TryParse(item.CdcKafkaBindingJson, out var other) || other is null || !baseline.IsIsolatedFrom(other)))
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
        var topicNames = new[] { binding.TopicName }.Concat(baseline.RetryStages.Select(stage => KafkaTopicNames.GetRetryTopic(binding.TopicName, stage))).ToArray();
        if (!baseline.Topics.Select(t => t.TopicName).Order(StringComparer.Ordinal).SequenceEqual(topicNames.Order(StringComparer.Ordinal)))
            return EventDeliveryRollbackReadiness.Unavailable;
        var topics = await lagObserver.ObserveTopicBindingsAsync(kafkaOptions.Value, topicNames, cancellationToken).ConfigureAwait(false);
        var matches = KafkaDeliveryTopology.Matches(baseline.Topics, topics);
        var resources = new RollbackControlResources(new Uri(options.ConnectBaseUri!).AbsoluteUri, baseline.ConnectorName,
            baseline.TopicName, baseline.ConsumerGroups, baseline.RetryStages, environment.EnvironmentName, baseline.ConfigurationFingerprint, baseline.KafkaClusterId)
        { Topics = matches ? topics! : baseline.Topics, ActivationId = baseline.ActivationId };
        if (!await journal.BindResourcesAsync(rollbackGeneration,
                JsonSerializer.Serialize(resources, KafkaMessagingJsonSerializerContext.Default.RollbackControlResources), cancellationToken).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        // 已知资源失配也须留下原基线，使自动恢复不能把同名新 Topic 当作原链路而解除 producer fence。
        if (!matches || baseline.KafkaClusterId != clusterId || baseline.ConnectorName != binding.ConnectorName || baseline.TopicName != binding.TopicName
            || baseline.ConfigurationFingerprint != configurationFingerprint || !baseline.ConsumerGroups.SequenceEqual(groups)
            || !baseline.RetryStages.SequenceEqual(kafkaOptions.Value.RetryStages)) return EventDeliveryRollbackReadiness.Unavailable;

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
            if (position is not null && await CoversFenceAsync(position).ConfigureAwait(false)) break;
            await Task.Delay(poll, token).ConfigureAwait(false);
        }
        var currentConfiguration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (currentConfiguration is null || KafkaConnectResourceIdentity.Compute(currentConfiguration) != resources.ConfigurationFingerprint)
            return EventDeliveryRollbackReadiness.Unavailable;
        if (resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        if (!await TopicsUnchangedAsync(token).ConfigureAwait(false)) return EventDeliveryRollbackReadiness.Unavailable;
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        if (!await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.Prepared,
                EventDeliveryRollbackPhase.PauseRequested, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        if ((await journal.FindAsync(rollbackGeneration, token).ConfigureAwait(false))?.Phase != EventDeliveryRollbackPhase.PauseRequested)
            return EventDeliveryRollbackReadiness.Unavailable;
        await connectAdmin.PauseConnectorAsync(resources.ConnectorName, token).ConfigureAwait(false);
        while (!await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false))
            await Task.Delay(poll, token).ConfigureAwait(false);
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        if (!await journal.TransitionAsync(rollbackGeneration, EventDeliveryRollbackPhase.PauseRequested,
                EventDeliveryRollbackPhase.Paused, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;

        // Offset 仅在 Inbox/处理完成后提交；连续两轮核对全部组和重试 Topic，未知值不等同于零。
        var stableRounds = 0;
        while (stableRounds < 2)
        {
            await lease.EnsureHeldAsync(token).ConfigureAwait(false);
            if (!await TopicsUnchangedAsync(token).ConfigureAwait(false)) return EventDeliveryRollbackReadiness.Unavailable;
            var drained = true;
            foreach (var group in resources.ConsumerGroups)
                foreach (var target in resources.Topics)
                    drained &= await lagObserver.IsDrainedAsync(kafkaOptions.Value, target, group, token).ConfigureAwait(false);
            stableRounds = drained ? stableRounds + 1 : 0;
            if (stableRounds < 2) await Task.Delay(poll, token).ConfigureAwait(false);
        }
        position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (position is null || !await CoversFenceAsync(position).ConfigureAwait(false)
            || resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(token).ConfigureAwait(false)
            || resources.KafkaClusterId != await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, token).ConfigureAwait(false)
            || !await TopicsUnchangedAsync(token).ConfigureAwait(false)
            || !await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false))
            return EventDeliveryRollbackReadiness.Unavailable;
        return new(rollbackGeneration, true, true, true, fence.ProducerFencePosition.ToJson(), position.ToJson(),
            rollbackGeneration.ToString("N"), fence.LastPublishedEventId, DateTimeOffset.UtcNow);

        async Task<bool> CoversFenceAsync(CdcDeliveryPosition observed) =>
            CdcDeliveryPosition.ConnectorCoversProducerFence(fence.ProducerFencePosition, observed)
            || await fenceReader.IsSameTransactionCoveredAsync(eventType, schemaVersion, rollbackGeneration,
                fence.ProducerFencePosition, observed, token).ConfigureAwait(false);

        async Task<bool> TopicsUnchangedAsync(CancellationToken queryToken) => KafkaDeliveryTopology.Matches(resources.Topics,
            await lagObserver.ObserveTopicBindingsAsync(kafkaOptions.Value, topicNames, queryToken).ConfigureAwait(false));
    }

    public async Task AbortAsync(string eventType, int schemaVersion, Guid rollbackGeneration, EventDeliveryControlLease lease, CancellationToken cancellationToken = default)
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
        if (!CdcKafkaDeliveryBinding.TryParse(owner.CdcKafkaBindingJson, out var baseline) || baseline is null
            || baseline.ActivationId == Guid.Empty || resources.ActivationId != baseline.ActivationId
            || resources.ConnectBaseUri != baseline.ConnectBaseUri || resources.EnvironmentName != baseline.EnvironmentName)
            throw new InvalidOperationException("恢复资源不属于原切流激活，保留生产者保护。");
        var options = rollbackOptions.Value;
        var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var currentGroups = catalog.GetAllSubscriptions()
            .Where(item => item.EventType == eventType && item.SchemaVersion == schemaVersion).Select(item => item.ConsumerName)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!options.Enabled || !Uri.TryCreate(options.ConnectBaseUri, UriKind.Absolute, out var currentUri)
            || resources.ConnectBaseUri != currentUri.AbsoluteUri || resources.EnvironmentName != environment.EnvironmentName
            || !resources.RetryStages.SequenceEqual(kafkaOptions.Value.RetryStages) || !resources.ConsumerGroups.SequenceEqual(currentGroups)
            || catalog.GetTopicRequired(eventType, schemaVersion).TopicCode != resources.TopicName)
            throw new InvalidOperationException("控制面地址已改变；必须恢复原配置后才能恢复原操作。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.PrepareTimeoutSeconds));
        if (resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(deadline.Token).ConfigureAwait(false)
            || resources.KafkaClusterId != await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, deadline.Token).ConfigureAwait(false))
            throw new InvalidOperationException("原 Connect 的 Kafka 集群身份已改变；保留保护。");
        var configuration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (configuration is null || KafkaConnectResourceIdentity.Compute(configuration) != resources.ConfigurationFingerprint)
            throw new InvalidOperationException("原 Connector 配置已改变；禁止恢复同名但不同资源的发布者。");
        var sourceDatabase = configuration.GetValueOrDefault(configuration.GetValueOrDefault("connector.class")
            == "io.debezium.connector.sqlserver.SqlServerConnector" ? "database.names" : "database.include.list");
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var sourceHealth = scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>();
        await RequireTopicsAsync().ConfigureAwait(false);
        await RequireRecoverableSourceAsync().ConfigureAwait(false);

        async Task RequireTopicsAsync()
        {
            if (!KafkaDeliveryTopology.Matches(resources.Topics, await lagObserver.ObserveTopicBindingsAsync(kafkaOptions.Value,
                resources.Topics.Select(t => t.TopicName).ToArray(), deadline.Token).ConfigureAwait(false)))
                throw new InvalidOperationException("原正式或重试 Topic 身份、分区或日志窗口已改变；保留 producer fence，禁止以同名新资源恢复。");
        }
        await lease.EnsureHeldAsync(deadline.Token).ConfigureAwait(false);
        if ((await journal.FindAsync(rollbackGeneration, deadline.Token).ConfigureAwait(false))?.Phase != EventDeliveryRollbackPhase.Recovering)
            throw new InvalidOperationException("恢复代次已不再可执行。");
        if (operation.HasPauseIntent)
            await connectAdmin.ResumeConnectorAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (!await connectAdmin.WaitForConnectorHealthyAsync(resources.ConnectorName,
                TimeSpan.FromSeconds(options.PrepareTimeoutSeconds), deadline.Token).ConfigureAwait(false))
            throw new InvalidOperationException("恢复请求已提交，但未确认全部 Connector 任务 RUNNING；保留生产者保护。");
        // RUNNING 可早于首次源读取；再次核对真实窗口，防止恢复期间清理日志后错误解除 producer fence。
        await RequireTopicsAsync().ConfigureAwait(false);
        await RequireRecoverableSourceAsync().ConfigureAwait(false);
        // 等待首个位点期间资源可能被替换；解除保护前重新验证身份。
        configuration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
        if (configuration is null || KafkaConnectResourceIdentity.Compute(configuration) != resources.ConfigurationFingerprint
            || resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(deadline.Token).ConfigureAwait(false)
            || resources.KafkaClusterId != await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, deadline.Token).ConfigureAwait(false))
            throw new InvalidOperationException("恢复期间 Connector 或 Kafka 集群身份已改变；保留生产者保护。");
        await lease.EnsureHeldAsync(deadline.Token).ConfigureAwait(false);

        async Task RequireRecoverableSourceAsync()
        {
            var position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
            // 尚未请求暂停时，首次心跳及 Connect offset flush 可能仍在路上；只等待真实提交位点，绝不合成坐标。
            while (position is null && !operation.HasPauseIntent)
            {
                await lease.EnsureHeldAsync(deadline.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(options.DrainPollIntervalMilliseconds), deadline.Token).ConfigureAwait(false);
                position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, deadline.Token).ConfigureAwait(false);
            }
            if (position is null || string.IsNullOrWhiteSpace(sourceDatabase)
                || (await sourceHealth.ReadSourcePositionAsync(sourceDatabase, position, deadline.Token).ConfigureAwait(false)).Recoverable != true)
                throw new InvalidOperationException("原 Connector 的已提交源位点不可恢复或无法确认；保留生产者保护，禁止用新基线冒充恢复。");
        }
    }

    /// <summary>锁外仅观察是否存在物理偏差；健康时不竞争人工控制锁，任何写入仍须在锁内完整重验。</summary>
    internal async Task<bool> NeedsReconciliationAsync(string eventType, int schemaVersion, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var store = scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>();
        if (await store.IsRollbackActiveAsync(eventType, schemaVersion, token).ConfigureAwait(false)) return false;
        var owner = await store.FindAsync(eventType, schemaVersion, token).ConfigureAwait(false);
        if (!CdcKafkaDeliveryBinding.TryParse(owner?.CdcKafkaBindingJson, out var baseline) || baseline is null || baseline.ActivationId == Guid.Empty) return false;
        if (owner!.CurrentOwner == EventDeliveryOwner.CdcKafka)
            return await connectAdmin.IsConnectorPausedAsync(baseline.ConnectorName, token).ConfigureAwait(false);
        if (owner.CurrentOwner != EventDeliveryOwner.LegacyPolling) return false;
        var operation = await scope.ServiceProvider.GetRequiredService<IEventDeliveryRollbackJournal>()
            .FindCurrentTerminalAsync(eventType, schemaVersion, token).ConfigureAwait(false);
        return operation is { Phase: EventDeliveryRollbackPhase.Completed, HasPauseIntent: true, ResourcesJson: not null }
            && !await connectAdmin.IsConnectorPausedAsync(baseline.ConnectorName, token).ConfigureAwait(false);
    }

    /// <summary>重放当前持久控制目标，修复已经在途的迟到 REST；不修改所有者、代次或生产者 fence。</summary>
    internal async Task<bool> ReconcileAsync(string eventType, int schemaVersion, EventDeliveryControlLease lease, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var journal = scope.ServiceProvider.GetRequiredService<IEventDeliveryRollbackJournal>();
        var store = scope.ServiceProvider.GetRequiredService<IEventStreamOwnershipStore>();
        if (await store.IsRollbackActiveAsync(eventType, schemaVersion, token).ConfigureAwait(false)) return false;
        var operation = await journal.FindCurrentTerminalAsync(eventType, schemaVersion, token).ConfigureAwait(false);
        var owner = await store.FindAsync(eventType, schemaVersion, token).ConfigureAwait(false);
        if (owner is null || !CdcKafkaDeliveryBinding.TryParse(owner.CdcKafkaBindingJson, out var baseline)
            || baseline is null || baseline.ActivationId == Guid.Empty) return false;
        var paused = owner.CurrentOwner == EventDeliveryOwner.LegacyPolling;
        RollbackControlResources resources;
        if (paused)
        {
            // Legacy 只重申所有权行关联的 Completed；新的切流会清除此关联。
            if (operation is not { Phase: EventDeliveryRollbackPhase.Completed, HasPauseIntent: true, ResourcesJson: not null }) return false;
            resources = JsonSerializer.Deserialize(operation.ResourcesJson, KafkaMessagingJsonSerializerContext.Default.RollbackControlResources)
                ?? throw new KafkaControlReconciliationException("resource_snapshot_unavailable");
        }
        else
        {
            if (owner.CurrentOwner != EventDeliveryOwner.CdcKafka || baseline.ConnectBaseUri is null || baseline.EnvironmentName is null) return false;
            // 重新切流后依据新的激活基线收敛 RUNNING，不能靠旧 Recovered 日志的机器时间推断目标。
            resources = new(baseline.ConnectBaseUri, baseline.ConnectorName, baseline.TopicName, baseline.ConsumerGroups,
                baseline.RetryStages, baseline.EnvironmentName, baseline.ConfigurationFingerprint, baseline.KafkaClusterId)
            { Topics = baseline.Topics, ActivationId = baseline.ActivationId };
        }
        var options = rollbackOptions.Value;
        var binding = options.Streams.SingleOrDefault(item => item.EventType == eventType && item.SchemaVersion == schemaVersion);
        var catalog = scope.ServiceProvider.GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var groups = catalog.GetAllSubscriptions().Where(item => item.EventType == eventType && item.SchemaVersion == schemaVersion)
            .Select(item => item.ConsumerName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!options.Enabled || !kafkaOptions.Value.Enabled || binding is null
            || options.Streams.Count(item => item.ConnectorName == binding.ConnectorName) != 1
            || binding.ConnectorName != resources.ConnectorName || binding.TopicName != resources.TopicName
            || resources.ConnectBaseUri != new Uri(options.ConnectBaseUri!).AbsoluteUri || resources.EnvironmentName != environment.EnvironmentName
            || resources.ConnectBaseUri != baseline.ConnectBaseUri || resources.EnvironmentName != baseline.EnvironmentName
            || resources.ActivationId != baseline.ActivationId
            || resources.ConnectorName != baseline.ConnectorName || resources.TopicName != baseline.TopicName
            || resources.ConfigurationFingerprint != baseline.ConfigurationFingerprint || resources.KafkaClusterId != baseline.KafkaClusterId
            || !resources.ConsumerGroups.SequenceEqual(groups) || !resources.ConsumerGroups.SequenceEqual(baseline.ConsumerGroups)
            || !resources.RetryStages.SequenceEqual(kafkaOptions.Value.RetryStages) || !resources.RetryStages.SequenceEqual(baseline.RetryStages)
            || catalog.GetTopicRequired(eventType, schemaVersion).TopicCode != resources.TopicName
            || !KafkaDeliveryTopology.Matches(baseline.Topics, resources.Topics))
            throw new KafkaControlReconciliationException("resource_mapping_changed");
        await EnsureOtherStreamsIsolatedAsync().ConfigureAwait(false);
        if (resources.KafkaClusterId != await connectAdmin.TryReadKafkaClusterIdAsync(token).ConfigureAwait(false)
            || resources.KafkaClusterId != await lagObserver.ObserveClusterIdAsync(kafkaOptions.Value, token).ConfigureAwait(false)
            || !KafkaDeliveryTopology.Matches(resources.Topics, await lagObserver.ObserveTopicBindingsAsync(kafkaOptions.Value,
                resources.Topics.Select(item => item.TopicName).ToArray(), token).ConfigureAwait(false)))
            throw new KafkaControlReconciliationException("kafka_identity_or_window_changed");
        var configuration = await connectAdmin.TryReadConnectorConfigurationAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (configuration is null || KafkaConnectResourceIdentity.Compute(configuration) != resources.ConfigurationFingerprint
            || !KafkaConnectResourceIdentity.IsTargetRoute(configuration, binding))
            throw new KafkaControlReconciliationException("connector_configuration_changed");
        if (paused && await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false)) return false;
        if (!paused)
        {
            // 只纠正明确 PAUSED 的 Connector；FAILED/未知不自动 resume，不掩盖源日志丢失。
            if (!await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false)) return false;
            var position = await connectAdmin.TryReadConnectorPositionAsync(resources.ConnectorName, token).ConfigureAwait(false);
            if (position is null || (await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>()
                .ReadSourcePositionAsync(binding.SourceDatabaseName, position, token).ConfigureAwait(false)).Recoverable != true)
                throw new KafkaControlReconciliationException("source_not_recoverable");
        }
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        var latest = await journal.FindCurrentTerminalAsync(eventType, schemaVersion, token).ConfigureAwait(false);
        if (await store.IsRollbackActiveAsync(eventType, schemaVersion, token).ConfigureAwait(false)
            || (paused && (latest?.Id != operation!.Id || latest.Phase != operation.Phase))
            || await store.FindAsync(eventType, schemaVersion, token).ConfigureAwait(false) != owner) return false;
        // 同锁内再次读取全部活动所有者；不能沿用慢速物理探测前的另一流激活/资源快照。
        await EnsureOtherStreamsIsolatedAsync().ConfigureAwait(false);
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        if (paused) await connectAdmin.PauseConnectorAsync(resources.ConnectorName, token).ConfigureAwait(false);
        else await connectAdmin.ResumeConnectorAsync(resources.ConnectorName, token).ConfigureAwait(false);
        if (paused)
        {
            while (!await connectAdmin.IsConnectorPausedAsync(resources.ConnectorName, token).ConfigureAwait(false))
                await Task.Delay(options.DrainPollIntervalMilliseconds, token).ConfigureAwait(false);
        }
        else if (!await connectAdmin.WaitForConnectorHealthyAsync(resources.ConnectorName, TimeSpan.FromSeconds(options.PrepareTimeoutSeconds), token).ConfigureAwait(false))
            throw new KafkaControlReconciliationException("running_not_confirmed");
        await lease.EnsureHeldAsync(token).ConfigureAwait(false);
        return true;

        async Task EnsureOtherStreamsIsolatedAsync()
        {
            var owners = await store.ListAsync(token).ConfigureAwait(false);
            var effective = await scope.ServiceProvider.GetRequiredService<IEffectiveEventDeliveryOwnerResolver>()
                .GetDeliveryOwnersAsync(scope.ServiceProvider.GetServices<IntegrationEventTopicDefinition>().ToArray(), token).ConfigureAwait(false);
            var others = owners.Where(item => (item.MessageType != eventType || item.SchemaVersion != schemaVersion)
                && (item.CurrentOwner == EventDeliveryOwner.CdcKafka || item.PreviousOwner == EventDeliveryOwner.CdcKafka
                    || item.RollbackOccurredAtUtc is not null || !string.IsNullOrWhiteSpace(item.CdcKafkaBindingJson))).ToArray();
            // 保留历史资源归属以隔离迟到动作；默认CDC和不完整的历史基线都不能漏检。
            if (effective.Any(item => item.Value == EventDeliveryOwner.CdcKafka
                    && (item.Key.EventType != eventType || item.Key.SchemaVersion != schemaVersion)
                    && !others.Any(other => other.CurrentOwner == EventDeliveryOwner.CdcKafka && other.MessageType == item.Key.EventType && other.SchemaVersion == item.Key.SchemaVersion))
                || others.Any(item => !CdcKafkaDeliveryBinding.TryParse(item.CdcKafkaBindingJson, out var other)
                    || other is null || !baseline.IsIsolatedFrom(other)))
                throw new KafkaControlReconciliationException("shared_connector");
        }
    }
}

/// <summary>暂停前冻结资源；恢复时读取原快照，不根据修改后的映射猜测目标。</summary>
internal sealed record RollbackControlResources(string ConnectBaseUri, string ConnectorName, string TopicName,
    string[] ConsumerGroups, string[] RetryStages, string EnvironmentName, string ConfigurationFingerprint, string KafkaClusterId)
{
    /// <summary>所属切流激活身份；相同配置重新切流也不能认领旧代次。</summary>
    public Guid ActivationId { get; init; }
    /// <summary>本操作从切流身份基线核验后冻结的完整窗口，必须进入源生成 JSON。</summary>
    public CdcKafkaTopicBinding[] Topics { get; init; } = [];
}

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

    /// <summary>只接受按原始行精确过滤类型和版本的专用Outbox路由。</summary>
    internal static bool IsTargetRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding)
        => IsRoute(config, binding, shadow: false);

    internal static bool IsShadowRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding)
        => IsRoute(config, binding, shadow: true);

    private static bool IsRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding, bool shadow)
    {
        if (string.IsNullOrWhiteSpace(binding.SourceDatabaseName) || !string.IsNullOrWhiteSpace(config.GetValueOrDefault("producer.override.bootstrap.servers"))) return false;
        // Connect 容忍转换错误会提交被丢弃记录的源位点，不能用于证明业务消息已到达 Broker。
        if (config.TryGetValue("errors.tolerance", out var tolerance) && tolerance != "none") return false;
        if (config.TryGetValue("event.processing.failure.handling.mode", out var failureMode) && failureMode != "fail") return false;
        // 不得允许 Debezium 在进入过滤器之前跳过 Outbox 插入记录。
        if (config.TryGetValue("skipped.operations", out var skipped) && skipped is not ("" or "t" or "none")) return false;
        if (string.IsNullOrWhiteSpace(config.GetValueOrDefault("topic.heartbeat.prefix"))
            || config.ContainsKey("heartbeat.topics.prefix")) return false;
        var sqlServer = config.GetValueOrDefault("connector.class") == "io.debezium.connector.sqlserver.SqlServerConnector";
        var mySql = config.GetValueOrDefault("connector.class") == "io.debezium.connector.mysql.MySqlConnector";
        // SQL Server 空闲时仅允许受控单行心跳事务；禁止通过 Connector 配置执行任意 SQL。
        const string sqlServerHeartbeat = "UPDATE dbo.fn_messaging_cdc_heartbeat SET UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = '01941f29-7c00-7000-8000-000000000001'";
        var action = config.GetValueOrDefault("heartbeat.action.query");
        if (sqlServer ? action != sqlServerHeartbeat : !string.IsNullOrWhiteSpace(action)) return false;
        // 两库捕获表静默时均须提交真实已读位点；仅设置心跳Topic并不会启用心跳。
        if (config.GetValueOrDefault("heartbeat.interval.ms") != "1000") return false;
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
            ["tasks.max"] = "1", ["snapshot.mode"] = "no_data",
            ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
            ["transforms.outbox.table.field.event.id"] = "Id", ["transforms.outbox.table.field.event.key"] = "PartitionKey",
            ["transforms.outbox.table.field.event.type"] = "MessageType", ["transforms.outbox.table.field.event.payload"] = "Payload",
            ["transforms.outbox.route.by.field"] = "MessageType",
            ["transforms.outbox.table.op.invalid.behavior"] = "fatal",
        };
        if (required.Any(item => config.GetValueOrDefault(item.Key) != item.Value)) return false;
        if (!IsDedicatedTopicRoute(config, binding, shadow)) return false;
        var placements = config.GetValueOrDefault("transforms.outbox.table.fields.additional.placement", "").Split(',', StringSplitOptions.TrimEntries);
        // SQL Server datetimeoffset 是 ZonedTimestamp 字符串，不能作为 EventRouter 的整数时间字段。
        // 两库均保留精确业务时间头；MySQL 使用发生时间定位，SQL Server 保持默认 CDC 处理时间。
        if (sqlServer && config.ContainsKey("transforms.outbox.table.field.event.timestamp")) return false;
        if (shadow && mySql && config.GetValueOrDefault("transforms.outbox.table.field.event.timestamp") != "OccurredAtUtc") return false;
        var expectedPlacements = new HashSet<string>(new[] { "Id:header:event_id", "MessageType:header:message_type", "SchemaVersion:header:schema_version", "ContentType:header:content_type",
            "TenantId:header:tenant_id", "CorrelationId:header:correlation_id", "CausationId:header:causation_id", "TraceParent:header:trace_parent",
            "Producer:header:producer", "OccurredAtUtc:header:occurred_at_utc" }, StringComparer.Ordinal);
        if (shadow) expectedPlacements.Add("CaptureGeneration:header:capture_generation");
        // 重复头可能遮蔽权威值，额外 envelope 映射会改变原始业务载荷字节。
        return placements.Length == expectedPlacements.Count && expectedPlacements.SetEquals(placements);
    }

    /// <summary>多流同表时仅允许规范类型投影和固定Topic白名单，同时保留本Connector的真实心跳。</summary>
    private static bool IsDedicatedTopicRoute(IReadOnlyDictionary<string, string> config, KafkaConnectRollbackStreamBinding binding, bool shadow)
    {
        var topic = shadow ? binding.ShadowTopicName : binding.TopicName;
        var sqlServer = config.GetValueOrDefault("connector.class") == "io.debezium.connector.sqlserver.SqlServerConnector";
        var source = config.GetValueOrDefault("topic.prefix") + "." + binding.SourceDatabaseName
            + (sqlServer ? ".dbo." : ".") + (shadow ? "fn_messaging_shadow_event" : "fn_messaging_outbox_event");
        if (string.IsNullOrWhiteSpace(config.GetValueOrDefault("topic.prefix"))
            || config.GetValueOrDefault("transforms.fullnet_filter.type") != "net.full.connect.FullNetOutboxStreamFilter"
            || config.GetValueOrDefault("transforms.fullnet_filter.source.topic") != source
            || config.GetValueOrDefault("transforms.fullnet_filter.event.type") != binding.EventType
            || config.GetValueOrDefault("transforms.fullnet_filter.schema.version") != binding.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || config.Keys.Any(key => key.StartsWith("transforms.fullnet_filter.predicate", StringComparison.Ordinal)
                || key.StartsWith("transforms.fullnet_filter.negate", StringComparison.Ordinal)
                || key.StartsWith("transforms.outbox.predicate", StringComparison.Ordinal))) return false;
        if (config.GetValueOrDefault("transforms") == "fullnet_filter,outbox")
            // EventRouter 通过正则匹配路由字段后才应用固定替换；任意正则可能令消息绕过目标 Topic。
            return config.GetValueOrDefault("transforms.outbox.route.topic.replacement") == topic
                && (!config.TryGetValue("transforms.outbox.route.topic.regex", out var routeRegex)
                    || routeRegex is "(?<routedByValue>.*)" or ".*");
        if (config.GetValueOrDefault("transforms") != "fullnet_filter,outbox,select"
            || !binding.EventType.StartsWith("fullnet.", StringComparison.Ordinal) || binding.SchemaVersion <= 0)
            return false;
        var suffix = binding.EventType["fullnet.".Length..] + ".v" + binding.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!topic.EndsWith(suffix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(config.GetValueOrDefault("topic.prefix"))) return false;
        var heartbeat = config["topic.heartbeat.prefix"] + "." + config["topic.prefix"];
        var expected = new Dictionary<string, string>
        {
            ["transforms.outbox.route.topic.regex"] = @"^fullnet\.(.*)$",
            ["transforms.outbox.route.topic.replacement"] = topic[..^suffix.Length] + "$1.v" + binding.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["predicates"] = "selected",
            ["predicates.selected.type"] = "org.apache.kafka.connect.transforms.predicates.TopicNameMatches",
            ["predicates.selected.pattern"] = "^(?:" + System.Text.RegularExpressions.Regex.Escape(topic) + "|" + System.Text.RegularExpressions.Regex.Escape(heartbeat) + ")$",
            ["transforms.select.type"] = "org.apache.kafka.connect.transforms.Filter",
            ["transforms.select.predicate"] = "selected", ["transforms.select.negate"] = "true",
        };
        // EventRouter不能再被额外谓词绕过；过滤规则和顺序全部固定，不能只检查最终Topic字符串。
        return expected.All(item => config.GetValueOrDefault(item.Key) == item.Value);
    }

    private static bool IsIdentityKey(string key) => key.StartsWith("transforms.", StringComparison.Ordinal)
        || key.StartsWith("predicates.", StringComparison.Ordinal) || key == "predicates"
        || key.StartsWith("value.converter.delegate.", StringComparison.Ordinal)
        || key.StartsWith("heartbeat.", StringComparison.Ordinal)
        || key is "topic.heartbeat.prefix"
        || key is "errors.tolerance" or "event.processing.failure.handling.mode" or "skipped.operations" or "connector.class" or "tasks.max" or "database.hostname" or "database.port" or "database.names"
            or "database.include.list" or "database.server.id" or "database.user" or "table.include.list" or "topic.prefix"
            or "snapshot.mode" or "capture.instance" or "transforms" or "key.converter" or "key.converter.schemas.enable"
            or "value.converter" or "value.converter.schemas.enable" or "schema.history.internal.kafka.topic"
            or "schema.history.internal.kafka.bootstrap.servers" or "producer.override.bootstrap.servers";
}
