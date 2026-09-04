using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Workflow.Features.ManageMyCc;

/// <summary>映射当前用户工作流抄送列表和已读动作。</summary>
internal static class Endpoint
{
    /// <summary>映射“我的抄送”接口。</summary>
    /// <param name="endpoints">Endpoint 路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workflow/cc/mine", async (
            WorkflowCcManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.ListMineAsync(actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowListMyCc")
        .WithTags("WorkflowCc")
        .Produces<IReadOnlyList<WorkflowCcResponse>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CcRead));

        endpoints.MapPost("/api/v1/workflow/cc/{ccId:guid}/read", async (
            Guid ccId,
            WorkflowCcManagementService service,
            IApiResultMapper mapper,
            HttpContext context,
            CancellationToken token) =>
        {
            if (!TryGetActor(context, out var actorUserId))
            {
                return Results.Unauthorized();
            }

            return mapper.Map(
                await service.MarkReadAsync(ccId, actorUserId, token).ConfigureAwait(false),
                context);
        })
        .WithName("workflowMarkCcRead")
        .WithTags("WorkflowCc")
        .Produces<WorkflowCcReadResponse>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(WorkflowPermissions.CcMarkRead));
    }

    /// <summary>从可信身份声明读取当前用户。</summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="actorUserId">解析后的当前用户标识。</param>
    /// <returns>声明包含有效用户标识时返回 <see langword="true"/>。</returns>
    private static bool TryGetActor(HttpContext context, out Guid actorUserId) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out actorUserId);
}
