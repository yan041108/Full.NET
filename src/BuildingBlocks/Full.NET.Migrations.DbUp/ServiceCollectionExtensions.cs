using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Migrations.DbUp;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 使用默认关闭的 UUID Contract 门禁注册 DbUp 迁移器。
    /// </summary>
    /// <remarks>
    /// 保留该重载用于源码兼容；需要执行 009 时必须改用带配置的重载并显式提供维护证据。
    /// </remarks>
    /// <param name="services">宿主服务集合。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetMigrations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<UuidBinaryContractOptions>();
        services.AddSingleton<IDatabaseMigrationRunner, DbUpMigrationRunner>();
        return services;
    }

    /// <summary>
    /// 注册 DbUp 迁移器及默认关闭的 UUID Contract 维护窗口门禁。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">宿主最终配置。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    public static IServiceCollection AddFullNetMigrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<UuidBinaryContractOptions>()
            .Bind(configuration.GetSection(UuidBinaryContractOptions.SectionName))
            .Validate(
                options => string.IsNullOrEmpty(options.DestructiveDdlApprovalId)
                    || UuidBinaryContractOptions.IsApprovalIdValid(
                        options.DestructiveDdlApprovalId),
                "UuidBinaryContract:DestructiveDdlApprovalId has an invalid format.")
            .ValidateOnStart();
        services.AddSingleton<IDatabaseMigrationRunner, DbUpMigrationRunner>();
        return services;
    }
}
