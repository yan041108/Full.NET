using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// IHostedService 启动时预热 AuthorizationCatalog，通过访问属性触发完整一致性校验。
/// 确保权限码无重复、导航节点无孤立引用、动作作用域符合父子约束，
/// 使错误在应用接收请求前立即暴露而非在首个授权调用时才抛出。
/// </summary>
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
