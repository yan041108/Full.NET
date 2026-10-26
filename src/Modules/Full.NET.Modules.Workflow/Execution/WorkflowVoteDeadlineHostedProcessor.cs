using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>Worker周期处理投票截止，异常后延迟重试避免故障活动占满CPU。</summary>
internal sealed class WorkflowVoteDeadlineHostedProcessor(IServiceScopeFactory scopes,
    ILogger<WorkflowVoteDeadlineHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowVoteDeadlineProcessor>().ProcessDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Workflow vote deadline scan failed."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
