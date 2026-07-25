using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Full.NET.Realtime.SignalR;

/// <summary>
/// 注册 SignalR Hub、MessagePack 协议与 <see cref="IRealtimePublisher"/> 实现。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFullNetRealtimeSignalR(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var options = new RealtimeOptions();
        configuration.GetSection(RealtimeOptions.SectionName).Bind(options);
        options.RedisBackplaneConnectionString = ResolveRedisBackplaneConnectionString(
            configuration,
            options);
        services.AddSingleton(Options.Create(options));

        if (!options.Enabled)
        {
            services.TryAddSingleton<IRealtimePublisher>(NullRealtimePublisher.Instance);
            return services;
        }

        var signalRBuilder = services
            .AddSignalR()
            .AddMessagePackProtocol();

        if (!string.IsNullOrWhiteSpace(options.RedisBackplaneConnectionString))
        {
            signalRBuilder.AddStackExchangeRedis(options.RedisBackplaneConnectionString, redisOptions =>
            {
                redisOptions.Configuration.ChannelPrefix =
                    RedisChannel.Literal($"fullnet:{environmentName.ToLowerInvariant()}:signalr:");
            });
        }

        services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPostConfigureOptions<JwtBearerOptions>,
            JwtBearerSignalRAccessTokenPostConfigure>());
        return services;
    }

    private static string? ResolveRedisBackplaneConnectionString(
        IConfiguration configuration,
        RealtimeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RedisBackplaneConnectionString))
        {
            return options.RedisBackplaneConnectionString;
        }

        var connectionString = configuration.GetConnectionString("redis");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }
}
