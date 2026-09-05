using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>仅在 Worker 宿主周期执行工作流待办超时扫描。</summary>
/// <param name="scopeFactory">为每轮创建独立依赖作用域。</param>
/// <param name="logger">记录迭代异常。</param>
internal sealed class WorkflowTodoTimeoutHostedProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowTodoTimeoutHostedProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollingDelay = TimeSpan.FromSeconds(30);

    /// <summary>持续执行有界扫描，满批时立即继续消化积压。</summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var count = 0;
            try
            {
                count = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow todo timeout worker iteration failed.");
            }

            if (count < 50)
            {
                await Task.Delay(PollingDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>执行一轮超时扫描，供受控测试直接驱动。</summary>
    /// <param name="cancellationToken">取消当前轮的令牌。</param>
    /// <returns>扫描候选数量。</returns>
    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkflowTodoTimeoutProcessor>()
            .ProcessDueAsync(cancellationToken).ConfigureAwait(false);
    }
}
