using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageRoutes;

/// <summary>读取与恢复分别授权，操作者只来自已验证会话；不能由请求选择租户或代次。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/instances/{instanceId:guid}/routes/{routeId:guid}", async (Guid instanceId, Guid routeId,
            WorkflowRouteManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
                mapper.Map(await service.GetAsync(instanceId, routeId, token).ConfigureAwait(false), context))
            .WithName("workflowGetRouteDecision").WithTags("WorkflowInstances").Produces<WorkflowRouteDecisionResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RoutesRead));
        endpoints.MapPost("/api/v1/workflow/instances/{instanceId:guid}/routes/{routeId:guid}/reconcile", async (Guid instanceId, Guid routeId,
            ReconcileWorkflowRouteRequest request, WorkflowRouteManagementService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
                Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                    ? mapper.Map(await service.ReconcileAsync(instanceId, routeId, actor, request, token).ConfigureAwait(false), context)
                    : Results.Unauthorized())
            .WithName("workflowReconcileRouteDecision").WithTags("WorkflowInstances").Produces<WorkflowRouteResolutionResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RoutesReconcile));
    }
}
