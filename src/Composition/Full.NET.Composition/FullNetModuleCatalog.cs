using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Settings;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Files;
using Full.NET.Modules.Document;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Jobs;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.SerialNumbers;
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
                services.AddFullNetModularity();
                foreach (var module in CreateModules())
                {
                    services.AddFullNetModule(module, configuration);
                }

                // 只读模块清单必须在全部模块注册后物化，禁止运行时再追加或编译加载。
                services.AddFullNetModuleCatalogSnapshot(CreateOfficialDescriptor);
                break;

            case FullNetHostProfile.Migrator:
                services.AddFullNetModularity();
                foreach (var module in CreateModules())
                {
                    module.AddMigrationServices(services, configuration);
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
        new AuditingModule(),
        new FilesModule(),
        new DocumentModule(),
        new NotificationsModule(),
        new JobsModule(),
        new TenancyModule(),
        new OrganizationModule(),
        new SettingsModule(),
        new CodeGenerationModule(),
        new SerialNumbersModule(),
    ];

    private static readonly string[] OfficialHostProfiles =
    [
        nameof(FullNetHostProfile.Api),
        nameof(FullNetHostProfile.Worker),
        nameof(FullNetHostProfile.Migrator),
    ];

    /// <summary>
    /// 由已注册模块生成官方描述符；版本取程序集版本，不暴露路径或载荷。
    /// </summary>
    private static FullNetModuleDescriptor CreateOfficialDescriptor(IFullNetModule module)
    {
        var assemblyVersion = module.GetType().Assembly.GetName().Version;
        var version = assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        return FullNetModuleDescriptor.Create(
            module.Name,
            module.Name,
            version,
            module.Dependencies,
            OfficialHostProfiles,
            FullNetModuleSourceClassification.Official,
            FullNetModuleHealthCapability.None);
    }
}
