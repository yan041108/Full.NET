using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeRoles;

/// <summary>知识库和文档角色授予分别授权；主体取自每请求权威交互会话。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases").WithTags("AiKnowledgeRoleGrants");
        Common(group.MapGet("/{knowledgeBaseId:guid}/roles", async (Guid knowledgeBaseId, AiKnowledgeRoleGrantService service,
            ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ExecuteAsync(knowledgeBaseId, null, null, AiKnowledgePermissions.RolesRead, service, authorization, mapper, context, token).ConfigureAwait(false)), "aiGetKnowledgeRoles", AiKnowledgePermissions.RolesRead);
        Common(group.MapPut("/{knowledgeBaseId:guid}/roles", async (Guid knowledgeBaseId, SetAiKnowledgeRoleGrantsRequest request, AiKnowledgeRoleGrantService service,
            ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ExecuteAsync(knowledgeBaseId, null, request, AiKnowledgePermissions.RolesUpdate, service, authorization, mapper, context, token).ConfigureAwait(false)), "aiSetKnowledgeRoles", AiKnowledgePermissions.RolesUpdate);
        Common(group.MapGet("/{knowledgeBaseId:guid}/documents/{documentId:guid}/roles", async (Guid knowledgeBaseId, Guid documentId, AiKnowledgeRoleGrantService service,
            ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ExecuteAsync(knowledgeBaseId, documentId, null, AiKnowledgeDocumentPermissions.RolesRead, service, authorization, mapper, context, token).ConfigureAwait(false)), "aiGetKnowledgeDocumentRoles", AiKnowledgeDocumentPermissions.RolesRead);
        Common(group.MapPut("/{knowledgeBaseId:guid}/documents/{documentId:guid}/roles", async (Guid knowledgeBaseId, Guid documentId, SetAiKnowledgeRoleGrantsRequest request, AiKnowledgeRoleGrantService service,
            ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ExecuteAsync(knowledgeBaseId, documentId, request, AiKnowledgeDocumentPermissions.RolesUpdate, service, authorization, mapper, context, token).ConfigureAwait(false)), "aiSetKnowledgeDocumentRoles", AiKnowledgeDocumentPermissions.RolesUpdate);
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder route, string name, string permission) => route.WithName(name)
        .Produces<AiKnowledgeRoleGrantsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
        .ProducesProblem(404).ProducesProblem(409).ProducesProblem(422).RequireAuthorization(FullNetPermissionPolicies.For(permission));
    private static async Task<IResult> ExecuteAsync(Guid knowledgeBaseId, Guid? documentId, SetAiKnowledgeRoleGrantsRequest? request, string permission,
        AiKnowledgeRoleGrantService service, ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token)
    {
        var actor = await authorization.AuthorizeAsync(permission, token).ConfigureAwait(false);
        if (actor is null) return mapper.Map(Result<AiKnowledgeRoleGrantsResponse>.Failure(new Error("authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden)), context);
        return mapper.Map(request is null ? await service.GetAsync(knowledgeBaseId, documentId, actor.UserId, token).ConfigureAwait(false)
            : await service.SetAsync(knowledgeBaseId, documentId, actor.UserId, request, token).ConfigureAwait(false), context);
    }
}
