using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Platform.Resources;
using Full.NET.Modules.Platform.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Platform;

/// <summary>
/// 平台模块：提供 Host 更新日志管理与用户已读能力。
/// </summary>
/// <remarks>
/// 更新日志为 Host 级内容；终端用户通过 Host 与 Tenant 会话读取已发布记录并标记已读。
/// </remarks>
public sealed class PlatformModule : IFullNetModule
{
    /// <summary>获取平台模块名称。</summary>
    public string Name => "Platform";

    /// <summary>获取平台模块运行所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>注册平台模块服务、授权目录与 JSON 源生成上下文。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">宿主配置根。</param>
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            PlatformAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            PlatformErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageHostReleaseNotes.HostReleaseNoteQueryService>();
        services.TryAddScoped<Features.ManageHostReleaseNotes.HostReleaseNoteManagementService>();
        services.TryAddScoped<Features.ManageMyReleaseNotes.MyReleaseNoteQueryService>();
        services.TryAddScoped<Features.ManageMyReleaseNotes.MyReleaseNoteManagementService>();
        services.Configure<Configuration.PlatformBackupExecutorOptions>(
            configuration.GetSection(Configuration.PlatformBackupExecutorOptions.SectionName));
        services.TryAddScoped<Features.ManageBackupExecutor.BackupExecutorStatusService>();
        services.TryAddScoped<Features.ManageBackupExecutor.BackupTaskQueryService>();
        services.TryAddScoped<Features.ManageBackupExecutor.BackupRunQueryService>();
        services.TryAddScoped<Features.ManageBackupExecutor.BackupRunArtifactService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                PlatformJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.PlatformDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射 Platform 模块全部 HTTP 路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageHostReleaseNotes.Endpoint.Map(endpoints);
        Features.ManageMyReleaseNotes.Endpoint.Map(endpoints);
        Features.ManageBackupExecutor.Endpoint.Map(endpoints);
    }
}
