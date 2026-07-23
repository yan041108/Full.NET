using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Observability;

public static class HealthEndpointExtensions
{
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
