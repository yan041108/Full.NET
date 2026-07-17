using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class AuthorizationCatalogValidator(
    AuthorizationCatalog catalog) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 解析不可变目录会执行全部一致性校验，使错误在应用接收请求前暴露。
        _ = catalog.Permissions.Count;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
