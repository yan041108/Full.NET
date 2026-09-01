using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Modules;

/// <summary>
/// Full.NET 模块注册的统一入口；每个业务模块必须提供一个稳定的公共实现。
/// </summary>
public interface IFullNetModule
{
    /// <summary>
    /// 获取模块在依赖图中的唯一稳定键；键按区分大小写的 Ordinal 规则比较，发布后不得随 CLR 类型重命名而变化。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 获取当前模块依赖的稳定模块键；每个键必须对应已注册模块，且依赖图必须保持无循环。
    /// </summary>
    IReadOnlyCollection<string> Dependencies { get; }

    /// <summary>
    /// 注册模块在 HTTP 宿主（Host.Api 等）中需要的完整服务集合，包括应用服务、仓储、校验器、事件订阅等。
    /// </summary>
    /// <param name="services">宿主的服务集合，扩展追加注册。</param>
    /// <param name="configuration">宿主配置根，用于读取模块配置节。</param>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// 注册模块在 Migrator 等迁移/Seed 宿主中需要的最小闭包，只包含 Contributor 及其传递依赖。
    /// </summary>
    /// <remarks>
    /// 默认没有迁移/Seed 专用注册。若模块存在真实 <c>IDataSeedContributor</c> 或迁移宿主依赖，
    /// 必须显式实现该方法，避免 Migrator 复用完整 <see cref="AddServices"/> 装入 HTTP、认证与权限运行时。
    /// </remarks>
    /// <param name="services">迁移宿主的服务集合。</param>
    /// <param name="configuration">迁移宿主配置根。</param>
    void AddMigrationServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// 映射模块对外暴露的 HTTP 端点（API Controller、Minimal API、gRPC 等）；宿主在路由阶段统一调用。
    /// </summary>
    /// <remarks>
    /// 端点注册需遵循模块的路由前缀与授权策略约定；若模块不提供 HTTP 能力，可留空实现。
    /// </remarks>
    /// <param name="endpoints">宿主端点路由构建器。</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>
    /// 注册模块在非 HTTP 宿主（如 Worker）也需要的最小后台能力，例如事件处理器与其依赖的上下文。
    /// </summary>
    /// <remarks>
    /// 完整宿主的 <see cref="AddServices"/> 必须自行包含这些注册；本方法用于让 Worker Profile
    /// 只装配后台能力而不引入 HTTP、认证与完整模块依赖图。默认无后台能力。
    /// </remarks>
    /// <param name="services">后台宿主的服务集合。</param>
    /// <param name="configuration">后台宿主配置根。</param>
    void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// 在指定管道阶段贡献模块自有中间件，由宿主在固定插入点统一调用，避免宿主直接引用具体模块。
    /// </summary>
    /// <remarks>
    /// 宿主只按 <see cref="ModulePipelineStage"/> 声明的顺序调用一次；模块必须自行判断阶段，
    /// 只在需要的阶段注册中间件。默认不贡献任何中间件。
    /// </remarks>
    /// <param name="app">宿主应用构建器，用于注册中间件。</param>
    /// <param name="stage">当前正在执行的管道阶段。</param>
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
    }
}
