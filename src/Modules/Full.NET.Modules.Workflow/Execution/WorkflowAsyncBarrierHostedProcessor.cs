using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>异步结果接纳不与出站HTTP共享循环，远端慢调用不能阻塞已就绪消费者。</summary>
internal sealed class WorkflowAsyncBarrierHostedProcessor(IServiceScopeFactory scopes,IOptions<WorkflowRecoveryWorkerOptions> options,
    ILogger<WorkflowAsyncBarrierHostedProcessor> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowAsyncBarrierProcessor>().ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogError("Workflow async barrier iteration failed; original dependencies and resume receipts remain durable.");}
            await Task.Delay(TimeSpan.FromMilliseconds(options.Value.PollMilliseconds),stoppingToken).ConfigureAwait(false);
        }
    }
}
