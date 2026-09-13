using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>持久 Agent 运行创建、读取与取消端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/runs")
            .WithTags("AiAgentRuns");

        group.MapPost("/", async (
            CreateAiAgentRunRequest request,
            AiAgentRunManagementService service,
            ICurrentTenant tenant,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!AgentRunHttpBinding.TryCreate(httpContext, tenant.Id, out var binding))
            {
                return Results.Unauthorized();
            }

            var result = await service.CreateAsync(request, binding, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Accepted($"/api/v1/ai/agent/runs/{result.Value!.RunId}", result.Value);
        })
        .WithName("aiCreateAgentRun")
        .Produces<CreateAiAgentRunResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Create));

        group.MapGet("/{runId:guid}", async (
            Guid runId,
            AiAgentRunQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await queries.GetOwnedAsync(runId, userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetAgentRun")
        .Produces<AiAgentRunResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Read));

        group.MapPost("/{runId:guid}/cancel", async (
            Guid runId,
            AiAgentRunManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await service.CancelAsync(runId, userId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiCancelAgentRun")
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Cancel));

        group.MapPost("/{runId:guid}/resume", async (
            Guid runId,
            AiAgentRunManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            ICurrentTenant tenant,
            CancellationToken cancellationToken) =>
        {
            if (!AgentRunHttpBinding.TryCreate(httpContext, tenant.Id, out var binding))
            {
                return Results.Unauthorized();
            }

            var result = await service.ResumeAsync(runId, binding, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiResumeAgentRun")
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Resume));
    }

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
