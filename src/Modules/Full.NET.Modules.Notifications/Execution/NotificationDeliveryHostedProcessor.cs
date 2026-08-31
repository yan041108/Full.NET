using Full.NET.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>Worker 轮询投递；满批立即继续，未满才等待 Poll。</summary>
internal sealed class NotificationDeliveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<NotificationDeliveryWorkerOptions> options,
    ILogger<NotificationDeliveryHostedProcessor> logger) : BackgroundService
{
    private readonly NotificationDeliveryWorkerOptions _options = options.Value;
    private DateTimeOffset _nextBacklogSampleAtUtc = DateTimeOffset.MinValue;

    internal TimeSpan PollingDelay => TimeSpan.FromMilliseconds(_options.PollMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedCount = 0;
            try
            {
                processedCount = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification delivery worker iteration failed.");
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
        var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
        var observedAtUtc = clock.UtcNow;
        if (observedAtUtc >= _nextBacklogSampleAtUtc)
        {
            _nextBacklogSampleAtUtc = observedAtUtc.AddSeconds(_options.BacklogSampleSeconds);
            try
            {
                await processor.SampleBacklogAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification delivery backlog sampling failed.");
            }
        }

        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    internal TimeSpan GetDelayAfterBatch(int processedCount) =>
        processedCount >= _options.BatchSize ? TimeSpan.Zero : PollingDelay;
}
