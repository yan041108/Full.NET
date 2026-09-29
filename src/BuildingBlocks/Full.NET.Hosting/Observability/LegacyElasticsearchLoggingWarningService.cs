using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 提醒运维旧版直写配置仍在生效，避免升级后误以为已切换到新入口。
/// </summary>
internal sealed class LegacyElasticsearchLoggingWarningService(
    ILogger<LegacyElasticsearchLoggingWarningService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Legacy Elasticsearch logging sink is enabled without DeliveryMode; direct delivery remains active until migrated.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
