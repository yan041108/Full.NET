using Full.NET.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>Worker 轮询待关联工作流的 DataApproval 请求。</summary>
internal sealed class DataApprovalRequestRecoveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<DataApprovalRequestRecoveryWorkerOptions> options,
    ILogger<DataApprovalRequestRecoveryHostedProcessor> logger) : BackgroundService
{
    private readonly DataApprovalRequestRecoveryWorkerOptions _options = options.Value;

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
                logger.LogError(exception, "DataApproval request recovery worker iteration failed.");
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
        var processor = scope.ServiceProvider.GetRequiredService<DataApprovalRequestRecoveryBatchProcessor>();
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }
}
