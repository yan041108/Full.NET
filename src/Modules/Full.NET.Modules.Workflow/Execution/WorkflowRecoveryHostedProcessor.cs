using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>Worker 轮询恢复任务；满批立即继续，未满才等待 Poll。</summary>
/// <param name="scopeFactory">每轮创建独立作用域以解析扫描器与批处理器。</param>
/// <param name="clock">控制发现扫描频率的 UTC 时钟。</param>
/// <param name="options">轮询间隔与批大小。</param>
/// <param name="logger">记录迭代失败的日志器。</param>
internal sealed class WorkflowRecoveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowRecoveryHostedProcessor> logger) : BackgroundService
{
    private readonly WorkflowRecoveryWorkerOptions _options = options.Value;
    private DateTimeOffset _nextScanAtUtc = DateTimeOffset.MinValue;

    /// <summary>未满批时的轮询间隔。</summary>
    internal TimeSpan PollingDelay => TimeSpan.FromMilliseconds(_options.PollMilliseconds);

    /// <summary>循环扫描并领取恢复任务，直到宿主取消。</summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollingDelay;
            try
            {
                var processedCount = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
                delay = GetDelayAfterBatch(processedCount);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                // 停机取消可能由数据库驱动包装，宿主已停止时无需再次轮询。
                break;
            }
            catch (ServiceCapacityExceededException exception)
            {
                // 遵守数据库准入的退避提示，再加有界抖动避免多个恢复实例同相重试。
                var minimum = exception.RetryAfter > delay ? exception.RetryAfter : delay;
                var jitterLimit = Math.Min(delay.TotalMilliseconds / 4, 1000);
                delay = minimum + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * jitterLimit);
                logger.LogWarning("Workflow recovery polling delayed by database capacity ({CapacityFailure}) for {RetryDelayMilliseconds} ms.",
                    exception.Kind, delay.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow recovery worker iteration failed.");
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>执行一轮扫描加领取；供测试直接驱动。</summary>
    /// <param name="cancellationToken">取消当前轮询的令牌。</param>
    /// <returns>本轮成功领取的任务数。</returns>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scanner = scope.ServiceProvider.GetRequiredService<WorkflowRecoveryScanner>();
        var processor = scope.ServiceProvider.GetRequiredService<WorkflowRecoveryBatchProcessor>();
        var now = clock.UtcNow;
        if (now >= _nextScanAtUtc)
        {
            // 先推进时间，故障时仍退避；满扫描批次允许下一轮继续发现，不限制消费排空。
            _nextScanAtUtc = now.AddSeconds(_options.ScanIntervalSeconds);
            if (await scanner.ScanAsync(cancellationToken).ConfigureAwait(false) >= _options.BatchSize)
                _nextScanAtUtc = now;
        }
        var timers = await scope.ServiceProvider.GetRequiredService<WorkflowDelayProcessor>()
            .ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
        var activities = await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>()
            .ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
        return timers + activities + await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>满批立即再跑，避免积压被 Poll 人为拉长。</summary>
    /// <param name="processedCount">本轮领取数量。</param>
    /// <returns>下一轮等待时间。</returns>
    internal TimeSpan GetDelayAfterBatch(int processedCount) =>
        processedCount >= _options.BatchSize ? TimeSpan.Zero : PollingDelay;
}
