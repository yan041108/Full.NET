using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution.BusinessCommands;

/// <summary>后台只扫描本地耐久等待和补偿意图；业务所有者执行由可靠消息消费者完成。</summary>
internal sealed class WorkflowBusinessCommandHostedProcessor(IServiceScopeFactory scopes, IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowBusinessCommandHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowBusinessCommandProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Workflow business command iteration failed; durable effects and compensation intents remain available."); }
            await Task.Delay(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds), stoppingToken).ConfigureAwait(false);
        }
    }
}
