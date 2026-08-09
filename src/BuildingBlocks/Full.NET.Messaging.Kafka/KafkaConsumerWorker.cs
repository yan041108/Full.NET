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
    private readonly KafkaEnvelopeReader _reader;
    private readonly KafkaOffsetCommitter _committer;
    private readonly KafkaFailureClassifier _failureClassifier;
    private readonly KafkaRetryRouter _retryRouter;
    private readonly KafkaDeadLetterPublisher _deadLetterPublisher;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    public KafkaConsumerWorker(
        IOptions<KafkaMessagingOptions> options,
        IServiceScopeFactory scopeFactory,
        KafkaEnvelopeReader reader,
        KafkaOffsetCommitter committer,
        KafkaFailureClassifier failureClassifier,
        KafkaRetryRouter retryRouter,
        KafkaDeadLetterPublisher deadLetterPublisher,
        ILogger<KafkaConsumerWorker> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _reader = reader;
        _committer = committer;
        _failureClassifier = failureClassifier;
        _retryRouter = retryRouter;
        _deadLetterPublisher = deadLetterPublisher;
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
        var catalog = scope.ServiceProvider
            .GetRequiredService<IntegrationEventSubscriptionCatalog>();
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
        }

        return plans.Values.ToArray();
    }

    private async Task RunConsumerGroupAsync(
        ConsumerGroupPlan plan,
        CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(
                _options.BuildConsumerConfig(plan.ConsumerName))
            .Build();
        consumer.Subscribe(plan.TopicCodes);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? consumeResult;
                try
                {
                    consumeResult = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException exception) when (exception.Error.IsFatal)
                {
                    _logger.LogError(exception, "Kafka consumer fatal error for group {ConsumerName}.", plan.ConsumerName);
                    throw;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (consumeResult?.Message is null)
                {
                    continue;
                }

                await ProcessMessageAsync(consumer, plan, consumeResult, stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Kafka consumer close failed for group {ConsumerName}.", plan.ConsumerName);
            }
        }
    }

    private async Task ProcessMessageAsync(
        IConsumer<string, byte[]> consumer,
        ConsumerGroupPlan plan,
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken cancellationToken)
    {
        var topicCode = plan.ResolveTopicCode(consumeResult.Topic);
        if (!_reader.TryRead(consumeResult, out var envelope, out var failureCode)
            || envelope is null)
        {
            var failure = _failureClassifier.Classify(
                new InvalidOperationException("Envelope rejected."),
                failureCode);
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                "unknown",
                "contract_rejected",
                failure.Code);
            await HandleDeliveryFailureAsync(
                    consumer,
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!plan.ContainsRoute(envelope.MessageType, envelope.SchemaVersion))
        {
            var failure = new IntegrationEventFailure(
                IntegrationEventFailureKind.Security,
                IntegrationEventFailureCodes.SchemaVersionUnknown,
                "No subscription route is registered for the integration event.");
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                envelope.MessageType,
                "route_missing",
                failure.Code);
            await HandleDeliveryFailureAsync(
                    consumer,
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var catalog = scope.ServiceProvider
                .GetRequiredService<IntegrationEventSubscriptionCatalog>();
            var subscription = catalog.GetRequired(
                plan.ConsumerName,
                envelope.MessageType,
                envelope.SchemaVersion);
            var dispatcher = scope.ServiceProvider
                .GetRequiredService<IntegrationEventConsumerDispatcher>();
            var inboxResult = await dispatcher
                .ConsumeAsync(plan.ConsumerName, envelope, subscription, cancellationToken)
                .ConfigureAwait(false);

            if (_committer.TryCommit(consumer, consumeResult, inboxResult))
            {
                KafkaMessagingTelemetry.RecordCommit(
                    ProviderCode,
                    topicCode,
                    plan.ConsumerName,
                    envelope.MessageType,
                    inboxResult.Status == InboxConsumeStatus.Processed ? "committed" : "already_processed");
                KafkaMessagingTelemetry.RecordConsume(
                    ProviderCode,
                    topicCode,
                    plan.ConsumerName,
                    envelope.MessageType,
                    inboxResult.Status == InboxConsumeStatus.Processed ? "processed" : "already_processed");
            }
        }
        catch (IntegrationEventPermanentException exception)
        {
            await HandleDeliveryFailureAsync(
                    consumer,
                    plan,
                    consumeResult,
                    envelope,
                    exception.Failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = _failureClassifier.Classify(exception);
            await HandleDeliveryFailureAsync(
                    consumer,
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleDeliveryFailureAsync(
        IConsumer<string, byte[]> consumer,
        ConsumerGroupPlan plan,
        ConsumeResult<string, byte[]> consumeResult,
        IntegrationEventEnvelope? envelope,
        IntegrationEventFailure failure,
        CancellationToken cancellationToken)
    {
        var topicCode = plan.ResolveTopicCode(consumeResult.Topic);
        var messageType = envelope?.MessageType ?? "unknown";
        var attemptCount = KafkaDeliveryHeaders.ReadAttemptCount(consumeResult.Message.Headers);

        if (_failureClassifier.ShouldRetry(failure)
            && _retryRouter.GetNextRetryTopic(consumeResult.Topic, attemptCount) is not null)
        {
            if (await _retryRouter
                    .TryRouteAsync(consumeResult, plan.ConsumerName, failure, attemptCount, cancellationToken)
                    .ConfigureAwait(false))
            {
                consumer.Commit(consumeResult);
                KafkaMessagingTelemetry.RecordConsume(
                    ProviderCode,
                    topicCode,
                    plan.ConsumerName,
                    messageType,
                    "retry_routed",
                    failure.Code);
                return;
            }

            _logger.LogWarning(
                "Transient Kafka failure for consumer {ConsumerName} could not be routed to retry; offset left uncommitted.",
                plan.ConsumerName);
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                messageType,
                "retry_publish_failed",
                failure.Code);
            return;
        }

        if (await _deadLetterPublisher
                .TryPublishAsync(consumeResult, plan.ConsumerName, failure, attemptCount, cancellationToken)
                .ConfigureAwait(false))
        {
            consumer.Commit(consumeResult);
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                messageType,
                "dead_lettered",
                failure.Code);
            _logger.LogWarning(
                "Integration event for consumer {ConsumerName} moved to dead-letter with code {FailureCode}.",
                plan.ConsumerName,
                failure.Code);
            return;
        }

        _logger.LogWarning(
            "Dead-letter publish failed for consumer {ConsumerName} with code {FailureCode}; offset left uncommitted.",
            plan.ConsumerName,
            failure.Code);
        KafkaMessagingTelemetry.RecordConsume(
            ProviderCode,
            topicCode,
            plan.ConsumerName,
            messageType,
            "dead_letter_publish_failed",
            failure.Code);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdown.CancelAfter(TimeSpan.FromSeconds(_options.ShutdownDrainSeconds));
        try
        {
            await base.StopAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Kafka consumer worker shutdown reached the drain timeout.");
        }
    }

    private sealed class ConsumerGroupPlan
    {
        private readonly HashSet<(string EventType, int SchemaVersion)> _routes = [];

        public ConsumerGroupPlan(string consumerName)
        {
            ConsumerName = consumerName;
        }

        public string ConsumerName { get; }

        public HashSet<string> TopicCodes { get; } = new(StringComparer.Ordinal);

        public void AddRoute(IIntegrationEventSubscription subscription, string topicCode)
        {
            TopicCodes.Add(topicCode);
            _routes.Add((subscription.EventType, subscription.SchemaVersion));
        }

        public bool ContainsRoute(
            string eventType,
            int schemaVersion) =>
            _routes.Contains((eventType, schemaVersion));

        public string ResolveTopicCode(string topic) => KafkaTopicNames.ResolveBaseTopic(topic);
    }
}
