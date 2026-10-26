using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>外部HTTP采用独立有界循环，远端等待不占用普通活动、定时器和恢复扫描循环。</summary>
internal sealed class WorkflowConnectorHostedProcessor(IServiceScopeFactory scopes, IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowConnectorHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowConnectorProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Workflow connector iteration failed; durable activation and attempt facts remain available for recovery."); }
            await Task.Delay(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds), stoppingToken).ConfigureAwait(false);
        }
    }
}
