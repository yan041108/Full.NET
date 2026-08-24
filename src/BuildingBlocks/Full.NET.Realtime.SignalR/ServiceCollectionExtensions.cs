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
/// 注册 SignalR Hub、JSON 协议与 <see cref="IRealtimePublisher"/> 实现。
/// </summary>
/// <remarks>
/// <para>该扩展是 API Host 启用实时推送的统一入口，禁止业务模块自行注册 Hub；
/// 当 <c>Realtime:Enabled=false</c> 或缺省 Redis Backplane 时，回退为 <see cref="NullRealtimePublisher"/>，
/// 业务代码无须感知发布器是否存在。</para>
/// <para>生产与 Staging 环境强制 Realtime Redis 与 Cache Redis 连接串不同；开发环境可通过
/// <c>Realtime:AllowSharedRedisInDevelopment=true</c> 显式允许共用，避免误带入生产故障域耦合。</para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 为 API Host 注册 SignalR Hub Endpoint 与 <see cref="IRealtimePublisher"/> 实现。
    /// </summary>
    /// <param name="services">服务集合，扩展方法基于此追加 Hub、Backplane、健康检查与遥测注册。</param>
    /// <param name="configuration">应用配置根，用于读取 <c>Realtime</c> 段、连接串与 Cache 段以做隔离校验。</param>
    /// <param name="environmentName">当前环境名（Production/Staging/Development 等），决定 Redis 隔离策略。</param>
    /// <returns>传入的服务集合，便于链式注册。</returns>
    /// <remarks>
    /// 启用 Hub 时会同步注册 JwtBearer 后置配置以支持 SignalR 鉴权令牌规范化；
    /// 未启用 Realtime 时仅注册 <see cref="NullRealtimePublisher"/>，且不注册 Hub Endpoint。
    /// </remarks>
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
    /// <param name="services">服务集合，扩展方法基于此追加发布器、Backplane 与健康检查注册。</param>
    /// <param name="configuration">应用配置根，用于读取 <c>Realtime</c> 段与连接串。</param>
    /// <param name="environmentName">当前环境名；非生产环境可按配置回退到 <c>ConnectionStrings:redis</c>。</param>
    /// <returns>传入的服务集合，便于链式注册。</returns>
    /// <remarks>
    /// Worker 等不承载 Hub Endpoint 的宿主调用本方法时必须显式提供 Backplane 连接串，
    /// 否则会被 <c>Validate</c> 拒绝；该路径不注册 JwtBearer 后置配置。
    /// </remarks>
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
