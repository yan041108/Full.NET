using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Confluent.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// Consumes shadow topics and records comparison evidence only; never invokes business handlers.
/// </summary>
internal sealed class ShadowEventComparisonProcessor : BackgroundService
{
    public const string MeterName = "Full.NET.Messaging.ShadowComparison";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> MatchCounter =
        Meter.CreateCounter<long>("shadow.comparison.match");
    private static readonly Counter<long> MismatchCounter =
        Meter.CreateCounter<long>("shadow.comparison.mismatch");

    private readonly ShadowComparisonOptions _shadowOptions;
    private readonly KafkaMessagingOptions _kafkaOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ShadowEventComparator _comparator = new();
    private readonly KafkaEnvelopeReader _reader = new();
    private readonly ILogger<ShadowEventComparisonProcessor> _logger;

    public ShadowEventComparisonProcessor(
        IOptions<ShadowComparisonOptions> shadowOptions,
        IOptions<KafkaMessagingOptions> kafkaOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<ShadowEventComparisonProcessor> logger)
    {
        _shadowOptions = shadowOptions.Value;
        _kafkaOptions = kafkaOptions.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_shadowOptions.Enabled || !_kafkaOptions.Enabled)
        {
            return;
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(
                _kafkaOptions.BuildConsumerConfig(_shadowOptions.ConsumerGroup))
            .Build();
        consumer.Subscribe(
            $"^{Regex.Escape(_shadowOptions.TopicPrefix)}\\..+");

        ShadowSourcePosition? lastPosition = null;
        var seenEventIds = new HashSet<Guid>();

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]>? consumeResult;
            try
            {
                consumeResult = consumer.Consume(
                    TimeSpan.FromMilliseconds(_shadowOptions.PollTimeoutMilliseconds));
            }
            catch (ConsumeException exception) when (exception.Error.IsFatal)
            {
                ShadowEventComparisonProcessorLog.FatalConsumerError(_logger, exception);
                throw;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (consumeResult?.Message?.Value is null)
            {
                continue;
            }

            var position = new ShadowSourcePosition(
                "kafka",
                $"{consumeResult.Topic}:{consumeResult.Partition.Value}",
                consumeResult.Offset.Value);
            var positionResult = _comparator.ValidateMonotonicPosition(lastPosition, position);
            if (!positionResult.IsMatch)
            {
                RecordMismatch(positionResult);
                consumer.Commit(consumeResult);
                lastPosition = position;
                continue;
            }

            if (!_reader.TryRead(consumeResult, out var envelope, out var failureCode)
                || envelope is null)
            {
                ShadowEventComparisonProcessorLog.InvalidEnvelope(
                    _logger,
                    consumeResult.Topic,
                    failureCode ?? "unknown");
                consumer.Commit(consumeResult);
                lastPosition = position;
                continue;
            }

            var observed = ShadowEventFingerprint.FromEnvelope(envelope);
            // 影子路径用 OccurredAtUtc→首次可见近似 commit-to-capture；正式 Connector 指标由平台填充。
            OutboxBacklogTelemetry.RecordCommitToCapture(
                Math.Max(0d, (DateTimeOffset.UtcNow - envelope.OccurredAtUtc).TotalSeconds),
                "unknown");
            var duplicate = seenEventIds.Contains(observed.EventId);
            if (!duplicate)
            {
                seenEventIds.Add(observed.EventId);
            }

            var expected = await TryLoadExpectedFingerprintAsync(
                    observed.EventId,
                    stoppingToken)
                .ConfigureAwait(false);
            var comparison = _comparator.CompareExpectedToObserved(
                expected,
                observed,
                position,
                duplicateObserved: duplicate);

            if (comparison.IsMatch)
            {
                MatchCounter.Add(1);
            }
            else
            {
                RecordMismatch(comparison);
            }

            consumer.Commit(consumeResult);
            lastPosition = position;
        }
    }

    private async Task<ShadowEventFingerprint?> TryLoadExpectedFingerprintAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var row = await queryExecutor
            .QuerySingleOrDefaultAsync<OutboxFingerprintRow>(
                OutboxFingerprintSql.SelectById,
                new { Id = eventId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return ShadowEventFingerprint.Create(
            row.Id,
            row.MessageType,
            row.SchemaVersion,
            row.PartitionKey,
            row.Payload,
            row.OccurredAtUtc);
    }

    private void RecordMismatch(ShadowEventComparisonResult comparison)
    {
        MismatchCounter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", comparison.Outcome.ToString()));
        ShadowEventComparisonProcessorLog.ComparisonMismatch(
            _logger,
            comparison.Outcome,
            comparison.MismatchField,
            comparison.Observed?.EventId);
    }

    private sealed record OutboxFingerprintRow(
        Guid Id,
        string MessageType,
        int SchemaVersion,
        string PartitionKey,
        byte[] Payload,
        DateTimeOffset OccurredAtUtc);

    private static class OutboxFingerprintSql
    {
        internal static readonly SqlStatement SelectById = new(
            "messaging.shadow.outbox.fingerprint",
            """
            SELECT Id, MessageType, SchemaVersion, PartitionKey, Payload, OccurredAtUtc
            FROM fn_messaging_outbox_event
            WHERE Id = @Id
            """,
            SqlDataScope.Global);
    }
}

internal static partial class ShadowEventComparisonProcessorLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Shadow comparison consumer fatal error.")]
    public static partial void FatalConsumerError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Shadow envelope invalid for topic {Topic}: {FailureCode}.")]
    public static partial void InvalidEnvelope(
        ILogger logger,
        string topic,
        string failureCode);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Shadow comparison mismatch outcome={Outcome} field={MismatchField} eventId={EventId}.")]
    public static partial void ComparisonMismatch(
        ILogger logger,
        ShadowComparisonOutcome outcome,
        string? mismatchField,
        Guid? eventId);
}
