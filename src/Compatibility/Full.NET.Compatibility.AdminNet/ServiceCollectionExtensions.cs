using Full.NET.Hosting.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Compatibility.AdminNet;

/// <summary>
/// Admin.NET 兼容层的服务注册入口；仅在需要复用 Admin.NET 统一响应信封的部署中启用。
/// </summary>
/// <remarks>
/// 该扩展只替换 <see cref="IApiResultMapper"/> 的默认实现为 <see cref="AdminNetApiResultMapper"/>，
/// 不影响标准 ProblemDetails 与真实 HTTP 状态码语义；未调用本扩展时 API 仍走标准映射。
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 用 Admin.NET 兼容信封映射器替换标准 <see cref="IApiResultMapper"/>。
    /// </summary>
    /// <param name="services">宿主 DI 服务集合。</param>
    /// <returns>链式返回 <paramref name="services"/>。</returns>
    /// <remarks>
    /// 替换为 Singleton：<see cref="AdminNetApiResultMapper"/> 无状态且依赖的本地化器、
    /// 语言上下文与 PreV1 错误码策略均由 DI 解析。ProblemDetails 异常处理路径与 HTTP 状态码不受此替换影响。
    /// </remarks>
    public static IServiceCollection AddAdminNetCompatibility(
        this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<
            IApiResultMapper,
            AdminNetApiResultMapper>());
        return services;
    }
}
