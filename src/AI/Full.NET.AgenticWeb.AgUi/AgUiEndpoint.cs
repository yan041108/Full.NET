using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>标准 AG-UI 事件 SSE 端点；仅重放持久事件，不执行 Agent。</summary>
public static class AgUiEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/agent/runs")
            .WithTags("AiAgentRuns");

        group.MapGet("/{runId:guid}/events/stream", async (
            Guid runId,
            long? afterSequence,
            AgUiStreamService streamService,
            ICurrentTenant tenant,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserId(httpContext, out var userId))
            {
                return Results.Unauthorized();
            }

            var scope = ResolveScope(tenant);
            var result = await streamService.StreamOwnedRunAsync(
                runId,
                scope,
                userId,
                afterSequence ?? 0,
                httpContext,
                cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Empty : mapper.Map(result, httpContext);
        })
        .WithName("aiStreamAgentRunEvents")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Read));
    }

    private static string ResolveScope(ICurrentTenant tenant) => tenant.Id is { } id
        ? id.ToString("N")
        : tenant.IsHost
            ? "host"
            : throw new InvalidOperationException("Tenant scope is required for agent runs.");

    private static bool TryResolveUserId(HttpContext httpContext, out Guid userId)
    {
        userId = default;
        var subject = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
