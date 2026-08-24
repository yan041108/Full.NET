#if FULLNET_API_NATIVE_AOT

using Full.NET.Messaging.Abstractions;

#else

using Full.NET.Messaging.Kafka;

#endif

using Full.NET.Serialization.MemoryPack;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;



namespace Full.NET.Host.Api;



/// <summary>

/// API 宿主序列化与 Kafka 运维重放的组合根分支；Native AOT 与 JIT 在 Kafka 重放上显式分叉。

/// </summary>

internal static class HostApiServiceRegistration

{

    /// <summary>注册 Integration Event 序列化器（JIT 与 Native AOT 统一 MemoryPack）。</summary>

    public static void AddIntegrationEventSerialization(IServiceCollection services)

    {

        services.AddFullNetMemoryPack();

    }



    /// <summary>注册 Kafka 范围重放运维能力（Native AOT 为禁用占位，JIT 为真实实现）。</summary>

    public static void AddKafkaReplayOperations(

        IServiceCollection services,

        IConfiguration configuration)

    {

#if FULLNET_API_NATIVE_AOT

        services.AddFullNetDisabledKafkaReplayOperations(configuration);

#else

        services.AddFullNetKafkaReplayOperations(configuration);

#endif

    }

}

