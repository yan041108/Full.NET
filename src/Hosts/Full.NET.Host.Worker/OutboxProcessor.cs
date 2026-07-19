using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

internal sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

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

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
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
            var handlers = services
                .GetServices<IIntegrationEventHandler>()
                .ToArray();
            var messages = await store
                .AcquireAsync(BatchSize, LeaseDuration, cancellationToken)
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
                throw new InvalidOperationException(
                    $"Unsupported Outbox content type '{message.ContentType}'.");
            }

            var matchingHandlers = IntegrationEventHandlerMatcher.Match(
                handlers,
                message.MessageType,
                message.SchemaVersion);
            if (matchingHandlers.Count != 1)
            {
                throw new InvalidOperationException(
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
            var retryAt = clock.UtcNow.Add(CalculateBackoff(message.Attempts));
            var error = $"{exception.GetType().Name}: {exception.Message}";
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
        Message = "Outbox polling iteration failed")]
    public static partial void BatchFailed(ILogger logger, Exception exception);
}
