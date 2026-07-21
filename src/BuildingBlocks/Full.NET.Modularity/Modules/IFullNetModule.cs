using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Modules;

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

    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>
    /// 注册模块在非 HTTP 宿主（如 Worker）也需要的最小后台能力，例如事件处理器与其依赖的上下文。
    /// </summary>
    /// <remarks>
    /// 完整宿主的 <see cref="AddServices"/> 必须自行包含这些注册；本方法用于让 Worker Profile
    /// 只装配后台能力而不引入 HTTP、认证与完整模块依赖图。默认无后台能力。
    /// </remarks>
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
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
    }
}
