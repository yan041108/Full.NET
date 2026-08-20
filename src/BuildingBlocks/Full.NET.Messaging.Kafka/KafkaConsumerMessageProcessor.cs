using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 定义单条 Kafka 消息处理所需的稳定路由与所有权状态边界。
/// </summary>
internal interface IKafkaConsumerRoutePlan
{
    string ConsumerName { get; }

    bool HasOwnershipRevoked { get; }

    bool ContainsRoute(string eventType, int schemaVersion);

    void SetOwnershipRevoked(string eventType, int schemaVersion, bool revoked);

    string ResolveTopicCode(string topic);
}

/// <summary>
/// 执行生产 Kafka Worker 的单消息语义：契约解析、Inbox 事务、Handler、Retry 与 DLQ。
/// </summary>
internal sealed class KafkaConsumerMessageProcessor
{
    private const string ProviderCode = "kafka";
    private readonly KafkaMessagingOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaEnvelopeReader _reader;
    private readonly KafkaOffsetCommitter _committer;
    private readonly KafkaFailureClassifier _failureClassifier;
    private readonly KafkaRetryRouter _retryRouter;
    private readonly KafkaDeadLetterPublisher _deadLetterPublisher;
    private readonly ILogger<KafkaConsumerMessageProcessor> _logger;

    public KafkaConsumerMessageProcessor(
        IOptions<KafkaMessagingOptions> options,
        IServiceScopeFactory scopeFactory,
        KafkaEnvelopeReader reader,
        KafkaOffsetCommitter committer,
        KafkaFailureClassifier failureClassifier,
        KafkaRetryRouter retryRouter,
        KafkaDeadLetterPublisher deadLetterPublisher,
        ILogger<KafkaConsumerMessageProcessor> logger)
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

    public async Task<bool> ProcessScheduledMessageAsync(
        IKafkaConsumerRoutePlan plan,
        ConsumeResult<string, byte[]> consumeResult,
        CancellationToken cancellationToken)
    {
        if (KafkaDeliveryHeaders.TryReadRetryNotBeforeUtc(
                consumeResult.Message.Headers,
                out var retryNotBeforeUtc))
        {
            var delay = retryNotBeforeUtc - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        return await ProcessMessageAsync(plan, consumeResult, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ProcessMessageAsync(
        IKafkaConsumerRoutePlan plan,
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
            return await HandleDeliveryFailureAsync(
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
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
            return await HandleDeliveryFailureAsync(
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            // 每条消息使用独立作用域，使 Inbox 事务和 Handler 状态与其他并发消息隔离。
            var catalog = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventSubscriptionCatalog>();
            var subscription = KafkaConsumerWorker.ResolveSubscription(
                scope.ServiceProvider,
                catalog,
                plan.ConsumerName,
                envelope.MessageType,
                envelope.SchemaVersion);
            var dispatcher = scope.ServiceProvider
                .GetRequiredService<IntegrationEventConsumerDispatcher>();
            var inboxResult = await dispatcher
                .ConsumeAsync(plan.ConsumerName, envelope, subscription, cancellationToken)
                .ConfigureAwait(false);
            plan.SetOwnershipRevoked(envelope.MessageType, envelope.SchemaVersion, false);
            KafkaMessagingTelemetry.SetOwnershipRevoked(
                plan.ConsumerName,
                plan.HasOwnershipRevoked);

            if (_committer.ShouldCommit(inboxResult))
            {
                KafkaMessagingTelemetry.RecordConsume(
                    ProviderCode,
                    topicCode,
                    plan.ConsumerName,
                    envelope.MessageType,
                    inboxResult.Status == InboxConsumeStatus.Processed
                        ? "processed"
                        : "already_processed");
                return true;
            }

            return false;
        }
        catch (IntegrationEventPermanentException exception)
        {
            return await HandleDeliveryFailureAsync(
                    plan,
                    consumeResult,
                    envelope,
                    exception.Failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EventDeliveryOwnershipRevokedException exception)
        {
            plan.SetOwnershipRevoked(envelope.MessageType, envelope.SchemaVersion, true);
            KafkaMessagingTelemetry.SetOwnershipRevoked(
                plan.ConsumerName,
                plan.HasOwnershipRevoked);
            _logger.LogWarning(
                exception,
                "Kafka delivery is paused for consumer {ConsumerName} because stream ownership was revoked.",
                plan.ConsumerName);
            KafkaMessagingTelemetry.RecordConsume(
                ProviderCode,
                topicCode,
                plan.ConsumerName,
                envelope.MessageType,
                "ownership_revoked",
                "messaging.delivery.ownership_revoked");
            // 保持 Poll 心跳的同时延长未提交消息重试间隔，避免所有权切换期间形成热循环。
            var ownershipWait = TimeSpan.FromMilliseconds(
                _options.OwnershipRevokedBackoffMilliseconds);
            KafkaMessagingTelemetry.RecordOwnershipWait(
                ProviderCode,
                plan.ConsumerName,
                ownershipWait.TotalSeconds);
            await Task.Delay(ownershipWait, cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = _failureClassifier.Classify(exception);
            return await HandleDeliveryFailureAsync(
                    plan,
                    consumeResult,
                    envelope,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> HandleDeliveryFailureAsync(
        IKafkaConsumerRoutePlan plan,
        ConsumeResult<string, byte[]> consumeResult,
        IntegrationEventEnvelope? envelope,
        IntegrationEventFailure failure,
        CancellationToken cancellationToken)
    {
        var topicCode = plan.ResolveTopicCode(consumeResult.Topic);
        var messageType = envelope?.MessageType ?? "unknown";
        var attemptCount = KafkaDeliveryHeaders.ReadAttemptCount(
            consumeResult.Message.Headers);

        if (_failureClassifier.ShouldRetry(failure)
            && _retryRouter.GetNextRetryTopic(consumeResult.Topic, attemptCount) is not null)
        {
            if (await _retryRouter
                    .TryRouteAsync(
                        consumeResult,
                        plan.ConsumerName,
                        failure,
                        attemptCount,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                KafkaMessagingTelemetry.RecordConsume(
                    ProviderCode,
                    topicCode,
                    plan.ConsumerName,
                    messageType,
                    "retry_routed",
                    failure.Code);
                return true;
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
            return false;
        }

        if (await _deadLetterPublisher
                .TryPublishAsync(
                    consumeResult,
                    plan.ConsumerName,
                    failure,
                    attemptCount,
                    cancellationToken)
                .ConfigureAwait(false))
        {
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
            return true;
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
        return false;
    }
}
