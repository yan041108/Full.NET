using Full.NET.Hosting.Api;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageOperations;

/// <summary>运营读取使用独立精确权限；任何恢复仍走原资源的授权、修订、原因和幂等入口。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflow/operations").WithTags("WorkflowOperations");
        group.MapGet("/steps", async (int? page, int? pageSize, Guid? definitionVersionId, long? minimumDwellSeconds,
            bool? onlyOverdue, WorkflowOperationsService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                ? mapper.Map(await service.StepsAsync(actor, page ?? 1, pageSize ?? 20, definitionVersionId, minimumDwellSeconds ?? 0, onlyOverdue ?? false, token).ConfigureAwait(false), context)
                : Results.Unauthorized())
            .WithName("workflowListOperationsSteps").Produces<WorkflowOperationsStepsResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.OperationsRead), FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRead));
        group.MapGet("/backlog", async (int? page, int? pageSize, Guid? definitionVersionId,
            WorkflowOperationsService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                ? mapper.Map(await service.BacklogAsync(actor, page ?? 1, pageSize ?? 20, definitionVersionId, token).ConfigureAwait(false), context)
                : Results.Unauthorized())
            .WithName("workflowListOperationsBacklog").Produces<WorkflowOperationsBacklogResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.OperationsRead), FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRead));
    }
}
