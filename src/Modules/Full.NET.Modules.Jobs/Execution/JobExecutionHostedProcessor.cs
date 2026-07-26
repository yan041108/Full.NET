using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>Worker 轮询执行待处理任务。</summary>
internal sealed class JobExecutionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<JobsWorkerOptions> options,
    ILogger<JobExecutionHostedProcessor> logger) : BackgroundService
{
    private readonly JobsWorkerOptions _options = options.Value;

    internal TimeSpan PollingDelay =>
        TimeSpan.FromMilliseconds(_options.PollMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                JobExecutionHostedProcessorLog.IterationFailed(logger, exception);
            }

            await Task.Delay(PollingDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<JobExecutionRunner>();
        await runner
            .ProcessPendingAsync(_options.BatchSize, cancellationToken)
            .ConfigureAwait(false);
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
