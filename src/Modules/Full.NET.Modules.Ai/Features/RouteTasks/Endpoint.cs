using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.RouteTasks;

/// <summary>同一封闭请求协议的两个精确授权入口；每次仅选择一个执行边界，不并行或自动回退。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/tasks").WithTags("AiTasks");
        group.MapPost("/knowledge-ask", ExecuteKnowledgeAsync)
            .WithName("aiRouteKnowledgeTask")
            .Produces<AskAiKnowledgeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiChatPermissions.Send))
            .AddEndpointFilter(new AiKnowledgeReadPermissionFilter(AiKnowledgeAskService.RequiredPermissions));
        group.MapPost("/authorized-tool-task", ExecuteToolAsync)
            .WithName("aiRouteAuthorizedToolTask")
            .Produces<CreateAiAgentRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiAgentRunPermissions.Create));
    }

    /// <summary>问答使用既有会话槽位、来源复核和总预算；不得把知识内容转为工具指令。</summary>
    internal static async Task<IResult> ExecuteKnowledgeAsync(RouteAiTaskRequest request, AiKnowledgeAskService service,
        IApiResultMapper mapper, HttpContext httpContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AiTaskRoutePolicy.Resolve(request) != AiTaskIntent.KnowledgeAsk) return Invalid();
        if (!Guid.TryParse(httpContext.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty)
            return Results.Unauthorized();
        return mapper.Map(await service.AskAsync(request.SessionId!.Value, actor, request.Knowledge!, cancellationToken)
            .ConfigureAwait(false), httpContext);
    }

    /// <summary>只派发固定重命名工作流；创建不代表写入已获授权，Worker 仍逐步检查工具权限和人工审批。</summary>
    internal static async Task<IResult> ExecuteToolAsync(RouteAiTaskRequest request, AiAgentRunManagementService service,
        ICurrentTenant tenant, ICurrentSessionAuthorization authorization, IApiResultMapper mapper,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AiTaskRoutePolicy.Resolve(request) != AiTaskIntent.AuthorizedToolTask) return Invalid();
        if (!AgentRunHttpBinding.TryCreate(httpContext, tenant.Id, out var binding)) return Results.Unauthorized();
        var actor = await authorization.AuthorizeAsync(AiAgentRunPermissions.Create, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // 等待权威授权期间可能切租户或撤销会话；不能把新主体资格用于旧冻结绑定。
        if (actor is null || actor.UserId != binding.UserId || actor.SessionId != binding.SessionId
            || actor.TenantId != binding.TenantId || tenant.Id != binding.TenantId
            || (binding.TenantId.HasValue ? !tenant.IsAvailable || tenant.IsHost : !tenant.IsHost))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "authorization.permission_denied" });
        var result = await service.CreateRoutedAsync(request.ToolTask!, binding, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Accepted($"/api/v1/ai/agent/runs/{result.Value!.RunId}", result.Value)
            : mapper.Map(result, httpContext);
    }

    private static IResult Invalid() => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
        extensions: new Dictionary<string, object?> { ["code"] = "ai.routing.invalid" });
}
