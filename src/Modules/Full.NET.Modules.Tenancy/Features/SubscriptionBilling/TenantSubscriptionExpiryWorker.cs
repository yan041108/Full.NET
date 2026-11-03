using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>过期状态整理只在 Worker 运行，不从 API 请求进程启动后台账单任务。</summary>
internal sealed partial class TenantSubscriptionExpiryWorker(IServiceScopeFactory scopes, ILogger<TenantSubscriptionExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await using var scope = scopes.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                budget.CancelAfter(TimeSpan.FromSeconds(25));
                await scope.ServiceProvider.GetRequiredService<TenantSubscriptionExpirySweep>().SweepAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception e) { ExpiryFailed(logger, e); }
            finally { tenant.Clear(); }
        }
    }
    [LoggerMessage(EventId = 7492, Level = LogLevel.Warning, Message = "订阅过期状态整理未完成，将在下一批次重试。")]
    private static partial void ExpiryFailed(ILogger logger, Exception exception);
}
