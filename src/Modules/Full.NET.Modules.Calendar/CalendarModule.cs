using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Calendar.Resources;
using Full.NET.Modules.Calendar.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Calendar;

/// <summary>
/// 日历模块：提供当前用户个人日程 CRUD 与完成状态切换能力。
/// </summary>
/// <remarks>
/// 模块只依赖 Identity 解析受信用户上下文；Host 与 Tenant 会话共用权限码，数据行通过 TenantId 可空列隔离。
/// </remarks>
public sealed class CalendarModule : IFullNetModule
{
    /// <summary>获取日历模块名称。</summary>
    public string Name => "Calendar";

    /// <summary>获取日历模块运行所需的模块依赖。</summary>
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    /// <summary>注册日历模块服务、授权目录与 JSON 源生成上下文。</summary>
    /// <param name="services">应用依赖注入服务集合。</param>
    /// <param name="configuration">宿主配置根。</param>
    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            CalendarAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            CalendarErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<Features.ManageMyPersonalSchedules.PersonalScheduleQueryService>();
        services.TryAddScoped<Features.ManageMyPersonalSchedules.PersonalScheduleManagementService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                CalendarJsonSerializerContext.Default));
#if FULLNET_AOT_COMPILE
        new Persistence.CalendarDapperAotMaterializerContributor()
            .RegisterMaterializers(new global::Full.NET.Data.Dapper.DapperAotMaterializerRegistrar());
#endif
    }

    /// <summary>映射 Calendar 模块全部 HTTP 路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.ManageMyPersonalSchedules.Endpoint.Map(endpoints);
    }
}
