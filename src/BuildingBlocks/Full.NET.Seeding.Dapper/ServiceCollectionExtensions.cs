using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                options => !string.IsNullOrWhiteSpace(options.DefaultLocale)
                    && options.LockTimeoutSeconds is >= 1 and <= 300,
                SeedErrorCodes.OptionsInvalid)
            .ValidateOnStart();
        return services;
    }
}
