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
    /// <summary>映射工作流实例启动、读取、取消、改派和执行轨迹端点。</summary>
    /// <param name="endpoints">应用端点路由构建器。</param>
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

        endpoints.MapPost("/api/v1/workflow/instances/{instanceId:guid}/reassign", async (
            Guid instanceId,
            ReassignWorkflowInstanceRequest request,
            WorkflowInstanceRecoveryService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ReassignAsync(instanceId, actorUserId, request, token)
                    .ConfigureAwait(false),
                context);
        })
        .WithName("workflowReassignInstance")
        .WithTags("WorkflowInstances")
        .Produces<WorkflowInstanceResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.InstancesRecover));

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

    /// <summary>从已认证主体读取稳定用户标识。</summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="actorUserId">解析成功后的操作人标识。</param>
    /// <returns>主体包含有效用户标识时返回 <see langword="true"/>。</returns>
    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
