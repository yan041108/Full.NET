using System.Net;
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
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion.Health;
using Full.NET.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Backplane;
#if !FULLNET_AOT_COMPILE
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
#endif

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
        => AddCachingCore(services, configuration, environment, isMigrator: false);

    /// <summary>为一次性 Migrator 注册模块所需的缓存依赖；不连接 Redis，也不承担跨节点缓存访问。</summary>
    /// <param name="services">迁移宿主服务集合。</param>
    /// <param name="configuration">模块策略配置。</param>
    /// <param name="environment">部署环境名。</param>
    /// <returns>迁移宿主服务集合。</returns>
    public static IServiceCollection AddFullNetMigratorCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        string environment)
        => AddCachingCore(services, configuration, environment, isMigrator: true);

    private static IServiceCollection AddCachingCore(
        IServiceCollection services, IConfiguration configuration, string environment, bool isMigrator)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var cacheOptions = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
            ?? new CacheOptions();
        var allowSharedRedis = ReadAllowSharedRedisInDevelopment(configuration);
        cacheOptions.RedisConnectionString = isMigrator ? null : ResolveRedisConnectionString(
            configuration,
            cacheOptions,
            environment,
            allowSharedRedis);

        Validate(cacheOptions);
        if (!isMigrator && IsProductionLike(environment) && string.IsNullOrWhiteSpace(cacheOptions.RedisConnectionString))
        {
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache:RedisConnectionString is required in Production/Staging."]);
        }
        EnsureIsolatedFromRealtime(
            configuration,
            cacheOptions,
            environment,
            allowSharedRedis);

        // 启动期构建注册表，未知/非法条目立即失败，避免首个请求才暴露配置错误。
        var policyRegistry = CachePolicyRegistry.Create(cacheOptions);

        services.AddSingleton(Options.Create(cacheOptions));
        services.AddSingleton<ICachePolicyRegistry>(policyRegistry);
        services.TryAddSingleton<ICacheInvalidator, FusionCacheInvalidator>();
        services.TryAddSingleton<ICacheBatchInvalidator, FusionCacheBatchInvalidator>();
        services.TryAddSingleton<CacheBackplaneHealthState>();
        services.TryAddSingleton<CacheRuntimeObservationState>();

        RegisterRedisDistributedCacheIfConfigured(
            services,
            cacheOptions,
            environment);

        // DI 拥有专属内存存储的生命周期；SDK 使用外部存储时不会代为释放。
        services.AddSingleton(_ => new FusionCacheMemoryStore(cacheOptions.L1PayloadCapacity));
        var fusionBuilder = services
            .AddFusionCache()
            .WithMemoryCache(sp => sp.GetRequiredService<FusionCacheMemoryStore>())
            .WithOptions(options =>
            {
                options.RemoveByTagBehavior = RemoveByTagBehavior.Remove;
                options.DefaultEntryOptionsProvider = new CachePolicyEntryOptionsProvider(policyRegistry);
                // 应用与环境必须同时隔离，不能由可选开关回退到共享失效通道。
                options.BackplaneChannelPrefix = $"fullnet:{environment.ToLowerInvariant()}:{cacheOptions.ApplicationName.ToLowerInvariant()}:v2";
                options.AutoRecoveryMaxItems = cacheOptions.AutoRecoveryMaxItems;
                options.AutoRecoveryMaxRetryCount = cacheOptions.AutoRecoveryMaxRetryCount;
            })
            .WithDefaultEntryOptions(options =>
            {
                options.Size = 1;
                options.Duration = cacheOptions.DefaultDuration;
                options.JitterMaxDuration = cacheOptions.Jitter;
                options.IsFailSafeEnabled = false;
                options.DistributedCacheHardTimeout = cacheOptions.RedisOperationTimeout;
            });

#if FULLNET_AOT_COMPILE
        fusionBuilder = fusionBuilder.WithSerializer(serviceProvider =>
            new BoundedFusionCacheSerializer(new FullNetFusionCacheJsonSerializer(
                serviceProvider.GetServices<ICacheJsonTypeInfoContributor>()), cacheOptions.MaxDeserializationBytes));
#else
        fusionBuilder = fusionBuilder.WithSerializer(_ => new BoundedFusionCacheSerializer(
            new FusionCacheSystemTextJsonSerializer(), cacheOptions.MaxDeserializationBytes));
#endif

        fusionBuilder
            .TryWithRegisteredDistributedCache()
            .TryWithRegisteredBackplane();
        if (cacheOptions.RedisConnectionString is not null)
        {
            // 保留 SDK 已注册的连接和生命周期，只在工厂边界添加结果观察，避免 DI 装饰递归。
            fusionBuilder.WithBackplane(sp => new ObservedFusionCacheBackplane(
                sp.GetRequiredService<IFusionCacheBackplane>(), sp.GetRequiredService<CacheBackplaneHealthState>()));
        }
        fusionBuilder.AsHybridCache();

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

    private static void RegisterRedisDistributedCacheIfConfigured(
        IServiceCollection services,
        CacheOptions cacheOptions,
        string environment)
    {
        if (cacheOptions.RedisConnectionString is not { } redisConnectionString)
        {
            return;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = CreateRedisConfiguration(redisConnectionString, cacheOptions);
            options.InstanceName = $"fullnet:{environment.ToLowerInvariant()}:{cacheOptions.ApplicationName.ToLowerInvariant()}:v2:";
        });
        services.AddFusionCacheStackExchangeRedisBackplane(options =>
            options.ConfigurationOptions = CreateRedisConfiguration(redisConnectionString, cacheOptions));
        services.TryAddSingleton<DistributedCacheHealthCheck>();
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "distributed-cache",
                sp => sp.GetRequiredService<DistributedCacheHealthCheck>(),
                failureStatus: null,
                tags: ["ready"]));
    }

    private static ConfigurationOptions CreateRedisConfiguration(string connectionString, CacheOptions options)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        var milliseconds = checked((int)options.RedisOperationTimeout.TotalMilliseconds);
        configuration.AsyncTimeout = milliseconds;
        configuration.SyncTimeout = milliseconds;
        configuration.ConnectTimeout = milliseconds;
        configuration.ConnectRetry = 0;
        return configuration;
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

        // 数据库编号、密码和连接选项不会改变物理故障域；只要端点集合重叠就拒绝。
        var cacheEndpoints = GetEffectiveRedisEndpoints(options.RedisConnectionString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var realtimeEndpoints = GetEffectiveRedisEndpoints(realtimeConnectionString);
        if (!realtimeEndpoints.Any(cacheEndpoints.Contains))
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

    /// <summary>按 SDK 实际连接的端口规则比较配置；不解析 DNS，也不把地址差异当成物理隔离证明。</summary>
    private static IEnumerable<string?> GetEffectiveRedisEndpoints(string connectionString)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        // Parse 为省略端口保留 0，连接时才补全；Sentinel 优先于 TLS，不能一律按 6379 比较。
        var defaultPort = !string.IsNullOrEmpty(configuration.ServiceName)
            ? 26379 : configuration.Ssl ? 6380 : 6379;
        return configuration.EndPoints.Select(endpoint => endpoint switch
        {
            DnsEndPoint dns when dns.Port == 0 => new DnsEndPoint(dns.Host, defaultPort, dns.AddressFamily).ToString(),
            IPEndPoint ip when ip.Port == 0 => new IPEndPoint(ip.Address, defaultPort).ToString(),
            _ => endpoint.ToString(),
        });
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

    private static bool ReadAllowSharedRedisInDevelopment(IConfiguration configuration) =>
        bool.TryParse(
            configuration[$"{RealtimeSectionName}:AllowSharedRedisInDevelopment"],
            out var allowSharedRedis)
        && allowSharedRedis;

    private static void Validate(CacheOptions options)
    {
        if (options.L1PayloadCapacity is < 1 or > 1_000_000)
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache:L1PayloadCapacity must be between 1 and 1000000 entry weight units."]);

        if (options.InvalidationMaxBatchItems is < 1 or > 10_000
            || options.InvalidationMaxBatchBytes is < 1 or > 8 * 1024 * 1024
            || options.InvalidationMaxConcurrency is < 1 or > 32
            || options.InvalidationMaxConcurrentBatches is < 1 or > 256)
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache invalidation batch limits must be 1-10000 items, 1-8388608 UTF-8 bytes, 1-32 concurrent operations and 1-256 concurrent batches."]);

        if (options.MaxDeserializationBytes is < 1 or > CacheOptions.MaximumDeserializationBytes)
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache:MaxDeserializationBytes must be between 1 and 67108864 bytes."]);

        if (string.IsNullOrWhiteSpace(options.ApplicationName) || options.ApplicationName.Length > 64
            || options.ApplicationName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
        {
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache:ApplicationName must contain 1-64 ASCII letters, digits, dot, underscore or hyphen."]);
        }
        if (options.InvalidationTimeout < TimeSpan.FromMilliseconds(1) || options.InvalidationTimeout > TimeSpan.FromMinutes(1)
            // 诊断快照最大年龄与后台轮询周期同量级，防止临时放宽在轮询故障时无限滞留。
            || options.DiagnosticSnapshotMaxAge < TimeSpan.FromSeconds(30) || options.DiagnosticSnapshotMaxAge > TimeSpan.FromMinutes(5)
            || options.RedisOperationTimeout < TimeSpan.FromMilliseconds(1) || options.RedisOperationTimeout > TimeSpan.FromSeconds(30)
            || options.AutoRecoveryMaxItems is < 1 or > 100_000 || options.AutoRecoveryMaxRetryCount is < 1 or > 100)
        {
            throw new OptionsValidationException(CacheOptions.SectionName, typeof(CacheOptions),
                ["Cache invalidation/Redis timeouts and recovery queue/retry limits are outside their supported bounds."]);
        }

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
