using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>Worker 轮询执行待处理任务。</summary>
internal sealed class JobExecutionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<JobExecutionHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<JobExecutionRunner>();
                await runner.ProcessPendingAsync(cancellationToken: stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                JobExecutionHostedProcessorLog.IterationFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }
    }
}

internal static partial class JobExecutionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Error,
        Message = "Job execution polling iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);
}
