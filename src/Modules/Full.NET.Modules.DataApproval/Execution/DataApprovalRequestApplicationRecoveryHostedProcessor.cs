using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>Worker 轮询待应用业务变更的 DataApproval 请求。</summary>
internal sealed class DataApprovalRequestApplicationRecoveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<DataApprovalRequestApplicationRecoveryWorkerOptions> options,
    ILogger<DataApprovalRequestApplicationRecoveryHostedProcessor> logger) : BackgroundService
{
    private readonly DataApprovalRequestApplicationRecoveryWorkerOptions _options = options.Value;

    /// <inheritdoc />
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
                logger.LogError(exception, "DataApproval application recovery worker iteration failed.");
            }

            var delay = processedCount >= _options.BatchSize
                ? TimeSpan.Zero
                : TimeSpan.FromMilliseconds(_options.PollMilliseconds);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider
            .GetRequiredService<DataApprovalRequestApplicationRecoveryBatchProcessor>();
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }
}
