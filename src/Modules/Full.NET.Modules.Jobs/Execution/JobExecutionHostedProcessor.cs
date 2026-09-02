using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>Worker 轮询执行待处理任务。</summary>
/// <param name="scopeFactory">用于为每轮任务创建隔离依赖注入作用域的工厂。</param>
/// <param name="clock">提供积压采样时间的系统时钟。</param>
/// <param name="options">任务 Worker 的批次、轮询与采样配置。</param>
/// <param name="logger">记录轮询故障与降级事件的日志器。</param>
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

    /// <summary>
    /// 持续轮询并执行到期任务，宿主停止后不再把数据库驱动包装的取消异常记录为运行故障。
    /// </summary>
    /// <param name="stoppingToken">宿主停止时触发的取消令牌。</param>
    /// <returns>后台轮询生命周期任务。</returns>
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
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                // SQL Server 可能把命令取消包装为 SqlException；停机信号已成立时属于预期终止，不应触发故障告警。
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
            .GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            await SampleBacklogAsync(
                    scope.ServiceProvider,
                    cancellationToken)
                .ConfigureAwait(false);
            await scope.ServiceProvider
                .GetRequiredService<JobWorkerHeartbeatService>()
                .UpsertAsync(cancellationToken)
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
