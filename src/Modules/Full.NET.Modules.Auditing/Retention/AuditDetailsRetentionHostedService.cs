using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

/// <summary>只在 Worker 执行的详情到期清理，与普通审计行保留策略独立运行。</summary>
internal sealed class AuditDetailsRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AuditDetailsRetentionOptions> options,
    ILogger<AuditDetailsRetentionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(60);
            try
            {
                var currentOptions = options.CurrentValue;
                delay = TimeSpan.FromSeconds(currentOptions.PollSeconds);
                var result = await ProcessOnceAsync(currentOptions, stoppingToken).ConfigureAwait(false);
                if (result.MayHaveMore)
                {
                    delay = TimeSpan.Zero;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // 详情清理失败不得记录原始数据库异常，也不能假报完成；下一轮重新尝试。
                AuditDetailsRetentionHostedServiceLog.IterationFailed(logger);
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<AuditDetailsRetentionResult> ProcessOnceAsync(
        AuditDetailsRetentionOptions currentOptions,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<AuditDetailsRetentionRunner>()
                .RunOnceAsync(currentOptions, cancellationToken).ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<AuditDetailsCleanupCheckpointStore>()
                .RecordSuccessfulPassAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal static partial class AuditDetailsRetentionHostedServiceLog
{
    [LoggerMessage(
        EventId = 4403,
        Level = LogLevel.Error,
        Message = "Audit details cleanup iteration failed")]
    public static partial void IterationFailed(ILogger logger);
}
