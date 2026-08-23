using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Full.NET.Realtime.SignalR.Health;
using Full.NET.Realtime.SignalR.Serialization;
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

        var options = configuration
                .GetSection(RealtimeOptions.SectionName)
                .Get<RealtimeOptions>()
            ?? new RealtimeOptions();
        options.RedisBackplaneConnectionString = ResolveRedisBackplaneConnectionString(
            configuration,
            options,
            environmentName);
        Validate(options, requireRedisBackplane, environmentName);
        EnsureIsolatedFromCache(
            configuration,
            options,
            environmentName);
        services.AddSingleton(Options.Create(options));

        if (!options.Enabled)
        {
            services.TryAddSingleton<IRealtimePublisher>(NullRealtimePublisher.Instance);
            return false;
        }

        var signalRBuilder = services.AddSignalR();
#if FULLNET_SIGNALR_MESSAGEPACK
        signalRBuilder.AddMessagePackProtocol();
#endif
        signalRBuilder.AddJsonProtocol(jsonOptions =>
        {
            jsonOptions.PayloadSerializerOptions.PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase;
            jsonOptions.PayloadSerializerOptions.DictionaryKeyPolicy =
                JsonNamingPolicy.CamelCase;
            jsonOptions.PayloadSerializerOptions.TypeInfoResolverChain.Insert(
                0,
                RealtimeJsonSerializerContext.Default);
        });
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
            services.TryAddSingleton<RealtimeBackplaneHealthCheck>();
            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    "realtime-backplane",
                    sp => sp.GetRequiredService<RealtimeBackplaneHealthCheck>(),
                    failureStatus: null,
                    tags: ["ready"]));
        }

        services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        return true;
    }

    private static string? ResolveRedisBackplaneConnectionString(
        IConfiguration configuration,
        RealtimeOptions options,
        string environmentName)
    {
        if (!string.IsNullOrWhiteSpace(options.RedisBackplaneConnectionString))
        {
            return options.RedisBackplaneConnectionString.Trim();
        }

        // 生产禁止回退共享 ConnectionStrings:redis，避免与 Cache 隐式共用故障域。
        if (IsProductionLike(environmentName)
            || !options.AllowSharedRedisInDevelopment)
        {
            return null;
        }

        var connectionString = configuration.GetConnectionString("redis");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
    }

    private static void EnsureIsolatedFromCache(
        IConfiguration configuration,
        RealtimeOptions options,
        string environmentName)
    {
        if (string.IsNullOrWhiteSpace(options.RedisBackplaneConnectionString))
        {
            return;
        }

        var cacheConnectionString = ResolveCacheConnectionStringForComparison(
            configuration,
            environmentName,
            options.AllowSharedRedisInDevelopment);
        if (string.IsNullOrWhiteSpace(cacheConnectionString))
        {
            return;
        }

        if (!ConnectionStringsEqual(
                cacheConnectionString,
                options.RedisBackplaneConnectionString))
        {
            return;
        }

        if (!IsProductionLike(environmentName)
            && options.AllowSharedRedisInDevelopment)
        {
            return;
        }

        throw new OptionsValidationException(
            RealtimeOptions.SectionName,
            typeof(RealtimeOptions),
            [
                "Cache:RedisConnectionString and Realtime:RedisBackplaneConnectionString must differ in Production/Staging; "
                + "shared Redis is only allowed in Development/Testing when Realtime:AllowSharedRedisInDevelopment=true.",
            ]);
    }

    private static string? ResolveCacheConnectionStringForComparison(
        IConfiguration configuration,
        string environmentName,
        bool allowSharedInDevelopment)
    {
        var dedicated = configuration[$"{CacheSectionName}:RedisConnectionString"];
        if (!string.IsNullOrWhiteSpace(dedicated))
        {
            return dedicated.Trim();
        }

        if (IsProductionLike(environmentName) || !allowSharedInDevelopment)
        {
            return null;
        }

        var shared = configuration.GetConnectionString("redis");
        return string.IsNullOrWhiteSpace(shared) ? null : shared.Trim();
    }

    // 避免 BuildingBlocks 循环引用；仅读取与 CacheOptions.SectionName 对齐的配置键。
    private const string CacheSectionName = "Cache";

    private static void Validate(
        RealtimeOptions options,
        bool requireRedisBackplane,
        string environmentName)
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

        ValidateTransportContract(options);

        if (requireRedisBackplane
            && string.IsNullOrWhiteSpace(
                options.RedisBackplaneConnectionString))
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    IsProductionLike(environmentName)
                        ? "Realtime publishing outside the API host requires Realtime:RedisBackplaneConnectionString."
                        : "Realtime publishing outside the API host requires Realtime:RedisBackplaneConnectionString "
                            + "(or ConnectionStrings:redis when Realtime:AllowSharedRedisInDevelopment=true).",
                ]);
        }
    }

    private static void ValidateTransportContract(RealtimeOptions options)
    {
        if (options.SkipNegotiation
            && options.TransportMode != RealtimeTransportMode.WebSocketsOnly)
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    "Realtime:SkipNegotiation requires TransportMode=WebSocketsOnly.",
                ]);
        }

        var affinityMayBeDisabled = options.TransportMode
                == RealtimeTransportMode.WebSocketsOnly
            && options.SkipNegotiation;
        if (!options.RequireSessionAffinity && !affinityMayBeDisabled)
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    "Realtime:RequireSessionAffinity may be false only when TransportMode=WebSocketsOnly and SkipNegotiation=true.",
                ]);
        }

        if (options.TransportMode == RealtimeTransportMode.Default
            && !options.RequireSessionAffinity)
        {
            throw new OptionsValidationException(
                RealtimeOptions.SectionName,
                typeof(RealtimeOptions),
                [
                    "Realtime:TransportMode=Default requires RequireSessionAffinity=true.",
                ]);
        }
    }

    internal static bool IsProductionLike(string environmentName) =>
        string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            environmentName,
            Environments.Staging,
            StringComparison.OrdinalIgnoreCase);

    internal static bool ConnectionStringsEqual(string left, string right) =>
        string.Equals(
            left.Trim(),
            right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
