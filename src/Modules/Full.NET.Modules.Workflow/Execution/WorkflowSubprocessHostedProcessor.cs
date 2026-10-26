using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>只推进Workflow自有耐久父子事实，子创建及文件准备各自位于正确事务边界。</summary>
internal sealed class WorkflowSubprocessHostedProcessor(IServiceScopeFactory scopes, IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowSubprocessHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Workflow subprocess iteration failed; durable parent and child facts remain available."); }
            await Task.Delay(options.Value.PollMilliseconds, stoppingToken).ConfigureAwait(false);
        }
    }
}
