using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeBases;

/// <summary>目录编辑与文档处理批准使用不同权限；所有者只能取自认证主体。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases").WithTags("AiKnowledgeBases");
        Common(group.MapGet("/{knowledgeBaseId:guid}/members", async (Guid knowledgeBaseId,
            ManageKnowledgeMembers.AiKnowledgeMemberService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.GetAsync(knowledgeBaseId, owner, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeMembers", AiKnowledgePermissions.MembersRead)
            .Produces<AiKnowledgeMembersResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        Common(group.MapPut("/{knowledgeBaseId:guid}/members", async (Guid knowledgeBaseId, SetAiKnowledgeMembersRequest request,
            ManageKnowledgeMembers.AiKnowledgeMemberService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.SetAsync(knowledgeBaseId, owner, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiSetKnowledgeMembers", AiKnowledgePermissions.MembersUpdate)
            .Produces<AiKnowledgeMembersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        Common(group.MapGet("/", async (int? page, int? pageSize, AiKnowledgeBaseService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.ListAsync(owner, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiListKnowledgeBases", AiKnowledgePermissions.Read)
            .Produces<PagedResult<AiKnowledgeBaseResponse>>(StatusCodes.Status200OK);
        Common(group.MapGet("/{knowledgeBaseId:guid}", async (Guid knowledgeBaseId, AiKnowledgeBaseService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.GetAsync(knowledgeBaseId, owner, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeBase", AiKnowledgePermissions.Read)
            .Produces<AiKnowledgeBaseResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        Common(group.MapPost("/", async (CreateAiKnowledgeBaseRequest request, AiKnowledgeBaseService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryOwner(context, out var owner)) return Results.Unauthorized();
            var result = await service.CreateAsync(owner, request, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/ai/knowledge-bases/{result.Value!.Id:D}", result.Value) : mapper.Map(result, context);
        }), "aiCreateKnowledgeBase", AiKnowledgePermissions.Create)
            .Produces<AiKnowledgeBaseResponse>(StatusCodes.Status201Created);
        Common(group.MapPut("/{knowledgeBaseId:guid}", async (Guid knowledgeBaseId, UpdateAiKnowledgeBaseRequest request,
            AiKnowledgeBaseService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.UpdateAsync(knowledgeBaseId, owner, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiUpdateKnowledgeBase", AiKnowledgePermissions.Update)
            .Produces<AiKnowledgeBaseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        Common(group.MapPut("/{knowledgeBaseId:guid}/policy", async (Guid knowledgeBaseId, UpdateAiKnowledgePolicyRequest request,
            AiKnowledgeBaseService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            TryOwner(context, out var owner) ? mapper.Map(await service.UpdatePolicyAsync(knowledgeBaseId, owner, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiUpdateKnowledgePolicy", AiKnowledgePermissions.PolicyUpdate)
            .Produces<AiKnowledgeBaseResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) =>
        endpoint.WithName(name).ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(FullNetPermissionPolicies.For(permission));

    private static bool TryOwner(HttpContext context, out Guid id) =>
        Guid.TryParse(context.User.FindFirst("sub")?.Value, out id) && id != Guid.Empty;
}
