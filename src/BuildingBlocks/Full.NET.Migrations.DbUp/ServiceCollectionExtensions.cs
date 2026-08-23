using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 注册 DbUp 迁移器与默认关闭的 Contract 维护窗口门禁，供 Migrator 宿主装配迁移能力。
/// </summary>
/// <remarks>
/// <para>本扩展只能由 Migrator 调用；API Host 与 Worker 不得注册或执行迁移，
/// 否则会绕过运行角色分离与数据库权限隔离边界。Contract 维护证据默认关闭，
/// 仅在显式提供配置时才会进入破坏性 DDL 的维护窗口。</para>
/// <para>本扩展同步注册 <see cref="UuidBinaryContractOptions"/> 与 <see cref="PreV1NamingContractOptions"/>
/// 的选项校验，破坏性 DDL 豁免标识在启动阶段即被校验为合规格式，避免运行时再失败。</para>
/// </remarks>
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
        services.AddOptions<PreV1NamingContractOptions>();
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
        services.AddOptions<PreV1NamingContractOptions>()
            .Bind(configuration.GetSection(PreV1NamingContractOptions.SectionName))
            .Validate(
                options => string.IsNullOrEmpty(options.DestructiveDdlApprovalId)
                    || UuidBinaryContractOptions.IsApprovalIdValid(
                        options.DestructiveDdlApprovalId),
                "PreV1NamingContract:DestructiveDdlApprovalId has an invalid format.")
            .ValidateOnStart();
        services.AddSingleton<IDatabaseMigrationRunner, DbUpMigrationRunner>();
        return services;
    }
}
