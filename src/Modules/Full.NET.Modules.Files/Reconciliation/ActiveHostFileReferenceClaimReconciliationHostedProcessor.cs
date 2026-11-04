using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Reconciliation;

/// <summary>仅 Worker 调度历史 Active 补偿；每轮建立并清除可信 Host 上下文。</summary>
internal sealed class ActiveHostFileReferenceClaimReconciliationHostedProcessor(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ActiveHostFileReferenceClaimReconciliationOptions> options,
    ILogger<ActiveHostFileReferenceClaimReconciliationHostedProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pollSeconds = 300;
            try
            {
                var current = options.CurrentValue;
                // 热更新异常仍使用有界退避，不能让负轮询时间在异常处理之外终止 Worker。
                pollSeconds = Math.Clamp(current.PollSeconds, 5, 86400);
                await ProcessOnceAsync(current, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Files active reference claim reconciliation iteration failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<ActiveHostFileReferenceClaimReconciliationResult> ProcessOnceAsync(
        ActiveHostFileReferenceClaimReconciliationOptions current, CancellationToken ct)
    {
        if (!current.Enabled) return ActiveHostFileReferenceClaimReconciliationResult.Empty;
        await using var scope = scopeFactory.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        host.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<ActiveHostFileReferenceClaimReconciliationRunner>()
                .RunOnceAsync(current, ct).ConfigureAwait(false);
        }
        finally { host.Clear(); }
    }
}
