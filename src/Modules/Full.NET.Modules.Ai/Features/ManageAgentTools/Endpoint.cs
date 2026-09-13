using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>Agent Tool 静态目录与调用审计 HTTP 端点。</summary>
internal static class Endpoint
{
    /// <summary>注册 Agent Tool 只读路由。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var catalogGroup = endpoints.MapGroup("/api/v1/ai/agent-tools")
            .WithTags("AiAgentTools");

        catalogGroup.MapGet("/", async (
            AiAgentToolCatalogService catalog,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = await catalog.ListAsync(httpContext.RequestAborted).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListAgentTools")
        .Produces<IReadOnlyList<AiAgentToolCatalogItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentToolPermissions.CatalogRead));

        catalogGroup.MapGet("/{toolName}", async (
            string toolName,
            AiAgentToolCatalogService catalog,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var result = await catalog.GetByNameAsync(toolName, httpContext.RequestAborted).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetAgentTool")
        .Produces<AiAgentToolCatalogItem>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentToolPermissions.CatalogRead));

        endpoints.MapGet("/api/v1/ai/agent-tool-calls", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            string? toolName,
            string? statusKey,
            AiAgentToolCallQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    new AiAgentToolCallListQuery(
                        page ?? 1,
                        pageSize ?? 20,
                        tenantId,
                        toolName,
                        statusKey),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListAgentToolCalls")
        .WithTags("AiAgentTools")
        .Produces<PagedResult<AiAgentToolCallListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentToolPermissions.CallsRead));
    }
}
