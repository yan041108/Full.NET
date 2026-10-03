using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Retention;

/// <summary>API 后台定期读取权威检查点；任何失败立即撤销本地详情资格。</summary>
internal sealed class AuditDetailsCapturePolicyRefreshService(
    IServiceScopeFactory scopeFactory,
    AuditDetailsCapturePolicyCache cache,
    IOptionsMonitor<AuditDetailsCaptureOptions> options,
    ILogger<AuditDetailsCapturePolicyRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken).ConfigureAwait(false);
                var intervalSeconds = 30;
                try
                {
                    intervalSeconds = options.CurrentValue.RefreshSeconds;
                }
                catch (Exception)
                {
                    cache.Clear();
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 5, 300)),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!options.CurrentValue.Enabled)
            {
                cache.Clear();
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                var snapshot = await scope.ServiceProvider
                    .GetRequiredService<IAuditDetailsCleanupCheckpointReader>()
                    .ReadAsync(cancellationToken).ConfigureAwait(false);
                cache.RecordSuccessfulRead(snapshot);
            }
            finally
            {
                tenant.Clear();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cache.Clear();
            throw;
        }
        catch (Exception)
        {
            cache.Clear();
            AuditDetailsCapturePolicyRefreshServiceLog.RefreshFailed(logger);
        }
    }
}

internal static partial class AuditDetailsCapturePolicyRefreshServiceLog
{
    [LoggerMessage(
        EventId = 4404,
        Level = LogLevel.Warning,
        Message = "Audit details capture checkpoint refresh failed")]
    public static partial void RefreshFailed(ILogger logger);
}
