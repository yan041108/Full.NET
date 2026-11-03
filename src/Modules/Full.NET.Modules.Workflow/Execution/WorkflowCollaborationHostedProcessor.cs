using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>Relay只处理耐久已收件事实；中断保留原准入与唯一目标身份供下次收敛。</summary>
internal sealed class WorkflowCollaborationHostedProcessor(IServiceScopeFactory scopes, IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowCollaborationHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowCollaborationFilePreparationProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
                await scope.ServiceProvider.GetRequiredService<WorkflowCollaborationRelay>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Workflow collaboration Relay failed; durable admission remains pending."); }
            await Task.Delay(options.Value.PollMilliseconds, stoppingToken).ConfigureAwait(false);
        }
    }
}
