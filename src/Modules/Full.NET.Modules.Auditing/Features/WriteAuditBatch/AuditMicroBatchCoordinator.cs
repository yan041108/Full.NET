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
        var options = _options.CurrentValue;
        var waitStarted = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.EnqueueTimeout);
        try
        {
            await _channel.Writer.WriteAsync(envelope, timeout.Token).ConfigureAwait(false);
            AuditMicroBatchTelemetry.RecordAccepted(kind);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 队列满或入队超时：B1 固定 fail-open，不写 Outbox。
            AuditMicroBatchTelemetry.RecordRejected("enqueue_timeout");
            AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
            return new AuditWriteResult(Succeeded: false);
        }

        var result = await envelope.Completion.Task.ConfigureAwait(false);
        AuditMicroBatchTelemetry.RecordWait(Stopwatch.GetElapsedTime(waitStarted));
        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<AuditWriteEnvelope>(
            Math.Max(1, _options.CurrentValue.MaxBatchRows));
        var bufferedBytes = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = _options.CurrentValue;
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                delayCts.CancelAfter(options.MaxBatchDelay);

                try
                {
                    while (buffer.Count < options.MaxBatchRows
                        && bufferedBytes < options.MaxBatchBytes)
                    {
                        AuditWriteEnvelope envelope;
                        if (buffer.Count == 0)
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
            catch (Exception exception)
            {
                _logger.LogError(exception, "B1 micro-batch loop failed; continuing.");
            }
        }

        await DrainOnShutdownAsync().ConfigureAwait(false);
    }

    private async Task DrainOnShutdownAsync()
    {
        var options = _options.CurrentValue;
        using var shutdownCts = new CancellationTokenSource(options.ShutdownFlushTimeout);
        var remaining = new List<AuditWriteEnvelope>();
        try
        {
            while (_channel.Reader.TryRead(out var envelope))
            {
                remaining.Add(envelope);
                if (remaining.Count >= options.MaxBatchRows)
                {
                    await FlushBufferAsync(remaining, shutdownCts.Token).ConfigureAwait(false);
                    remaining.Clear();
                }
            }

            if (remaining.Count > 0)
            {
                await FlushBufferAsync(remaining, shutdownCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 停机超时：未刷出的信封 fail-open，避免请求永久挂起。
            FailOpenRemaining(remaining, "shutdown_timeout");
            while (_channel.Reader.TryRead(out var envelope))
            {
                envelope.Completion.TrySetResult(new AuditWriteResult(Succeeded: false));
                AuditMicroBatchTelemetry.RecordRejected("shutdown_timeout");
            }
        }
    }

    private static void FailOpenRemaining(
        List<AuditWriteEnvelope> remaining,
        string reason)
    {
        foreach (var envelope in remaining)
        {
            if (envelope.Completion.TrySetResult(new AuditWriteResult(Succeeded: false)))
            {
                AuditMicroBatchTelemetry.RecordRejected(reason);
            }
        }

        remaining.Clear();
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
            _flushGate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
