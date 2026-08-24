using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 为 Native AOT API 宿主注册禁用的 Kafka 范围重放占位，避免引用 Confluent.Kafka 发布闭包。
/// </summary>
public static class DisabledKafkaReplayServiceCollectionExtensions
{
    /// <summary>
    /// 注册始终关闭的 <see cref="KafkaReplayExecutionPolicy"/> 与占位 <see cref="IKafkaReplayService"/>。
    /// </summary>
    public static IServiceCollection AddFullNetDisabledKafkaReplayOperations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        _ = configuration;

        services.AddSingleton(
            new KafkaReplayExecutionPolicy(
                false,
                1_000,
                TimeSpan.FromSeconds(45)));
        services.AddScoped<IKafkaReplayService, DisabledKafkaReplayService>();
        return services;
    }
}
