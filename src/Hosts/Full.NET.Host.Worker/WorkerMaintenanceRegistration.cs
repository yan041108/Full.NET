using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Host.Worker;

/// <summary>一次性维护命令与常驻 Worker 的 DI 边界调整。</summary>
internal static class WorkerMaintenanceRegistration
{
    /// <summary>
    /// 退役扫描等一次性命令只需解析 Handler 与 Outbox 读取端口，不得启动任何后台循环（含工厂注册的 IHostedService）。
    /// </summary>
    internal static void StripBackgroundLoops(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == typeof(IHostedService))
            {
                services.RemoveAt(index);
            }
        }
    }
}