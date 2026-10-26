using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>截止轮询与外部HTTP分开；失败保留原步骤与事务事实供下一轮重试。</summary>
internal sealed class WorkflowSlaHostedProcessor(IServiceScopeFactory scopes, IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowSlaHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowSlaProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Workflow SLA iteration failed; original deadline and system disposition remain durable."); }
            await Task.Delay(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds), stoppingToken).ConfigureAwait(false);
        }
    }
}
