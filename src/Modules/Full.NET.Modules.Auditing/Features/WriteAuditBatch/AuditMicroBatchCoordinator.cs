using System.Diagnostics;
using System.Threading.Channels;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// B1 跨请求有界微批协调器：持有 Channel，按行数/字节/时延排空，请求等待自身批次结果。
/// </summary>
internal sealed class AuditMicroBatchCoordinator : BackgroundService
{
    private readonly Channel<AuditWriteEnvelope> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditMicroBatchOptions> _options;
    private readonly ILogger<AuditMicroBatchCoordinator> _logger;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly AuditQueueByteBudget _byteBudget = new();

    internal long QueueBytesInUse => _byteBudget.ReservedBytes;

    public AuditMicroBatchCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditMicroBatchOptions> options,
        ILogger<AuditMicroBatchCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        var capacity = Math.Max(1, options.CurrentValue.Capacity);
        _channel = Channel.CreateBounded<AuditWriteEnvelope>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    /// <summary>入队 Operation/Exception（可空）并等待各自批次结果；Access 不得进入。</summary>
    public async Task FlushImportantAsync(
        OperationLogWriteModel? operation,
        ExceptionLogWriteModel? exception,
        CancellationToken cancellationToken)
    {
        var pending = new List<Task<AuditWriteResult>>(2);
        if (operation is not null)
        {
            pending.Add(EnqueueAsync(
                AuditWriteEnvelope.ForOperation(operation),
                "operation",
                cancellationToken));
        }

        if (exception is not null)
        {
            pending.Add(EnqueueAsync(
                AuditWriteEnvelope.ForException(exception),
                "exception",
                cancellationToken));
        }

        if (pending.Count == 0)
        {
            return;
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
    }

    /// <summary>Outbound 按调用契约入队并等待批次结果。</summary>
    public Task<AuditWriteResult> EnqueueOutboundAsync(
        OutboundCallLogRecord record,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            AuditWriteEnvelope.ForOutbound(record),
            "outbound",
            cancellationToken);

    private async Task<AuditWriteResult> EnqueueAsync(
        AuditWriteEnvelope envelope,
        string kind,
        CancellationToken cancellationToken)
    {
        var waitStarted = Stopwatch.GetTimestamp();
        AuditMicroBatchOptions options;
        try
        {
            options = _options.CurrentValue;
        }
        catch (Exception)
        {
            // 热配置无效时拒绝当前 B1 写入，不让日志旁路改变业务结果。
            AuditMicroBatchTelemetry.RecordRejected("options_invalid");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }
        if (envelope.EstimatedBytes > options.MaxBatchBytes)
        {
            AuditMicroBatchTelemetry.RecordRejected("event_oversize");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }

        if (!_byteBudget.TryReserve(envelope.EstimatedBytes, options.QueueMaxBytes))
        {
            AuditMicroBatchTelemetry.RecordRejected("queue_byte_budget");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.EnqueueTimeout);
        var accepted = false;
        try
        {
            await _channel.Writer.WriteAsync(envelope, timeout.Token).ConfigureAwait(false);
            accepted = true;
            AuditMicroBatchTelemetry.RecordAccepted(kind);

            var result = await envelope.Completion.Task.ConfigureAwait(false);
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 队列满或入队超时：B1 固定 fail-open，不写 Outbox。
            AuditMicroBatchTelemetry.RecordRejected("enqueue_timeout");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }
        catch (ChannelClosedException)
        {
            AuditMicroBatchTelemetry.RecordRejected("channel_closed");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }
        finally
        {
            // WriteAsync 未接受的信封归生产者；接受后由消费端在写库尝试结束时归还。
            if (!accepted)
            {
                envelope.ReleaseQueueBudgetOnce(_byteBudget);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<AuditWriteEnvelope>(
            Math.Max(1, _options.CurrentValue.MaxBatchRows));
        var bufferedBytes = 0;
        AuditWriteEnvelope? deferred = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var optionsReadFailed = true;
            try
            {
                var options = _options.CurrentValue;
                optionsReadFailed = false;
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                delayCts.CancelAfter(options.MaxBatchDelay);

                try
                {
                    while (buffer.Count < options.MaxBatchRows
                        && bufferedBytes < options.MaxBatchBytes)
                    {
                        AuditWriteEnvelope envelope;
                        if (deferred is not null)
                        {
                            envelope = deferred;
                            deferred = null;
                        }
                        else if (buffer.Count == 0)
                        {
                            envelope = await _channel.Reader
                                .ReadAsync(stoppingToken)
                                .ConfigureAwait(false);
                        }
                        else if (!_channel.Reader.TryRead(out envelope!))
                        {
                            await _channel.Reader
                                .WaitToReadAsync(delayCts.Token)
                                .ConfigureAwait(false);
                            if (!_channel.Reader.TryRead(out envelope!))
                            {
                                break;
                            }
                        }

                        if (envelope.EstimatedBytes > options.MaxBatchBytes)
                        {
                            // 热更新收紧上限后，已经入队的旧事件也不能越界写入。
                            envelope.Completion.TrySetResult(new AuditWriteResult(Succeeded: false));
                            AuditMicroBatchTelemetry.RecordRejected("event_oversize");
                            envelope.ReleaseQueueBudgetOnce(_byteBudget);
                            continue;
                        }

                        if (buffer.Count > 0
                            && envelope.EstimatedBytes > options.MaxBatchBytes - bufferedBytes)
                        {
                            deferred = envelope;
                            break;
                        }

                        buffer.Add(envelope);
                        bufferedBytes += envelope.EstimatedBytes;
                        if (buffer.Count >= options.MaxBatchRows
                            || bufferedBytes >= options.MaxBatchBytes)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (
                    !stoppingToken.IsCancellationRequested && buffer.Count > 0)
                {
                    // MaxBatchDelay 到期：排空当前缓冲。
                }

                if (buffer.Count > 0)
                {
                    await FlushBufferAsync(buffer, stoppingToken).ConfigureAwait(false);
                    buffer.Clear();
                    bufferedBytes = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Writer 边界的意外失败也必须终结请求，不能带着原缓冲无限重试。
                FailOpenRemaining(buffer, "flush_exception");
                bufferedBytes = 0;
                if (optionsReadFailed)
                {
                    // 已接受信封不能无限等待配置恢复；新请求也会在生产者侧 fail-open。
                    if (deferred is not null)
                    {
                        FailOpenEnvelope(deferred, "options_invalid");
                        deferred = null;
                    }

                    FailOpenQueued("options_invalid");
                }
                _logger.LogError("B1 micro-batch loop failed; continuing.");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机信号优先，随后统一排空。
                }
            }
        }

        await DrainOnShutdownAsync(buffer, deferred).ConfigureAwait(false);
    }

    private async Task DrainOnShutdownAsync(
        List<AuditWriteEnvelope> buffered,
        AuditWriteEnvelope? deferred)
    {
        var remaining = new List<AuditWriteEnvelope>();
        var remainingBytes = 0;

        try
        {
            var options = _options.CurrentValue;
            using var shutdownCts = new CancellationTokenSource(options.ShutdownFlushTimeout);

            async Task AddAsync(AuditWriteEnvelope envelope)
            {
                if (envelope.Completion.Task.IsCompleted)
                {
                    envelope.ReleaseQueueBudgetOnce(_byteBudget);
                    return;
                }

                if (envelope.EstimatedBytes > options.MaxBatchBytes)
                {
                    envelope.Completion.TrySetResult(new AuditWriteResult(Succeeded: false));
                    AuditMicroBatchTelemetry.RecordRejected("event_oversize");
                    envelope.ReleaseQueueBudgetOnce(_byteBudget);
                    return;
                }

                if (remaining.Count > 0
                    && (remaining.Count >= options.MaxBatchRows
                        || envelope.EstimatedBytes > options.MaxBatchBytes - remainingBytes))
                {
                    await FlushBufferAsync(remaining, shutdownCts.Token).ConfigureAwait(false);
                    remaining.Clear();
                    remainingBytes = 0;
                }

                remaining.Add(envelope);
                remainingBytes += envelope.EstimatedBytes;
            }

            foreach (var envelope in buffered)
            {
                await AddAsync(envelope).ConfigureAwait(false);
            }

            if (deferred is not null)
            {
                await AddAsync(deferred).ConfigureAwait(false);
            }

            while (_channel.Reader.TryRead(out var envelope))
            {
                await AddAsync(envelope).ConfigureAwait(false);
            }

            if (remaining.Count > 0)
            {
                await FlushBufferAsync(remaining, shutdownCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // 停机超时或 writer 基础设施失败：所有持有信封都必须解除请求等待。
            var reason = exception is OperationCanceledException
                ? "shutdown_timeout"
                : "shutdown_failure";
            FailOpenRemaining(remaining, reason);
            FailOpenRemaining(buffered, reason);
            if (deferred is not null)
            {
                FailOpenEnvelope(deferred, reason);
            }
            FailOpenQueued(reason);

            if (exception is not OperationCanceledException)
            {
                _logger.LogError("B1 micro-batch shutdown drain failed open.");
            }
        }
    }

    private void FailOpenRemaining(
        List<AuditWriteEnvelope> remaining,
        string reason)
    {
        foreach (var envelope in remaining)
        {
            FailOpenEnvelope(envelope, reason);
        }

        remaining.Clear();
    }

    private void FailOpenQueued(string reason)
    {
        while (_channel.Reader.TryRead(out var envelope))
        {
            FailOpenEnvelope(envelope, reason);
        }
    }

    private void FailOpenEnvelope(AuditWriteEnvelope envelope, string reason)
    {
        if (envelope.Completion.TrySetResult(new AuditWriteResult(Succeeded: false)))
        {
            AuditMicroBatchTelemetry.RecordRejected(reason);
        }

        envelope.ReleaseQueueBudgetOnce(_byteBudget);
    }

    private async Task FlushBufferAsync(
        List<AuditWriteEnvelope> buffer,
        CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriteBatchWriter>();
            await writer.WriteMicroBatchAsync(buffer.ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // 毒记录二分可能提前完成部分请求；保留预算直到整批写入尝试结束。
            foreach (var envelope in buffer)
            {
                if (envelope.Completion.Task.IsCompleted)
                {
                    envelope.ReleaseQueueBudgetOnce(_byteBudget);
                }
            }
            _flushGate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
