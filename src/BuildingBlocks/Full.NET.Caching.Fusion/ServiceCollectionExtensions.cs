using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ZiggyCreatures.Caching.Fusion;
using Full.NET.Caching.Fusion.Health;

namespace Full.NET.Caching.Fusion;

public static class ServiceCollectionExtensions
{
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
        cacheOptions.RedisConnectionString = ResolveRedisConnectionString(configuration, cacheOptions);

        Validate(cacheOptions);

        services.AddSingleton(Options.Create(cacheOptions));

        if (cacheOptions.RedisConnectionString is { } redisConnectionString)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = $"fullnet:{environment.ToLowerInvariant()}:";
            });
            services.AddFusionCacheStackExchangeRedisBackplane(options =>
                options.Configuration = redisConnectionString);
            services.AddHealthChecks()
                .AddCheck<DistributedCacheHealthCheck>(
                    "distributed-cache",
                    tags: ["ready"]);
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

        services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddFusionCacheInstrumentation())
            .WithMetrics(metrics => metrics.AddFusionCacheInstrumentation());

        return services;
    }

    private static string? ResolveRedisConnectionString(
        IConfiguration configuration,
        CacheOptions options)
    {
        var connectionString = string.IsNullOrWhiteSpace(options.RedisConnectionString)
            ? configuration.GetConnectionString("redis")
            : options.RedisConnectionString;

        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }

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
    }
}
