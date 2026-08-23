using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Serialization.MessagePack;

/// <summary>
/// 注册 MessagePack 实现的 Integration Event 序列化器到 DI 容器。
/// </summary>
/// <remarks>
/// 注册为 <see cref="ServiceLifetime.Singleton"/>，因为 MessagePack 标准选项与契约解析器为不可变共享对象，
/// 可跨请求安全复用；事件类型自身不应在序列化器实例上保留状态。
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="MessagePackIntegrationEventSerializer"/> 作为 <see cref="IIntegrationEventSerializer"/> 的实现。
    /// </summary>
    /// <param name="services">服务集合，扩展方法基于此追加具体类型与接口的 Singleton 注册。</param>
    /// <returns>传入的服务集合，便于链式注册。</returns>
    /// <remarks>
    /// 同时注册具体类型与接口实现，使消费方既能注入接口获得多态能力，也能直接获取具体类型以访问 <see cref="MessagePackIntegrationEventSerializer.ContentType"/>。
    /// </remarks>
    public static IServiceCollection AddFullNetMessagePack(
        this IServiceCollection services)
    {
        services.AddSingleton<MessagePackIntegrationEventSerializer>();
        services.AddSingleton<IIntegrationEventSerializer>(provider =>
            provider.GetRequiredService<MessagePackIntegrationEventSerializer>());
        return services;
    }
}
