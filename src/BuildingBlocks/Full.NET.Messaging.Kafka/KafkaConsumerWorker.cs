using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// Kafka Poll 循环在数据库事务外执行；Inbox 事务由 <see cref="IntegrationEventConsumerDispatcher"/> 负责。
/// </summary>
internal sealed class KafkaConsumerWorker : BackgroundService
{
    private const string ProviderCode = "kafka";

    private readonly KafkaMessagingOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaConsumerMessageProcessor _messageProcessor;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    public KafkaConsumerWorker(
        IOptions<KafkaMessagingOptions> options,
        IServiceScopeFactory scopeFactory,
        KafkaConsumerMessageProcessor messageProcessor,
        ILogger<KafkaConsumerWorker> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _messageProcessor = messageProcessor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var consumerGroups = BuildConsumerGroups();
        if (consumerGroups.Count == 0)
        {
            _logger.LogWarning("Kafka messaging is enabled but no integration event subscriptions are registered.");
            return;
        }
        var workers = consumerGroups
            .Select(group => RunConsumerGroupAsync(group, stoppingToken))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private IReadOnlyList<ConsumerGroupPlan> BuildConsumerGroups()
    {
        using var scope = _scopeFactory.CreateScope();
        // 修复意图：KafkaConsumerWorker 是 Singleton HostedService，不得在字段或构造函数中直接持有
        // Scoped catalog/dispatcher/订阅；必须每次通过 IServiceScopeFactory 创建独立作用域解析。
        // 这里同时使用接口 IIntegrationEventSubscriptionCatalog，以便空目录默认值实现能参与解析。
        var catalog = scope.ServiceProvider
            .GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var subscriptions = scope.ServiceProvider
            .GetServices<IIntegrationEventSubscription>();
        var plans = new Dictionary<string, ConsumerGroupPlan>(StringComparer.Ordinal);
        foreach (var subscription in subscriptions)
        {
            var topic = catalog.GetTopicRequired(
                subscription.EventType,
                subscription.SchemaVersion);
            if (!plans.TryGetValue(subscription.ConsumerName, out var plan))
            {
                plan = new ConsumerGroupPlan(subscription.ConsumerName);
                plans[subscription.ConsumerName] = plan;
            }

            plan.AddRoute(subscription, topic.TopicCode);
            foreach (var retryStage in _options.RetryStages)
            {
                plan.TopicCodes.Add(
                    KafkaTopicNames.GetRetryTopic(topic.TopicCode, retryStage));
            }
        }

        return plans.Values.ToArray();
    }

    private async Task RunConsumerGroupAsync(
        ConsumerGroupPlan plan,
        CancellationToken stoppingToken)
    {
        KafkaConsumerPartitionCoordinator? coordinator = null;
        var builder = new ConsumerBuilder<string, byte[]>(
                _options.BuildConsumerConfig(plan.ConsumerName))
            .SetPartitionsAssignedHandler((_, partitions) =>
                coordinator?.OnAssigned(partitions))
            .SetPartitionsRevokedHandler((_, offsets) =>
                coordinator?.OnRevoked(offsets.Select(offset => offset.TopicPartition)))
            .SetPartitionsLostHandler((_, offsets) =>
                coordinator?.OnLost(offsets.Select(offset => offset.TopicPartition)));
        using var consumer = builder.Build();
        await using var scheduler = new KafkaPartitionWorkScheduler(
            async (consumeResult, partitionCancellationToken) =>
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    partitionCancellationToken);
                KafkaDeliveryHeaders.TryReadHeader(
                    consumeResult.Message.Headers,
                    KafkaEnvelopeHeaderNames.TraceParent,
                    out var traceParent);
                using var activity = KafkaMessagingTelemetry.StartConsumeActivity(
                    plan.ResolveTopicCode(consumeResult.Topic),
                    plan.ConsumerName,
                    consumeResult.Partition.Value,
                    consumeResult.Offset.Value,
                    traceParent);
                return await _messageProcessor.ProcessScheduledMessageAsync(
                        plan,
                        consumeResult,
                        linkedCancellation.Token)
                    .ConfigureAwait(false);
            },
            _options,
            (sequence, inflight, bufferDepth) => KafkaMessagingTelemetry.UpdateProcessingState(
                ProviderCode,
                plan.ConsumerName,
                sequence,
                inflight,
                bufferDepth));
        coordinator = new KafkaConsumerPartitionCoordinator(
            consumer,
            scheduler,
            _options,
            plan.ConsumerName,
            _logger);
        consumer.Subscribe(plan.TopicCodes);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
                coordinator.ResumeDuePartitions(DateTimeOffset.UtcNow);

                ConsumeResult<string, byte[]>? consumeResult;
                try
                {
                    // Handler 在独立分区通道运行；短 Poll 同时负责 Heartbeat、Rebalance 和完成命令泵。
                    consumeResult = consumer.Consume(
                        KafkaConsumerPollTiming.Resolve(
                            _options,
                            scheduler.InFlightCount,
                            scheduler.HasPendingCompletion));
                }
                catch (ConsumeException exception) when (exception.Error.IsFatal)
                {
                    _logger.LogError(exception, "Kafka consumer fatal error for group {ConsumerName}.", plan.ConsumerName);
                    throw;
                }
                catch (ConsumeException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Kafka consumer recoverable poll error for group {ConsumerName}; polling will continue.",
                        plan.ConsumerName);
                    continue;
                }
                if (consumeResult?.Message is null)
                {
                    continue;
                }

                if (!coordinator.TryDispatch(consumeResult))
                {
                    throw new InvalidOperationException(
                        $"Kafka partition '{consumeResult.TopicPartition}' accepted a second "
                        + "delivery while its bounded processing lane was occupied.");
                }
            }
        }
        finally
        {
            var drained = await scheduler
                .StopAsync(TimeSpan.FromSeconds(_options.ShutdownDrainSeconds))
                .ConfigureAwait(false);
            coordinator.ProcessCompletions(DateTimeOffset.UtcNow);
            coordinator.OnRevoked(consumer.Assignment);
            if (!drained)
            {
                _logger.LogWarning(
                    "Kafka in-flight partition processing exceeded the shutdown drain timeout for group {ConsumerName}.",
                    plan.ConsumerName);
            }

            try
            {
                consumer.Close();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Kafka consumer close failed for group {ConsumerName}.", plan.ConsumerName);
            }

            KafkaMessagingTelemetry.RemoveConsumerState(plan.ConsumerName);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdown.CancelAfter(TimeSpan.FromSeconds(_options.ShutdownDrainSeconds));
        try
        {
            await base.StopAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            _logger.LogWarning("Kafka consumer worker shutdown reached the drain timeout.");
        }
    }

    internal static IIntegrationEventSubscription ResolveSubscription(
        IServiceProvider serviceProvider,
        IIntegrationEventSubscriptionCatalog catalog,
        string consumerName,
        string messageType,
        int schemaVersion)
    {
        foreach (var registry in serviceProvider.GetServices<IIntegrationEventHandlerRegistry>())
        {
            if (registry.TryResolve(
                    messageType,
                    schemaVersion,
                    consumerName,
                    out var descriptor))
            {
                return catalog.GetByHandlerTypeRequired(descriptor.HandlerType);
            }
        }

        // 插件和测试订阅可暂时使用显式 Catalog；生产模块优先走编译期注册表。
        return catalog.GetRequired(consumerName, messageType, schemaVersion);
    }

    private sealed class ConsumerGroupPlan : IKafkaConsumerRoutePlan
    {
        private readonly HashSet<(string EventType, int SchemaVersion)> _routes = [];
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            (string EventType, int SchemaVersion), byte> _revokedRoutes = [];

        public ConsumerGroupPlan(string consumerName)
        {
            ConsumerName = consumerName;
        }

        public string ConsumerName { get; }

        public HashSet<string> TopicCodes { get; } = new(StringComparer.Ordinal);

        public bool HasOwnershipRevoked => !_revokedRoutes.IsEmpty;

        public void AddRoute(IIntegrationEventSubscription subscription, string topicCode)
        {
            TopicCodes.Add(topicCode);
            _routes.Add((subscription.EventType, subscription.SchemaVersion));
        }

        public bool ContainsRoute(
            string eventType,
            int schemaVersion) =>
            _routes.Contains((eventType, schemaVersion));

        public void SetOwnershipRevoked(
            string eventType,
            int schemaVersion,
            bool revoked)
        {
            var route = (eventType, schemaVersion);
            if (revoked)
            {
                _revokedRoutes.TryAdd(route, 0);
            }
            else
            {
                _revokedRoutes.TryRemove(route, out _);
            }
        }

        public string ResolveTopicCode(string topic) => KafkaTopicNames.ResolveBaseTopic(topic);
    }
}
