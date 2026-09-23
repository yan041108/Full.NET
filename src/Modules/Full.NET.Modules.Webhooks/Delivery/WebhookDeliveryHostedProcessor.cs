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
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Webhook delivery worker iteration failed.");
            }

            if (processed < Math.Clamp(_options.BatchSize, 1, 50))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollMilliseconds), stoppingToken)
                    .ConfigureAwait(false);
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
