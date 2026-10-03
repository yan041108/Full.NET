using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOrganizationCandidates;

/// <summary>候选枚举使用独立权限和权威交互会话，旧令牌声明不能维持已撤销的权限。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases").WithTags("AiKnowledgeOrganizationCandidates");
        Common(group.MapGet("/{knowledgeBaseId:guid}/organization-candidates", async (Guid knowledgeBaseId, int? page, int? pageSize,
            AiKnowledgeOrganizationCandidateService service, ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ListAsync(knowledgeBaseId, null, page, pageSize, AiKnowledgePermissions.OrganizationCandidatesRead, service, authorization, mapper, context, token).ConfigureAwait(false)),
            "aiListKnowledgeOrganizationCandidates", AiKnowledgePermissions.OrganizationCandidatesRead);
        Common(group.MapGet("/{knowledgeBaseId:guid}/documents/{documentId:guid}/organization-candidates", async (Guid knowledgeBaseId, Guid documentId, int? page, int? pageSize,
            AiKnowledgeOrganizationCandidateService service, ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            await ListAsync(knowledgeBaseId, documentId, page, pageSize, AiKnowledgeDocumentPermissions.OrganizationCandidatesRead, service, authorization, mapper, context, token).ConfigureAwait(false)),
            "aiListKnowledgeDocumentOrganizationCandidates", AiKnowledgeDocumentPermissions.OrganizationCandidatesRead);
    }

    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) => endpoint.WithName(name)
        .Produces<PagedResult<AiKnowledgeOrganizationCandidateResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(permission));
    private static async Task<IResult> ListAsync(Guid knowledgeBaseId, Guid? documentId, int? page, int? pageSize, string permission,
        AiKnowledgeOrganizationCandidateService service, ICurrentSessionAuthorization authorization, IApiResultMapper mapper, HttpContext context, CancellationToken token)
    {
        // 策略先保护匿名及无权限声明；此处再核对账号、会话、当前租户和权威权限，主体不可来自请求覆盖。
        var actor = await authorization.AuthorizeAsync(permission, token).ConfigureAwait(false);
        if (actor is null)
            return mapper.Map(Result<PagedResult<AiKnowledgeOrganizationCandidateResponse>>.Failure(new Error(
                "authorization.permission_denied", "The current session does not have the required permission.", ErrorType.Forbidden)), context);
        return mapper.Map(await service.ListAsync(knowledgeBaseId, documentId, actor.UserId, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context);
    }
}
