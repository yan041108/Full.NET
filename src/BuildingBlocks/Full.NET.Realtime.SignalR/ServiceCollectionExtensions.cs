using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Full.NET.Realtime.SignalR.Health;
using OpenTelemetry.Metrics;

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
        if (AddFullNetRealtimePublisherCore(
                services,
                configuration,
                environmentName,
                requireRedisBackplane: false))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<
                IPostConfigureOptions<JwtBearerOptions>,
                JwtBearerSignalRAccessTokenPostConfigure>());
        }

        return services;
    }

    /// <summary>
    /// 注册不承载 Hub Endpoint 的实时发布能力，供 Worker 通过同一 Redis Backplane 修复推送。
    /// </summary>
    public static IServiceCollection AddFullNetRealtimePublisher(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        AddFullNetRealtimePublisherCore(
            services,
            configuration,
            environmentName,
            requireRedisBackplane: true);
        return services;
    }

    private static bool AddFullNetRealtimePublisherCore(
        IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        bool requireRedisBackplane)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var options = new RealtimeOptions();
        configuration.GetSection(RealtimeOptions.SectionName).Bind(options);
        options.RedisBackplaneConnectionString = ResolveRedisBackplaneConnectionString(
            configuration,
            options);
        Validate(options, requireRedisBackplane);
        services.AddSingleton(Options.Create(options));

        if (!options.Enabled)
        {
            services.TryAddSingleton<IRealtimePublisher>(NullRealtimePublisher.Instance);
            return false;
        }

        var signalRBuilder = services
            .AddSignalR()
            .AddMessagePackProtocol();
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(
                RealtimeBackplaneTelemetry.MeterName));

        if (!string.IsNullOrWhiteSpace(options.RedisBackplaneConnectionString))
        {
            signalRBuilder.AddStackExchangeRedis(redisOptions =>
                redisOptions.Configuration = RealtimeRedisConfiguration.Create(
                    options.RedisBackplaneConnectionString,
                    environmentName));
            services.TryAddSingleton<
                IRealtimeBackplaneProbe,
                RealtimeBackplaneProbe>();
            services.AddHealthChecks()
                .AddCheck<RealtimeBackplaneHealthCheck>(
                    "realtime-backplane",
                    tags: ["ready"]);
        }

        services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        return true;
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

    private static void Validate(
        RealtimeOptions options,
        bool requireRedisBackplane)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.HubPath)
            || !options.HubPath.StartsWith('/')
            || options.HubPath.Length == 1
            || options.HubPath.EndsWith('/')
            || options.HubPath.Contains(
                "//",
                StringComparison.Ordinal)
            || options.HubPath.Contains('?')
            || options.HubPath.Contains('#')
            || options.HubPath.Any(char.IsWhiteSpace))
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    "Realtime:HubPath must be an absolute path without whitespace, a query string, or a fragment.",
                ]);
        }

        if (requireRedisBackplane
            && string.IsNullOrWhiteSpace(
                options.RedisBackplaneConnectionString))
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    "Realtime publishing outside the API host requires Realtime:RedisBackplaneConnectionString or ConnectionStrings:redis.",
                ]);
        }
    }
}
