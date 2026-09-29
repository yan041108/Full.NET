using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 在宿主启动时写出一次资源事实；日志普通事件仅携带 Instance 避免逐条复制系统信息。
/// </summary>
internal sealed class LoggingResourceAnnouncementService(
    LoggingResourceMetadata resource,
    ILogger<LoggingResourceAnnouncementService> logger) : IHostedService
{
    private int _announced;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _announced, 1) == 0)
        {
            logger.LogInformation(
                "LoggingResource Application={Application} Instance={Instance} HostRole={HostRole} Environment={Environment} RuntimeVersion={RuntimeVersion} OSDescription={OSDescription} FrameworkVersion={FrameworkVersion}",
                resource.Application,
                resource.Instance,
                resource.HostRole,
                resource.Environment,
                resource.RuntimeVersion,
                resource.OSDescription,
                resource.FrameworkVersion);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
