using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Webhooks.Delivery;

/// <summary>轮询待投递 Webhook 并执行 HTTP 回调。</summary>
internal sealed class WebhookDeliveryHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<WebhookDeliveryWorkerOptions> options,
    ILogger<WebhookDeliveryHostedProcessor> logger) : BackgroundService
{
    private readonly WebhookDeliveryWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Webhook delivery is disabled by Worker configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            TimeSpan? capacityDelay = null;
            try
            {
                processed = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ServiceCapacityExceededException exception)
            {
                // 准入退避遵守数据库建议，多副本在正常轮询间隔内加入小幅抖动。
                var pollingDelay = TimeSpan.FromMilliseconds(_options.PollMilliseconds);
                var minimumDelay = exception.RetryAfter > pollingDelay ? exception.RetryAfter : pollingDelay;
                var jitterLimit = Math.Min(pollingDelay.TotalMilliseconds / 4, 1000);
                capacityDelay = minimumDelay + TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * jitterLimit);
                logger.LogWarning("Webhook delivery polling delayed by database capacity ({CapacityFailure}) for {RetryDelayMilliseconds} ms.",
                    exception.Kind, capacityDelay.Value.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook delivery worker iteration failed.");
            }

            var delay = capacityDelay ?? (processed < Math.Clamp(_options.BatchSize, 1, 50)
                ? TimeSpan.FromMilliseconds(_options.PollMilliseconds)
                : TimeSpan.Zero);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var processor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>();
            return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}
