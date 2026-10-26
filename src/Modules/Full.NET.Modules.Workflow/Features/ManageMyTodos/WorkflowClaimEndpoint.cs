using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

/// <summary>普通领取、管理员分配与本人释放使用独立精确权限；请求不能指定可信作用域。</summary>
internal static class WorkflowClaimEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/claim-pools", async (int? page, int? pageSize, WorkflowClaimPoolService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                ? mapper.Map(await service.ListAsync(actor, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context)
                : Results.Unauthorized())
            .WithName("workflowListClaimPools").WithTags("WorkflowTodos").Produces<WorkflowClaimPoolPageResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ClaimPoolsRead));
        endpoints.MapPost("/api/v1/workflow/claim-pools/{poolId:guid}/claim", async (Guid poolId, ClaimWorkflowPoolRequest request,
            WorkflowClaimPoolService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                ? mapper.Map(await service.ClaimAsync(poolId, actor, request, token).ConfigureAwait(false), context)
                : Results.Unauthorized())
            .WithName("workflowClaimPool").WithTags("WorkflowTodos").Produces<WorkflowInstanceResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ClaimPoolsClaim));
        endpoints.MapPost("/api/v1/workflow/claim-pools/{poolId:guid}/assign", async (Guid poolId, AssignWorkflowPoolRequest request,
            WorkflowClaimPoolService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)
                ? mapper.Map(await service.AssignAsync(poolId, actor, request, token).ConfigureAwait(false), context)
                : Results.Unauthorized())
            .WithName("workflowAssignClaimPool").WithTags("WorkflowTodos").Produces<WorkflowInstanceResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.ClaimPoolsAssign));
        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/release", async (Guid todoId, ReleaseWorkflowTodoRequest request,
            WorkflowClaimReleaseService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor)) return Results.Unauthorized();
            if (request.SignatureFileId is not null && !(await context.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(context.User, FullNetPermissionPolicies.For(WorkflowPermissions.TodosSign)).ConfigureAwait(false)).Succeeded)
                return mapper.Map(Result<WorkflowInstanceResponse>.Failure(new Error(WorkflowErrorCodes.TodoActionForbidden,
                    "Signature permission is required.", ErrorType.Forbidden)), context);
            return mapper.Map(await service.ReleaseAsync(todoId, actor, request, token).ConfigureAwait(false), context);
        }).WithName("workflowReleaseTodo").WithTags("WorkflowTodos").Produces<WorkflowInstanceResponse>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRelease));
    }
}
