using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 生产回退控制面：暂停 Connector、等待 Consumer 排空并验证 CDC 位点覆盖 producer fence。
/// </summary>
internal sealed class KafkaConnectEventDeliveryRollbackReadinessReader(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaConnectRollbackOptions> rollbackOptions,
    IOptions<KafkaMessagingOptions> kafkaOptions,
    KafkaConnectAdminClient connectAdmin,
    KafkaConsumerLagObserver lagObserver,
    RollbackControlPlaneFenceRegistry fenceRegistry,
    ILogger<KafkaConnectEventDeliveryRollbackReadinessReader> logger)
    : IEventDeliveryRollbackReadinessReader
{
    public async Task<EventDeliveryRollbackReadiness> PrepareAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        var options = rollbackOptions.Value;
        if (!options.Enabled)
        {
            return EventDeliveryRollbackReadiness.Unavailable;
        }

        var binding = ResolveBinding(eventType, schemaVersion);
        if (binding is null)
        {
            KafkaConnectRollbackLog.StreamBindingMissing(logger, eventType, schemaVersion);
            return EventDeliveryRollbackReadiness.Unavailable;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var fenceReader = scope.ServiceProvider
            .GetRequiredService<IEventDeliveryProducerFencePositionReader>();
        var producerFence = await fenceReader
            .TryReadAsync(eventType, schemaVersion, rollbackGeneration, cancellationToken)
            .ConfigureAwait(false);
        if (producerFence is null)
        {
            KafkaConnectRollbackLog.ProducerFenceMissing(logger, eventType, schemaVersion, rollbackGeneration);
            return EventDeliveryRollbackReadiness.Unavailable;
        }

        var fenceKey = new RollbackFenceKey(eventType, schemaVersion, rollbackGeneration);
        var controlPlaneFenceToken = BuildFenceToken(rollbackGeneration, binding.ConnectorName);
        try
        {
            await connectAdmin
                .PauseConnectorAsync(binding.ConnectorName, cancellationToken)
                .ConfigureAwait(false);
            if (!await connectAdmin
                    .IsConnectorPausedAsync(binding.ConnectorName, cancellationToken)
                    .ConfigureAwait(false))
            {
                KafkaConnectRollbackLog.ConnectorPauseUnverified(logger, binding.ConnectorName);
                return EventDeliveryRollbackReadiness.Unavailable;
            }

            var drained = await lagObserver
                .WaitUntilDrainedAsync(
                    kafkaOptions.Value,
                    binding.TopicName,
                    binding.ConsumerGroupId,
                    TimeSpan.FromSeconds(options.DrainTimeoutSeconds),
                    TimeSpan.FromMilliseconds(options.DrainPollIntervalMilliseconds),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!drained)
            {
                KafkaConnectRollbackLog.ConsumerDrainTimeout(
                    logger,
                    binding.TopicName,
                    binding.ConsumerGroupId);
                return EventDeliveryRollbackReadiness.Unavailable;
            }

            var connectorPosition = await connectAdmin
                .TryReadConnectorPositionAsync(binding.ConnectorName, cancellationToken)
                .ConfigureAwait(false);
            if (connectorPosition is null)
            {
                KafkaConnectRollbackLog.ConnectorPositionMissing(logger, binding.ConnectorName);
                return EventDeliveryRollbackReadiness.Unavailable;
            }

            var coversFence = CdcDeliveryPosition.ConnectorCoversProducerFence(
                producerFence.ProducerFencePosition,
                connectorPosition);
            if (!coversFence)
            {
                KafkaConnectRollbackLog.ConnectorPositionBehindFence(
                    logger,
                    binding.ConnectorName,
                    producerFence.ProducerFencePosition.ToJson(),
                    connectorPosition.ToJson());
                return EventDeliveryRollbackReadiness.Unavailable;
            }

            var observedAtUtc = DateTimeOffset.UtcNow;
            fenceRegistry.TryRegister(
                fenceKey,
                new RollbackFenceState(
                    binding.ConnectorName,
                    controlPlaneFenceToken,
                    ConnectorPaused: true));

            return new EventDeliveryRollbackReadiness(
                rollbackGeneration,
                ConnectorStopped: true,
                BrokerMessagesDrainedOrIsolated: true,
                SourcePositionCoversProducerFence: true,
                ProducerFencePositionJson: producerFence.ProducerFencePosition.ToJson(),
                CdcSourcePositionJson: connectorPosition.ToJson(),
                ControlPlaneFenceToken: controlPlaneFenceToken,
                LastPublishedEventId: producerFence.LastPublishedEventId,
                ObservedAtUtc: observedAtUtc);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            KafkaConnectRollbackLog.PrepareFailed(logger, exception, eventType, schemaVersion);
            return EventDeliveryRollbackReadiness.Unavailable;
        }
    }

    public async Task AbortAsync(
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);

        var fenceKey = new RollbackFenceKey(eventType, schemaVersion, rollbackGeneration);
        if (!fenceRegistry.TryGet(fenceKey, out var fenceState))
        {
            return;
        }

        if (!fenceState.ConnectorPaused)
        {
            fenceRegistry.TryRemove(fenceKey);
            return;
        }

        await connectAdmin
            .ResumeConnectorAsync(fenceState.ConnectorName, cancellationToken)
            .ConfigureAwait(false);
        fenceRegistry.TryRemove(fenceKey);
    }

    private KafkaConnectRollbackStreamBinding? ResolveBinding(string eventType, int schemaVersion) =>
        rollbackOptions.Value.Streams.FirstOrDefault(
            candidate => candidate.SchemaVersion == schemaVersion
                && string.Equals(candidate.EventType, eventType, StringComparison.Ordinal));

    private static string BuildFenceToken(Guid rollbackGeneration, string connectorName) =>
        $"{rollbackGeneration:N}:{connectorName}:paused";
}

internal static partial class KafkaConnectRollbackLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Kafka Connect rollback stream binding is missing for {EventType} schema {SchemaVersion}.")]
    public static partial void StreamBindingMissing(
        ILogger logger,
        string eventType,
        int schemaVersion);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Producer fence snapshot is missing for {EventType} schema {SchemaVersion} generation {RollbackGeneration}.")]
    public static partial void ProducerFenceMissing(
        ILogger logger,
        string eventType,
        int schemaVersion,
        Guid rollbackGeneration);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Connector {ConnectorName} pause could not be verified.")]
    public static partial void ConnectorPauseUnverified(ILogger logger, string connectorName);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Consumer group {ConsumerGroupId} did not drain topic {TopicName} before rollback timeout.")]
    public static partial void ConsumerDrainTimeout(
        ILogger logger,
        string topicName,
        string consumerGroupId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message = "Connector {ConnectorName} offsets are unavailable.")]
    public static partial void ConnectorPositionMissing(ILogger logger, string connectorName);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Connector {ConnectorName} position {ConnectorPositionJson} does not cover producer fence {ProducerFencePositionJson}.")]
    public static partial void ConnectorPositionBehindFence(
        ILogger logger,
        string connectorName,
        string producerFencePositionJson,
        string connectorPositionJson);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Kafka Connect rollback preparation failed for {EventType} schema {SchemaVersion}.")]
    public static partial void PrepareFailed(
        ILogger logger,
        Exception exception,
        string eventType,
        int schemaVersion);
}
