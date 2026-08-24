using Full.NET.Messaging.Kafka;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Host.Api;

/// <summary>
/// API 宿主序列化与 Kafka 运维重放的组合根；JIT 与 Native AOT 统一 MemoryPack 与真实 Kafka Replay。
/// </summary>
internal static class HostApiServiceRegistration
{
    /// <summary>注册 Integration Event 序列化器（JIT 与 Native AOT 统一 MemoryPack）。</summary>
    public static void AddIntegrationEventSerialization(IServiceCollection services)
    {
        services.AddFullNetMemoryPack();
    }

    /// <summary>注册 Kafka 范围重放运维能力（Phase 3B：Native AOT 与 JIT 共用真实实现）。</summary>
    public static void AddKafkaReplayOperations(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddFullNetKafkaReplayOperations(configuration);
    }
}
