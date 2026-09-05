using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;

/// <summary>映射恢复任务查询、人工重试和对账端点；写操作必须携带修订号与幂等键。</summary>
internal static class Endpoint
{
    /// <summary>注册恢复任务管理路由。</summary>
    /// <param name="endpoints">应用端点路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workflow/recovery-tasks")
            .WithTags("WorkflowRecoveryTasks");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            WorkflowRecoveryTaskService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) => mapper.Map(
                await service.ListAsync(page ?? 1, pageSize ?? 20, token).ConfigureAwait(false),
                context))
        .WithName("workflowListRecoveryTasks")
        .Produces<PagedResult<WorkflowRecoveryTaskResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RecoveryTasksRead));

        group.MapGet("/{taskId:guid}", async (
            Guid taskId,
            WorkflowRecoveryTaskService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) => mapper.Map(
                await service.GetAsync(taskId, token).ConfigureAwait(false),
                context))
        .WithName("workflowGetRecoveryTask")
        .Produces<WorkflowRecoveryTaskResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RecoveryTasksRead));

        group.MapPost("/{taskId:guid}/retry", async (
            Guid taskId,
            RetryWorkflowRecoveryTaskRequest request,
            WorkflowRecoveryTaskService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.RetryAsync(taskId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowRetryRecoveryTask")
        .Produces<WorkflowRecoveryTaskResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RecoveryTasksRetry));

        group.MapPost("/{taskId:guid}/reconcile", async (
            Guid taskId,
            ReconcileWorkflowRecoveryTaskRequest request,
            WorkflowRecoveryTaskService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ReconcileAsync(taskId, actorUserId, request, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowReconcileRecoveryTask")
        .Produces<WorkflowRecoveryTaskResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.RecoveryTasksReconcile));
    }

    /// <summary>从已认证主体读取稳定用户标识。</summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="actorUserId">解析成功后的操作人标识。</param>
    /// <returns>主体包含有效用户标识时返回 <see langword="true"/>。</returns>
    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
