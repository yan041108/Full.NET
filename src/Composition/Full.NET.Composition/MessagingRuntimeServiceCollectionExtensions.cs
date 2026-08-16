using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Composition;

/// <summary>
/// 统一 Api/Worker 与 Messaging 运行时相关的 DI 注册入口。
/// </summary>
public static class MessagingRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Api 宿主：Kafka 重放运维能力（无常驻 Consumer）。
    /// </summary>
    public static IServiceCollection AddFullNetMessagingReplayForApi(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddFullNetKafkaReplayOperations(configuration);

    /// <summary>
    /// Worker 宿主 HybridKafka 模式：Modularity 订阅目录 + Kafka Consumer 运行时。
    /// </summary>
    public static IServiceCollection AddFullNetMessagingWorkerRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        services.AddFullNetModularity();
        services.AddFullNetKafkaMessaging(configuration, environmentName);
        return services;
    }
}
