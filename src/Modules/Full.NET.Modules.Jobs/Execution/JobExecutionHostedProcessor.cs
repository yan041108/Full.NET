using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>Worker 轮询执行待处理任务。</summary>
internal sealed class JobExecutionHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<JobsWorkerOptions> options,
    ILogger<JobExecutionHostedProcessor> logger) : BackgroundService
{
    private readonly JobsWorkerOptions _options = options.Value;
    private DateTimeOffset _nextBacklogSampleAtUtc = DateTimeOffset.MinValue;

    internal TimeSpan PollingDelay =>
        TimeSpan.FromMilliseconds(_options.PollMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;
            try
            {
                processedCount = await ProcessOnceAsync(stoppingToken)
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

            var delay = GetDelayAfterBatch(processedCount);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            await SampleBacklogAsync(
                    scope.ServiceProvider,
                    cancellationToken)
                .ConfigureAwait(false);
            await scope.ServiceProvider
                .GetRequiredService<JobScheduleDispatcher>()
                .ProcessDueAsync(_options.BatchSize, cancellationToken)
                .ConfigureAwait(false);
            var runner = scope.ServiceProvider
                .GetRequiredService<JobExecutionRunner>();
            return await runner
                .ProcessPendingAsync(_options.BatchSize, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private async Task SampleBacklogAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var observedAtUtc = clock.UtcNow;
        if (observedAtUtc < _nextBacklogSampleAtUtc)
        {
            return;
        }

        // 先推进下一采样点，避免数据库故障时每个轮询周期重复施压。
        _nextBacklogSampleAtUtc = observedAtUtc.AddSeconds(
            _options.BacklogSampleSeconds);
        try
        {
            var snapshot = await services
                .GetRequiredService<JobsBacklogReader>()
                .ReadAsync(observedAtUtc, cancellationToken)
                .ConfigureAwait(false);
            JobsTelemetry.RecordBacklog(snapshot, observedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            JobExecutionHostedProcessorLog.BacklogSamplingFailed(
                logger,
                exception);
        }
    }

    internal TimeSpan GetDelayAfterBatch(int processedCount) =>
        processedCount >= _options.BatchSize ? TimeSpan.Zero : PollingDelay;
}

internal static partial class JobExecutionHostedProcessorLog
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Error,
        Message = "Job execution polling iteration failed")]
    public static partial void IterationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Warning,
        Message = "Job execution backlog sampling failed; execution polling will continue")]
    public static partial void BacklogSamplingFailed(
        ILogger logger,
        Exception exception);
}
