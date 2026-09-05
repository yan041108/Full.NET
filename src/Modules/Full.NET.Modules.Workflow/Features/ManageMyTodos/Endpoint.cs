using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageMyTodos;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/todos/mine", async (
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(await service.ListMineAsync(actorUserId, token).ConfigureAwait(false), context);
        })
        .WithName("workflowListMyTodos")
        .WithTags("WorkflowTodos")
        .Produces<IReadOnlyList<WorkflowTodoResponse>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(await service.GetAsync(todoId, actorUserId, token).ConfigureAwait(false), context);
        })
        .WithName("workflowGetTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoDetailResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/return-targets", async (
            Guid todoId,
            int? page,
            int? pageSize,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListReturnTargetsAsync(
                    todoId, actorUserId, page ?? 1, pageSize ?? 100, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowListTodoReturnTargets")
        .WithTags("WorkflowTodos")
        .Produces<IReadOnlyList<WorkflowTodoReturnTargetResponse>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReturn));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/countersign-chain", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetCountersignChainAsync(todoId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowGetTodoCountersignChain")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoCountersignChainResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/countersign", async (
            Guid todoId,
            CountersignWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.CountersignAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowCountersignTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/countersign/cancel", async (
            Guid todoId,
            CancelWorkflowTodoCountersignRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.CancelCountersignAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowCancelTodoCountersign")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosCountersign));

        endpoints.MapGet("/api/v1/workflow/todos/{todoId:guid}/runtime", async (
            Guid todoId,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetRuntimeAsync(todoId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowGetTodoRuntime")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowTodoRuntimeResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosRead));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/approve", async (
            Guid todoId,
            ActWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ApproveAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowApproveTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosApprove));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/reject", async (
            Guid todoId,
            ActWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.RejectAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowRejectTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReject));

        endpoints.MapPost("/api/v1/workflow/todos/{todoId:guid}/return", async (
            Guid todoId,
            ReturnWorkflowTodoRequest request,
            WorkflowTodoManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ReturnAsync(todoId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowReturnTodo")
        .WithTags("WorkflowTodos")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.TodosReturn));
    }

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
