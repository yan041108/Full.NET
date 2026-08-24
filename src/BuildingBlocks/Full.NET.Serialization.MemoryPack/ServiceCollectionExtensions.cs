using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Serialization.MemoryPack;

/// <summary>
/// 注册 MemoryPack 实现的 Integration Event 序列化器到 DI 容器。
/// </summary>
/// <remarks>
/// 注册为 <see cref="ServiceLifetime.Singleton"/>，因为 MemoryPack 格式化器由源生成器静态产出，
/// 可跨请求安全复用；事件类型自身不应在序列化器实例上保留状态。
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="MemoryPackIntegrationEventSerializer"/> 作为 <see cref="IIntegrationEventSerializer"/> 的实现。
    /// </summary>
    /// <param name="services">服务集合，扩展方法基于此追加具体类型与接口的 Singleton 注册。</param>
    /// <returns>传入的服务集合，便于链式注册。</returns>
    public static IServiceCollection AddFullNetMemoryPack(
        this IServiceCollection services)
    {
        services.AddSingleton<MemoryPackIntegrationEventSerializer>();
        services.AddSingleton<IIntegrationEventSerializer>(provider =>
            provider.GetRequiredService<MemoryPackIntegrationEventSerializer>());
        return services;
    }
}
