using Full.NET.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>Worker 轮询恢复任务；满批立即继续，未满才等待 Poll。</summary>
/// <param name="scopeFactory">每轮创建独立作用域以解析扫描器与批处理器。</param>
/// <param name="options">轮询间隔与批大小。</param>
/// <param name="logger">记录迭代失败的日志器。</param>
internal sealed class WorkflowRecoveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowRecoveryHostedProcessor> logger) : BackgroundService
{
    private readonly WorkflowRecoveryWorkerOptions _options = options.Value;

    /// <summary>未满批时的轮询间隔。</summary>
    internal TimeSpan PollingDelay => TimeSpan.FromMilliseconds(_options.PollMilliseconds);

    /// <summary>循环扫描并领取恢复任务，直到宿主取消。</summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;
            try
            {
                processedCount = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow recovery worker iteration failed.");
            }

            var delay = GetDelayAfterBatch(processedCount);
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
        await scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>满批立即再跑，避免积压被 Poll 人为拉长。</summary>
    /// <param name="processedCount">本轮领取数量。</param>
    /// <returns>下一轮等待时间。</returns>
    internal TimeSpan GetDelayAfterBatch(int processedCount) =>
        processedCount >= _options.BatchSize ? TimeSpan.Zero : PollingDelay;
}
