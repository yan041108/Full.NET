using System.Threading.Channels;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>
/// B2 访问日志使用独立有界队列和批量 INSERT；满载或写入失败时仅丢弃日志。
/// 后台作用域不依赖请求租户上下文，租户标识在请求结束时已复制进载荷。
/// </summary>
internal sealed class AccessLogWriteQueue : BackgroundService
{
    private readonly Channel<AccessLogWriteModel> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIdGenerator _idGenerator;
    private readonly AccessLogCaptureOptions _options;
    private readonly ILogger<AccessLogWriteQueue> _logger;

    public AccessLogWriteQueue(
        IServiceScopeFactory scopeFactory,
        IIdGenerator idGenerator,
        IOptions<AccessLogCaptureOptions> options,
        ILogger<AccessLogWriteQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _idGenerator = idGenerator;
        _options = options.Value;
        _logger = logger;
        _channel = Channel.CreateBounded<AccessLogWriteModel>(
            new BoundedChannelOptions(_options.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public bool Enabled => _options.Enabled;

    /// <summary>请求线程只做非阻塞入队；队列满时显式记丢弃指标。</summary>
    public bool TryEnqueue(AccessLogWriteModel model)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(model))
        {
            AccessLogTelemetry.RecordDropped("queue_full_or_stopped");
            return false;
        }

        AccessLogTelemetry.RecordAccepted();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AccessLogWriteModel>(_options.MaxBatchRows);
        try
        {
            while (true)
            {
                batch.Add(await _channel.Reader.ReadAsync(stoppingToken)
                    .ConfigureAwait(false));
                using var delay = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                delay.CancelAfter(_options.MaxBatchDelay);
                while (batch.Count < _options.MaxBatchRows)
                {
                    if (_channel.Reader.TryRead(out var next))
                    {
                        batch.Add(next);
                        continue;
                    }

                    try
                    {
                        if (!await _channel.Reader.WaitToReadAsync(delay.Token)
                            .ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                await FlushAsync(batch, stoppingToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 数据库执行结果未知时丢弃当前批次，避免停机重试产生重复访问记录。
            AccessLogTelemetry.RecordDropped("shutdown_cancelled", batch.Count);
            batch.Clear();
        }
        catch (ChannelClosedException)
        {
            // 写入端关闭后排空最后一批。
        }

        using var shutdown = new CancellationTokenSource(_options.ShutdownFlushTimeout);
        try
        {
            while (_channel.Reader.TryRead(out var pending))
            {
                batch.Add(pending);
                if (batch.Count >= _options.MaxBatchRows)
                {
                    await FlushAsync(batch, shutdown.Token).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            await FlushAsync(batch, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AccessLogTelemetry.RecordDropped("shutdown_timeout", batch.Count);
            _logger.LogWarning("B2 access-log shutdown flush timed out.");
        }
    }

    private async Task FlushAsync(
        IReadOnlyList<AccessLogWriteModel> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            var rows = batch.Select(model => (_idGenerator.NewId(), model)).ToArray();
            var (statement, parameters) = AccessLogBatchSql.Build(rows);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var affected = await executor.ExecuteAsync(statement, parameters, cancellationToken)
                .ConfigureAwait(false);
            if (affected != batch.Count)
            {
                throw new InvalidOperationException(
                    $"B2 access-log batch affected {affected} rows instead of {batch.Count}.");
            }

            AccessLogTelemetry.RecordWritten(batch.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AccessLogTelemetry.RecordDropped("write_failed", batch.Count);
            _logger.LogWarning(exception,
                "B2 access-log batch of {Count} rows was dropped after a write failure.",
                batch.Count);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
