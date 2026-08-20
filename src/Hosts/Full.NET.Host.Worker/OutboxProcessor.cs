using System.Runtime.ExceptionServices;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
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
    private int _consecutiveEmptyBatches;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;
            try
            {
                processedCount = await ProcessOnceAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                OutboxProcessorLog.BatchFailed(logger, exception);
            }

            var delay = GetDelayAfterBatch(processedCount);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxEnvelope> messages;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var currentTenant =
                services.GetRequiredService<CurrentTenantAccessor>();
            currentTenant.SetHost();
            try
            {
                var store = services.GetRequiredService<IOutboxStore>();
                var backlogReader =
                    services.GetRequiredService<IOutboxBacklogReader>();
                await RecordBacklogAsync(backlogReader, cancellationToken)
                    .ConfigureAwait(false);
                messages = await store
                    .AcquireAsync(
                        _options.BatchSize,
                        TimeSpan.FromSeconds(_options.LeaseSeconds),
                        cancellationToken)
                    .ConfigureAwait(false);
                OutboxProcessorLog.MessagesLeased(logger, messages.Count);

                if (_options.MaxConcurrency == 1)
                {
                    var handlers = services
                        .GetServices<IIntegrationEventHandler>()
                        .ToArray();
                    var ownerResolver = services
                        .GetService<IEffectiveEventDeliveryOwnerResolver>();
                    return await ProcessBatchWithLeaseRenewalAsync(
                            messages,
                            async (
                                markBatchTerminal,
                                batchCancellationToken) =>
                            {
                                foreach (var message in messages)
                                {
                                    await ProcessMessageAsync(
                                            message,
                                            handlers,
                                            store,
                                            ownerResolver,
                                            batchCancellationToken)
                                        .ConfigureAwait(false);
                                }

                                markBatchTerminal();
                                return messages.Count;
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        // 并发路径在领取事务提交并释放批次 Scope 后启动，每条消息独占 Scoped Handler 与数据库会话。
        return await ProcessBatchWithLeaseRenewalAsync(
                messages,
                async (
                    markBatchTerminal,
                    batchCancellationToken) =>
                {
                    await Parallel.ForEachAsync(
                            messages,
                            new ParallelOptions
                            {
                                CancellationToken = batchCancellationToken,
                                MaxDegreeOfParallelism =
                                    _options.MaxConcurrency,
                            },
                            ProcessMessageInScopeAsync)
                        .ConfigureAwait(false);
                    markBatchTerminal();
                    return messages.Count;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal TimeSpan GetDelayAfterBatch(int processedCount)
    {
        if (processedCount >= _options.BatchSize)
        {
            _consecutiveEmptyBatches = 0;
            OutboxBacklogTelemetry.RecordEmptyPollBackoff(TimeSpan.Zero);
            return TimeSpan.Zero;
        }

        if (processedCount > 0)
        {
            _consecutiveEmptyBatches = 0;
            OutboxBacklogTelemetry.RecordEmptyPollBackoff(TimeSpan.Zero);
            return TimeSpan.FromMilliseconds(_options.PollMilliseconds);
        }

        var exponent = Math.Min(_consecutiveEmptyBatches, 30);
        _consecutiveEmptyBatches = Math.Min(_consecutiveEmptyBatches + 1, 30);
        var delay = Math.Min(
            _options.MaximumIdlePollMilliseconds,
            _options.PollMilliseconds * Math.Pow(2d, exponent));
        var backoff = TimeSpan.FromMilliseconds(delay);
        // 空轮询退避秒数进入低基数 Gauge，便于与积压年龄对照，不改变轮询语义。
        OutboxBacklogTelemetry.RecordEmptyPollBackoff(backoff);
        return backoff;
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
        IEffectiveEventDeliveryOwnerResolver? ownerResolver,
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

            var deliveryOwner = ownerResolver is null
                ? EventDeliveryOwner.LegacyPolling
                : await ownerResolver
                    .GetDeliveryOwnerAsync(
                        message.MessageType,
                        message.SchemaVersion,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (deliveryOwner is not (EventDeliveryOwner.LegacyPolling
                or EventDeliveryOwner.ShadowCdc))
            {
                throw new OutboxPermanentException(
                    OutboxDeadLetterReasons.LegacyOwnerRevoked,
                    $"Legacy polling no longer owns '{message.MessageType}' schema {message.SchemaVersion}.");
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

            var context = new IntegrationEventContext(
                message.Id,
                message.MessageType,
                message.SchemaVersion,
                message.TenantId,
                message.TraceId,
                message.OccurredAtUtc);
            await matchingHandlers[0]
                .HandleAsync(context, message.Payload, cancellationToken)
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

    internal async Task<int> ProcessBatchWithLeaseRenewalAsync(
        IReadOnlyList<OutboxEnvelope> messages,
        Func<Action, CancellationToken, Task<int>> processBatch,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        var lockId = messages[0].LockId;
        if (messages.Any(message => message.LockId != lockId))
        {
            throw new InvalidOperationException(
                "An Outbox batch must share one lock identifier.");
        }

        using var leaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completionOrder = new OutboxLeaseCompletionOrder();

        var messageIds = messages.Select(message => message.Id).ToArray();
        var renewalTask = RenewLeaseAndTrackFailureAsync();
        var processingTask = ProcessBatchAndTrackCompletionAsync();
        var completedTask = await Task.WhenAny(processingTask, renewalTask)
            .ConfigureAwait(false);
        if (completedTask == renewalTask)
        {
            Exception? renewalFailure = null;
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
            }

            if (completionOrder.ShouldPreserveProcessingOutcome(
                processingTask.IsCompletedSuccessfully))
            {
                return await processingTask.ConfigureAwait(false);
            }

            leaseCancellation.Cancel();
            Exception? processingFailure = null;
            try
            {
                await processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (leaseCancellation.IsCancellationRequested)
            {
                // 续租失败后先等待协作式 Handler 退出，再传播原始租约故障。
            }
            catch (Exception exception)
            {
                processingFailure = exception;
            }

            if (renewalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(renewalFailure).Throw();
            }

            if (processingFailure is not null)
            {
                ExceptionDispatchInfo.Capture(processingFailure).Throw();
            }

            throw new InvalidOperationException(
                $"Outbox lease '{lockId:D}' renewal stopped unexpectedly.");
        }

        try
        {
            return await processingTask.ConfigureAwait(false);
        }
        finally
        {
            leaseCancellation.Cancel();
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (leaseCancellation.IsCancellationRequested)
            {
                // 批次已进入终态或宿主退出，续租循环应随 linked token 有界停止。
            }
            catch (Exception)
                when (completionOrder.ShouldPreserveProcessingOutcome(
                    processingTask.IsCompletedSuccessfully))
            {
                // 终态先完成时零行续租是正常竞争；处理先失败时保留原始处理异常。
            }
        }

        async Task<int> ProcessBatchAndTrackCompletionAsync()
        {
            try
            {
                return await processBatch(
                        completionOrder.MarkBatchTerminal,
                        leaseCancellation.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                completionOrder.MarkProcessingCompleted();
            }
        }

        async Task RenewLeaseAndTrackFailureAsync()
        {
            try
            {
                await RenewLeaseUntilCanceledAsync(
                        messageIds,
                        lockId,
                        completionOrder.MarkRenewalFailed,
                        leaseCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                completionOrder.MarkRenewalFailed();
                throw;
            }
        }
    }

    private async Task RenewLeaseUntilCanceledAsync(
        IReadOnlyCollection<Guid> messageIds,
        Guid lockId,
        Action markRenewalFailed,
        CancellationToken cancellationToken)
    {
        var renewalInterval = TimeSpan.FromSeconds(
            _options.LeaseRenewalSeconds);
        var leaseDuration = TimeSpan.FromSeconds(_options.LeaseSeconds);
        while (true)
        {
            await Task.Delay(renewalInterval, cancellationToken)
                .ConfigureAwait(false);
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            CurrentTenantAccessor? currentTenant = null;
            try
            {
                currentTenant =
                    services.GetRequiredService<CurrentTenantAccessor>();
                currentTenant.SetHost();
                var store = services.GetRequiredService<IOutboxStore>();
                await store
                    .RenewLeaseAsync(
                        messageIds,
                        lockId,
                        leaseDuration,
                        cancellationToken)
                    .ConfigureAwait(false);
                OutboxProcessorLog.LeaseRenewed(logger, lockId);
            }
            catch
            {
                markRenewalFailed();
                throw;
            }
            finally
            {
                currentTenant?.Clear();
            }
        }
    }

    private async ValueTask ProcessMessageInScopeAsync(
        OutboxEnvelope message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var currentTenant =
            services.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var handlers = services
                .GetServices<IIntegrationEventHandler>()
                .ToArray();
            var store = services.GetRequiredService<IOutboxStore>();
            var ownerResolver = services.GetService<IEffectiveEventDeliveryOwnerResolver>();
            await ProcessMessageAsync(
                    message,
                    handlers,
                    store,
                    ownerResolver,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
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

internal sealed class OutboxLeaseCompletionOrder
{
    private long _nextOrder;
    private long _processingCompletionOrder;
    private long _renewalFailureOrder;
    private long _terminalOrder;

    public bool DidBatchReachTerminalBeforeRenewalFailure
    {
        get
        {
            var terminalOrder = Volatile.Read(ref _terminalOrder);
            var renewalFailureOrder =
                Volatile.Read(ref _renewalFailureOrder);
            return terminalOrder > 0
                && renewalFailureOrder > 0
                && terminalOrder < renewalFailureOrder;
        }
    }

    public bool DidProcessingCompleteBeforeRenewalFailure
    {
        get
        {
            var processingCompletionOrder =
                Volatile.Read(ref _processingCompletionOrder);
            var renewalFailureOrder =
                Volatile.Read(ref _renewalFailureOrder);
            return processingCompletionOrder > 0
                && renewalFailureOrder > 0
                && processingCompletionOrder < renewalFailureOrder;
        }
    }

    public void MarkBatchTerminal()
    {
        var order = Interlocked.Increment(ref _nextOrder);
        Interlocked.CompareExchange(ref _terminalOrder, order, 0);
    }

    public bool ShouldPreserveProcessingOutcome(bool processingSucceeded) =>
        DidBatchReachTerminalBeforeRenewalFailure
        || (!processingSucceeded
            && DidProcessingCompleteBeforeRenewalFailure);

    public void MarkRenewalFailed()
    {
        var order = Interlocked.Increment(ref _nextOrder);
        Interlocked.CompareExchange(ref _renewalFailureOrder, order, 0);
    }

    public void MarkProcessingCompleted()
    {
        var order = Interlocked.Increment(ref _nextOrder);
        Interlocked.CompareExchange(
            ref _processingCompletionOrder,
            order,
            0);
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

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Renewed Outbox lease {LockId}")]
    public static partial void LeaseRenewed(ILogger logger, Guid lockId);
}

internal sealed class OutboxPermanentException(string reasonCode, string message)
    : InvalidOperationException(message)
{
    public string ReasonCode { get; } = reasonCode;
}
