using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 提供租户模块给非 HTTP 宿主使用的最小服务注册入口。
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// 注册后台 Outbox Worker 消费租户事件所需的最小服务集合。
    /// </summary>
    /// <remarks>
    /// Worker 不承载 HTTP Endpoint，因此不得为了缓存失效处理器注册完整
    /// Tenancy 模块及其 Identity 依赖。
    /// </remarks>
    public static IServiceCollection AddFullNetTenancyWorkerServices(
        this IServiceCollection services)
    {
        services.TryAddScoped<CurrentTenantAccessor>();
        services.TryAddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler,
            TenantProvisionedCacheInvalidationHandler>());
        return services;
    }
}
