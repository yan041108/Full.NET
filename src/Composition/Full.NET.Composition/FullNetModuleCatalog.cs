using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Organization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Composition;

/// <summary>
/// 集中维护官方模块与宿主 Profile 的显式映射，禁止宿主各自复制模块清单。
/// </summary>
public static class FullNetModuleCatalog
{
    /// <summary>浏览器管理端使用的精确来源 CORS 策略名称。</summary>
    public const string BrowserCorsPolicy = IdentityModule.BrowserCorsPolicy;

    /// <summary>
    /// 按宿主 Profile 注册完整模块或最小后台能力。
    /// </summary>
    public static IServiceCollection AddFullNetApplicationModules(
        this IServiceCollection services,
        IConfiguration configuration,
        FullNetHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        switch (profile)
        {
            case FullNetHostProfile.Api:
            case FullNetHostProfile.Migrator:
                services.AddFullNetModularity();
                foreach (var module in CreateModules())
                {
                    services.AddFullNetModule(module, configuration);
                }

                break;

            case FullNetHostProfile.Worker:
                // Worker 只装配各模块声明的后台能力，避免把 HTTP、认证和完整模块依赖图带入后台进程。
                foreach (var module in CreateModules())
                {
                    module.AddBackgroundServices(services, configuration);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    profile,
                    "未知的 Full.NET 宿主 Profile。");
        }

        return services;
    }

    /// <summary>
    /// 官方模块的唯一集中清单，按依赖顺序排列；新增模块只在此追加一行。
    /// </summary>
    private static IReadOnlyList<IFullNetModule> CreateModules() =>
    [
        new IdentityModule(),
        new TenancyModule(),
        new OrganizationModule(),
    ];
}
