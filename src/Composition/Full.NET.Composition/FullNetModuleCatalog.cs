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
using Full.NET.Modules.Messaging;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.SerialNumbers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Composition;

/// <summary>
/// 集中维护官方模块与宿主 Profile 的显式映射，禁止宿主各自复制模块清单。
/// </summary>
/// <remarks>
/// <para>Composition 组合根是代码库中<strong>唯一</strong>允许直接引用具体模块实现类型（如 <c>IdentityModule</c>）的位置。
/// 宿主项目（Api/Worker/Migrator）必须通过本类提供的扩展方法完成装配，禁止自行 new 模块实例或调用 <c>AddModule{T}</c>。</para>
/// <para>模块注册遵循依赖顺序：先置模块（Tenancy→Identity→Organization）优先注册，
/// 保证下游业务模块解析基础设施时无顺序依赖问题。</para>
/// </remarks>
public static class FullNetModuleCatalog
{
    /// <summary>
    /// 浏览器管理端使用的精确来源 CORS 策略名称。
    /// </summary>
    /// <remarks>
    /// 由 IdentityModule 统一定义，API Host 通过 <c>UseCors(BrowserCorsPolicy)</c> 启用，
    /// 允许管理端前端域名访问后端 API。
    /// </remarks>
    public const string BrowserCorsPolicy = IdentityModule.BrowserCorsPolicy;

    /// <summary>
    /// 按宿主 Profile 注册完整模块或最小后台能力。
    /// </summary>
    /// <param name="services">宿主 DI 服务集合。</param>
    /// <param name="configuration">宿主配置根，用于绑定模块级 Options。</param>
    /// <param name="profile">宿主角色装配 Profile，决定启用的注入入口。</param>
    /// <returns>链式返回 <paramref name="services"/>。</returns>
    /// <remarks>
    /// <para>显式逐个调用 <c>AddModule{T}</c>，<strong>不做</strong>程序集扫描，保证依赖关系可见且可被架构测试断言。</para>
    /// <para>按 Profile 选择性注入：</para>
    /// <list type="table">
    /// <listheader>
    /// <term>Profile</term>
    /// <description>调用的注入入口</description>
    /// </listheader>
    /// <item>
    /// <term><see cref="FullNetHostProfile.Api"/></term>
    /// <description><c>AddServices(services, config)</c> + 物化只读目录快照</description>
    /// </item>
    /// <item>
    /// <term><see cref="FullNetHostProfile.Worker"/></term>
    /// <description><c>AddBackgroundServices(services, config)</c>（只装配后台消费者最小依赖）</description>
    /// </item>
    /// <item>
    /// <term><see cref="FullNetHostProfile.Migrator"/></term>
    /// <description><c>AddMigrationServices(services, config)</c>（只装配迁移/初始化领域服务）</description>
    /// </item>
    /// <item>
    /// <term><see cref="FullNetHostProfile.Test"/></term>
    /// <description>由测试项目自行控制模块子集，宿主入口不直接使用</description>
    /// </item>
    /// </list>
    /// </remarks>
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
        new MessagingModule(),
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
