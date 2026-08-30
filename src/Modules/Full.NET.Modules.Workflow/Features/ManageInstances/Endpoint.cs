using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageInstances;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/workflow/instances", async (
            StartWorkflowInstanceRequest request,
            WorkflowInstanceManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            var result = await service.StartAsync(actorUserId, request, token).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/v1/workflow/instances/{result.Value!.Id:D}", result.Value)
                : mapper.Map(result, context);
        })
        .WithName("workflowStartInstance")
        .WithTags("WorkflowInstances")
        .Produces<WorkflowInstanceResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesStart));

        endpoints.MapPost("/api/v1/workflow/instances/{instanceId:guid}/cancel", async (
            Guid instanceId,
            CancelWorkflowInstanceRequest request,
            WorkflowInstanceManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.CancelAsync(instanceId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowCancelInstance")
        .WithTags("WorkflowInstances")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesCancel));

        endpoints.MapGet("/api/v1/workflow/instances/{instanceId:guid}", async (
            Guid instanceId,
            WorkflowInstanceManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.GetAsync(instanceId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowGetInstance")
        .WithTags("WorkflowInstances")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRead));

        endpoints.MapGet("/api/v1/workflow/instances/{instanceId:guid}/execution-logs", async (
            Guid instanceId,
            WorkflowInstanceManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListExecutionLogsAsync(instanceId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowListInstanceExecutionLogs")
        .WithTags("WorkflowInstances")
        .Produces<IReadOnlyList<WorkflowExecutionLogResponse>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRead));
    }

    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
