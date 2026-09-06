using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ObservabilityAdmin.Features.MonitorElasticsearchLogPipeline;

/// <summary>Elasticsearch 日志管道健康检查 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册 Elasticsearch 日志管道健康路由。</summary>
    /// <param name="endpoints">应用程序路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/observability/elasticsearch-log-pipeline")
            .WithTags("ObservabilityElasticsearchLogPipeline");

        group.MapGet("/health", async (
            ElasticsearchLogPipelineHealthService healthService,
            CancellationToken cancellationToken) =>
        {
            var result = await healthService.GetHealthAsync(cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("observabilityGetElasticsearchLogPipelineHealth")
        .Produces<ElasticsearchLogPipelineHealthResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityElasticsearchPermissions.Read));
    }
}
