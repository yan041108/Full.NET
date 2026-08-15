using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using Full.NET.Caching.Fusion.Health;

namespace Full.NET.Caching.Fusion;

/// <summary>
/// FusionCache + Redis 分层缓存的 <see cref="IServiceCollection"/> 注册入口。
/// 在启动期完成配置校验、策略注册表构建、Backplane 隔离检查与 OTel 指标桥接，
/// 避免首个业务请求才暴露缓存配置错误。
/// </summary>
public static class ServiceCollectionExtensions
{
    // 与 RealtimeOptions.SectionName 对齐，避免 BuildingBlocks 循环引用。
    private const string RealtimeSectionName = "Realtime";

    /// <summary>
    /// 注册 Full.NET 受治理缓存基础设施：绑定 <see cref="CacheOptions"/>、
    /// 构建并校验 <see cref="ICachePolicyRegistry"/>、配置 FusionCache + Redis L2 + Backplane、
    /// 注入可靠性监控 <see cref="FusionCacheReliabilityMonitor"/> 与 OTel Tracing/Metrics。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">应用配置根，读取 <c>Cache</c> 与 <c>Realtime</c> 节。</param>
    /// <param name="environment">规范化部署环境名，用于 Redis InstanceName 前缀与生产/开发策略判断。</param>
    /// <exception cref="OptionsValidationException">配置不满足契约（TTL 非正、Redis 共享冲突等）时启动期直接抛出。</exception>
    public static IServiceCollection AddFullNetCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        string environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var cacheOptions = new CacheOptions();
        configuration.GetSection(CacheOptions.SectionName).Bind(cacheOptions);
        var allowSharedRedis = configuration.GetValue(
            $"{RealtimeSectionName}:AllowSharedRedisInDevelopment",
            defaultValue: false);
        cacheOptions.RedisConnectionString = ResolveRedisConnectionString(
            configuration,
            cacheOptions,
            environment,
            allowSharedRedis);

        Validate(cacheOptions);
        EnsureIsolatedFromRealtime(
            configuration,
            cacheOptions,
            environment,
            allowSharedRedis);

        // 启动期构建注册表，未知/非法条目立即失败，避免首个请求才暴露配置错误。
        var policyRegistry = CachePolicyRegistry.Create(cacheOptions);

        services.AddSingleton(Options.Create(cacheOptions));
        services.AddSingleton<ICachePolicyRegistry>(policyRegistry);

        if (cacheOptions.RedisConnectionString is { } redisConnectionString)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = $"fullnet:{environment.ToLowerInvariant()}:";
            });
            services.AddFusionCacheStackExchangeRedisBackplane(options =>
                options.Configuration = redisConnectionString);
            services.TryAddSingleton<DistributedCacheHealthCheck>();
            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    "distributed-cache",
                    sp => sp.GetRequiredService<DistributedCacheHealthCheck>(),
                    failureStatus: null,
                    tags: ["ready"]));
        }

        services
            .AddFusionCache()
            .WithOptions(options =>
            {
                // 安全关键缓存要求提交后尽快摘除旧条目；标签仅做惰性屏蔽会放大多节点陈旧窗口。
                options.RemoveByTagBehavior = RemoveByTagBehavior.Remove;
            })
            .WithDefaultEntryOptions(options =>
            {
                options.Duration = cacheOptions.DefaultDuration;
                options.JitterMaxDuration = cacheOptions.Jitter;
                options.IsFailSafeEnabled = false;
            })
            .WithSystemTextJsonSerializer()
            .TryWithRegisteredDistributedCache()
            .TryWithRegisteredBackplane()
            .AsHybridCache();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostedService,
                FusionCacheReliabilityMonitor>());
        services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddFusionCacheInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(CacheReliabilityTelemetry.MeterName)
                .AddFusionCacheInstrumentation());

        return services;
    }

    private static string? ResolveRedisConnectionString(
        IConfiguration configuration,
        CacheOptions options,
        string environment,
        bool allowSharedRedisInDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(options.RedisConnectionString))
        {
            return options.RedisConnectionString.Trim();
        }

        // 生产禁止静默回退共享 ConnectionStrings:redis。
        if (IsProductionLike(environment) || !allowSharedRedisInDevelopment)
        {
            return null;
        }

        var connectionString = configuration.GetConnectionString("redis");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
    }

    private static void EnsureIsolatedFromRealtime(
        IConfiguration configuration,
        CacheOptions options,
        string environment,
        bool allowSharedRedisInDevelopment)
    {
        if (string.IsNullOrWhiteSpace(options.RedisConnectionString))
        {
            return;
        }

        var realtimeConnectionString = ResolveRealtimeConnectionStringForComparison(
            configuration,
            environment,
            allowSharedRedisInDevelopment);
        if (string.IsNullOrWhiteSpace(realtimeConnectionString))
        {
            return;
        }

        if (!string.Equals(
                options.RedisConnectionString.Trim(),
                realtimeConnectionString.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsProductionLike(environment) && allowSharedRedisInDevelopment)
        {
            return;
        }

        throw new OptionsValidationException(
            CacheOptions.SectionName,
            typeof(CacheOptions),
            [
                "Cache:RedisConnectionString and Realtime:RedisBackplaneConnectionString must differ in Production/Staging; "
                + "shared Redis is only allowed in Development/Testing when Realtime:AllowSharedRedisInDevelopment=true.",
            ]);
    }

    private static string? ResolveRealtimeConnectionStringForComparison(
        IConfiguration configuration,
        string environment,
        bool allowSharedRedisInDevelopment)
    {
        var dedicated = configuration[
            $"{RealtimeSectionName}:RedisBackplaneConnectionString"];
        if (!string.IsNullOrWhiteSpace(dedicated))
        {
            return dedicated.Trim();
        }

        if (IsProductionLike(environment) || !allowSharedRedisInDevelopment)
        {
            return null;
        }

        var shared = configuration.GetConnectionString("redis");
        return string.IsNullOrWhiteSpace(shared) ? null : shared.Trim();
    }

    private static bool IsProductionLike(string environment) =>
        string.Equals(
            environment,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            environment,
            Environments.Staging,
            StringComparison.OrdinalIgnoreCase);

    private static void Validate(CacheOptions options)
    {
        if (options.DefaultDuration <= TimeSpan.Zero)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                ["Cache:DefaultDuration must be greater than zero."]);
        }

        if (options.Jitter < TimeSpan.Zero)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                ["Cache:Jitter cannot be negative."]);
        }

        if (options.RedisConnectionString is null)
        {
            return;
        }

        try
        {
            _ = ConfigurationOptions.Parse(options.RedisConnectionString);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException)
        {
            throw new OptionsValidationException(
                CacheOptions.SectionName,
                typeof(CacheOptions),
                ["Cache:RedisConnectionString has an invalid format."]);
        }
    }
}
