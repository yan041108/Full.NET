using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
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
    private readonly IntegrationEventSubscriptionCatalog _catalog;
    private readonly IntegrationEventConsumerDispatcher _dispatcher;
    private readonly IReadOnlyList<IIntegrationEventSubscription> _subscriptions;
    private readonly KafkaEnvelopeReader _reader;
    private readonly KafkaOffsetCommitter _committer;
    private readonly ILogger<KafkaConsumerWorker> _logger;

    public KafkaConsumerWorker(
        IOptions<KafkaMessagingOptions> options,
        IntegrationEventSubscriptionCatalog catalog,
        IntegrationEventConsumerDispatcher dispatcher,
        IEnumerable<IIntegrationEventSubscription> subscriptions,
        KafkaEnvelopeReader reader,
        KafkaOffsetCommitter committer,
        ILogger<KafkaConsumerWorker> logger)
    {
        _options = options.Value;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _subscriptions = subscriptions.ToArray();
        _reader = reader;
        _committer = committer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (_subscriptions.Count == 0)
        {
            _logger.LogWarning("Kafka messaging is enabled but no integration event subscriptions are registered.");
            return;
        }

        var consumerGroups = BuildConsumerGroups();
        var workers = consumerGroups
            .Select(group => RunConsumerGroupAsync(group, stoppingToken))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private IReadOnlyList<ConsumerGroupPlan> BuildConsumerGroups()
    {
        var plans = new Dictionary<string, ConsumerGroupPlan>(StringComparer.Ordinal);
        foreach (var subscription in _subscriptions)
        {
            var topic = _catalog.GetTopicRequired(
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
        if (!_reader.TryRead(consumeResult, out var envelope, out var failureCode)
            || envelope is null)
        {
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                plan.ResolveTopicCode(consumeResult.Topic),
                plan.ConsumerName,
                "unknown",
                "contract_rejected",
                failureCode);
            _logger.LogWarning(
                "Rejected Kafka envelope for consumer {ConsumerName} with code {FailureCode}.",
                plan.ConsumerName,
                failureCode);
            return;
        }

        if (!plan.TryGetSubscription(envelope.MessageType, envelope.SchemaVersion, out var subscription))
        {
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                plan.ResolveTopicCode(consumeResult.Topic),
                plan.ConsumerName,
                envelope.MessageType,
                "route_missing",
                IntegrationEventFailureCodes.SchemaVersionUnknown);
            _logger.LogWarning(
                "No subscription route for consumer {ConsumerName}, event {EventType} schema {SchemaVersion}.",
                plan.ConsumerName,
                envelope.MessageType,
                envelope.SchemaVersion);
            return;
        }

        var topicCode = plan.ResolveTopicCode(consumeResult.Topic);
        try
        {
            var inboxResult = await _dispatcher
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
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                envelope.MessageType,
                "permanent_failure",
                exception.Failure.Code);
            _logger.LogWarning(
                exception,
                "Permanent integration event failure for consumer {ConsumerName}; offset left uncommitted.",
                plan.ConsumerName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                envelope.MessageType,
                "transient_failure",
                IntegrationEventFailureCodes.TransientPrefix + "consumer_dispatch");
            _logger.LogWarning(
                exception,
                "Transient integration event failure for consumer {ConsumerName}; offset left uncommitted.",
                plan.ConsumerName);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Kafka consumer worker shutdown reached the drain timeout.");
        }
    }

    private sealed class ConsumerGroupPlan
    {
        private readonly Dictionary<(string EventType, int SchemaVersion), IIntegrationEventSubscription> _routes = new();

        public ConsumerGroupPlan(string consumerName)
        {
            ConsumerName = consumerName;
        }

        public string ConsumerName { get; }

        public HashSet<string> TopicCodes { get; } = new(StringComparer.Ordinal);

        public void AddRoute(IIntegrationEventSubscription subscription, string topicCode)
        {
            TopicCodes.Add(topicCode);
            _routes[(subscription.EventType, subscription.SchemaVersion)] = subscription;
        }

        public bool TryGetSubscription(
            string eventType,
            int schemaVersion,
            out IIntegrationEventSubscription subscription) =>
            _routes.TryGetValue((eventType, schemaVersion), out subscription!);

        public string ResolveTopicCode(string topic) => topic;
    }
}