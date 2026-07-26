using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Seeding.Abstractions;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Seeding.Dapper;

/// <summary>
/// 提供 Seed 基础设施的依赖注入装配入口。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Seed 非敏感配置；具体执行器在基础设施完成后由同一入口追加。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetSeeding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .Validate(
                options => IsValidLocale(options.DefaultLocale)
                    && options.LockTimeoutSeconds is >= 1 and <= 300,
                SeedErrorCodes.OptionsInvalid)
            .ValidateOnStart();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<ISeedExecutionLeaseProvider, SeedExecutionLease>();
        services.TryAddScoped<ISeedExecutionStore, SeedExecutionStore>();
        services.TryAddScoped<ISeedOrchestrator, SeedOrchestrator>();
        return services;
    }

    private static bool IsValidLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(locale.Trim());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
