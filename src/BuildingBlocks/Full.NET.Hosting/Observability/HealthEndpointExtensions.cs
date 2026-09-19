using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 注册 Full.NET 三段式健康检查端点（live/ready/startup）的扩展方法集合。
/// </summary>
/// <remarks>
/// <para>端点约定：</para>
/// <list type="number">
/// <item>/health/live 始终返回 200，仅用于进程存活探针，不依赖任何 HealthCheck；</item>
/// <item>/health/ready 仅检查带 "ready" 标签的 HealthCheck，用于流量就绪探针；</item>
/// <item>/health/startup 仅检查带 "startup" 标签的 HealthCheck，用于启动门禁。</item>
/// </list>
/// 映射 ready/startup 端点前必须确保对应标签下至少有一个真实 HealthCheck 注册，否则抛出 <see cref="InvalidOperationException"/>。
/// </remarks>
public static class HealthEndpointExtensions
{
    /// <summary>
    /// 映射 /health/live、/health/ready、/health/startup 三个端点，并校验对应标签下已注册真实健康检查。
    /// </summary>
    /// <param name="endpoints">路由构造器，由 WebApplication.Map 扩展链提供。</param>
    /// <returns>原路由构造器，便于链式映射。</returns>
    /// <exception cref="InvalidOperationException">映射 ready/startup 端点时未找到对应标签的健康检查注册。</exception>
    public static IEndpointRouteBuilder MapFullNetHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        EnsureTaggedChecksRegistered(endpoints, "ready", "/health/ready");
        EnsureTaggedChecksRegistered(endpoints, "startup", "/health/startup");
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });
        endpoints.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("startup")
        });
        return endpoints;
    }

    private static void EnsureTaggedChecksRegistered(
        IEndpointRouteBuilder endpoints,
        string tag,
        string path)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        if (!options.Registrations.Any(registration => registration.Tags.Contains(tag)))
        {
            throw new InvalidOperationException(
                $"映射 {path} 前必须先注册至少一个带有 {tag} 标签的真实健康检查。");
        }
    }
}
