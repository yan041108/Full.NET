using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FullNetAppNameToken.Composition;

/// <summary>应用拥有的静态组合根；业务模块接入此清单，框架预设仍由受管目录维护。</summary>
public static class ApplicationModuleCatalog
{
    /// <summary>按宿主角色装配框架预设和应用模块。</summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">宿主配置。</param>
    /// <param name="profile">API、Worker 或 Migrator 角色。</param>
    /// <returns>宿主服务集合。</returns>
    public static IServiceCollection AddApplicationModules(
        this IServiceCollection services, IConfiguration configuration, FullNetHostProfile profile) =>
        services.AddFullNetApplicationModules(configuration, profile, CreateModules());

    // 保持标准集合表达式，供显式 CLI 接入；不扫描程序集或猜测业务模块。
    private static IReadOnlyList<IFullNetModule> CreateModules() =>
    [
    ];
}
