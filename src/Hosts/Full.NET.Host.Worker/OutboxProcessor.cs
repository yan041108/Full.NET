using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using global::MessagePack;

namespace Full.NET.Host.Worker;

internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<OutboxWorkerOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private readonly OutboxWorkerOptions _options = options.Value;
    private DateTimeOffset _nextBacklogSampleAtUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                OutboxProcessorLog.BatchFailed(logger, exception);
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(_options.PollMilliseconds),
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var currentTenant = services.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var store = services.GetRequiredService<IOutboxStore>();
            var backlogReader =
                services.GetRequiredService<IOutboxBacklogReader>();
            var handlers = services
                .GetServices<IIntegrationEventHandler>()
                .ToArray();
            await RecordBacklogAsync(backlogReader, cancellationToken)
                .ConfigureAwait(false);
            var messages = await store
                .AcquireAsync(
                    _options.BatchSize,
                    TimeSpan.FromSeconds(_options.LeaseSeconds),
                    cancellationToken)
                .ConfigureAwait(false);
            OutboxProcessorLog.MessagesLeased(logger, messages.Count);

            foreach (var message in messages)
            {
                await ProcessMessageAsync(
                        message,
                        handlers,
                        store,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private async Task RecordBacklogAsync(
        IOutboxBacklogReader backlogReader,
        CancellationToken cancellationToken)
    {
        var observedAtUtc = clock.UtcNow;
        if (observedAtUtc < _nextBacklogSampleAtUtc)
        {
            return;
        }

        // 先推进下一采样点，避免数据库或指标平台故障时每个轮询周期重复施压。
        _nextBacklogSampleAtUtc = observedAtUtc.AddSeconds(
            _options.BacklogSampleSeconds);
        try
        {
            var snapshot = await backlogReader
                .ReadBacklogAsync(cancellationToken)
                .ConfigureAwait(false);
            OutboxBacklogTelemetry.Record(snapshot, observedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            OutboxProcessorLog.BacklogSamplingFailed(logger, exception);
        }
    }

    private async Task ProcessMessageAsync(
        OutboxEnvelope message,
        IReadOnlyCollection<IIntegrationEventHandler> handlers,
        IOutboxStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.Equals(
                    message.ContentType,
                    "application/x-msgpack",
                    StringComparison.Ordinal))
            {
                throw new OutboxPermanentException(
                    OutboxDeadLetterReasons.UnsupportedContentType,
                    $"Unsupported Outbox content type '{message.ContentType}'.");
            }

            var matchingHandlers = IntegrationEventHandlerMatcher.Match(
                handlers,
                message.MessageType,
                message.SchemaVersion);
            if (matchingHandlers.Count == 0)
            {
                throw new OutboxPermanentException(
                    OutboxDeadLetterReasons.HandlerNotFound,
                    $"Expected one handler for '{message.MessageType}' schema {message.SchemaVersion}, but found none.");
            }

            if (matchingHandlers.Count > 1)
            {
                throw new OutboxPermanentException(
                    OutboxDeadLetterReasons.AmbiguousHandler,
                    $"Expected one handler for '{message.MessageType}' schema {message.SchemaVersion}, "
                    + $"but found {matchingHandlers.Count}.");
            }

            await matchingHandlers[0]
                .HandleAsync(message.Payload, cancellationToken)
                .ConfigureAwait(false);
            await store
                .MarkProcessedAsync(
                    message.Id,
                    message.LockId,
                    cancellationToken)
                .ConfigureAwait(false);
            OutboxProcessorLog.MessageProcessed(
                logger,
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.Attempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = $"{exception.GetType().Name}: {exception.Message}";
            if (TryGetDeadLetterReasonCode(message, exception, out var reasonCode))
            {
                var deadLetteredAt = clock.UtcNow;
                await store
                    .MarkDeadLetterAsync(
                        message.Id,
                        message.LockId,
                        error,
                        reasonCode,
                        deadLetteredAt,
                        cancellationToken)
                    .ConfigureAwait(false);
                OutboxProcessorLog.MessageDeadLettered(
                    logger,
                    exception,
                    message.Id,
                    message.MessageType,
                    message.SchemaVersion,
                    message.Attempts,
                    reasonCode,
                    deadLetteredAt);
                return;
            }

            var retryAt = clock.UtcNow.Add(CalculateBackoff(message.Attempts));
            await store
                .MarkFailedAsync(
                    message.Id,
                    message.LockId,
                    error,
                    retryAt,
                    cancellationToken)
                .ConfigureAwait(false);
            OutboxProcessorLog.MessageFailed(
                logger,
                exception,
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.Attempts,
                retryAt);
        }
    }

    private bool TryGetDeadLetterReasonCode(
        OutboxEnvelope message,
        Exception exception,
        out string reasonCode)
    {
        if (exception is OutboxPermanentException permanentException)
        {
            reasonCode = permanentException.ReasonCode;
            return true;
        }

        if (exception is MessagePackSerializationException
            || exception is FormatException
            || exception is InvalidDataException)
        {
            reasonCode = OutboxDeadLetterReasons.InvalidPayload;
            return true;
        }

        if (message.Attempts >= _options.MaxAttempts)
        {
            reasonCode = OutboxDeadLetterReasons.MaxAttemptsExceeded;
            return true;
        }

        reasonCode = string.Empty;
        return false;
    }

    private static TimeSpan CalculateBackoff(int attempts)
    {
        var seconds = Math.Min(300d, Math.Pow(2d, Math.Max(0, attempts)));
        return TimeSpan.FromSeconds(seconds);
    }
}

internal static partial class OutboxProcessorLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "Leased {MessageCount} Outbox messages")]
    public static partial void MessagesLeased(ILogger logger, int messageCount);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Processed Outbox message {MessageId} ({EventType} v{SchemaVersion}, attempt {Attempts})")]
    public static partial void MessageProcessed(
        ILogger logger,
        Guid messageId,
        string eventType,
        int schemaVersion,
        int attempts);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "Failed Outbox message {MessageId} ({EventType} v{SchemaVersion}, attempt {Attempts}); retry at {RetryAt}")]
    public static partial void MessageFailed(
        ILogger logger,
        Exception exception,
        Guid messageId,
        string eventType,
        int schemaVersion,
        int attempts,
        DateTimeOffset retryAt);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Error,
        Message = "Dead-lettered Outbox message {MessageId} ({EventType} v{SchemaVersion}, attempt {Attempts}); reason {ReasonCode}, dead-lettered at {DeadLetteredAt}")]
    public static partial void MessageDeadLettered(
        ILogger logger,
        Exception exception,
        Guid messageId,
        string eventType,
        int schemaVersion,
        int attempts,
        string reasonCode,
        DateTimeOffset deadLetteredAt);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Outbox polling iteration failed")]
    public static partial void BatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Outbox backlog sampling failed; message processing will continue")]
    public static partial void BacklogSamplingFailed(
        ILogger logger,
        Exception exception);
}

internal sealed class OutboxPermanentException(string reasonCode, string message)
    : InvalidOperationException(message)
{
    public string ReasonCode { get; } = reasonCode;
}
