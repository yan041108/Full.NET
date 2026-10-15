using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>独立恢复权限只允许写入意图，不能通过读取权或恢复操作创建收费任务。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapPost(
        "/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/index-changes/recover",
        async (Guid knowledgeBaseId, AiKnowledgeIncrementalRecoveryRequest request, AiKnowledgeIncrementalRecoveryService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty) return Results.Unauthorized();
            if (context.Request.Query.Count != 0) return mapper.Map(Full.NET.Abstractions.Results.Result<bool>.Failure(new(
                AiKnowledgeErrorCodes.InputInvalid, "Recovery accepts only the declared request body.", Full.NET.Abstractions.Results.ErrorType.Validation)), context);
            return mapper.Map(await service.RecoverAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context);
        })
        .WithName("aiRecoverKnowledgeIndexChanges").WithTags("AiKnowledgeIndexing")
        .RequireAuthorization(AiKnowledgeIncrementalRecoveryService.Permissions.Select(FullNetPermissionPolicies.For).ToArray())
        .Produces<AiKnowledgeIncrementalRecoveryResponse>(StatusCodes.Status200OK)
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
}
