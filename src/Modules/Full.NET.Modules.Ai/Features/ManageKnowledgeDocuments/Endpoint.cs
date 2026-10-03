using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeDocuments;

/// <summary>每项文档操作独立授权；所有者和租户只来自受信会话。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents").WithTags("AiKnowledgeDocuments");
        Common(group.MapGet("/", async (Guid knowledgeBaseId, int? page, int? pageSize, AiKnowledgeDocumentService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.ListAsync(knowledgeBaseId, actor, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiListKnowledgeDocuments", AiKnowledgeDocumentPermissions.Read)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePermissions.Read))
            .Produces<PagedResult<AiKnowledgeDocumentResponse>>(StatusCodes.Status200OK);
        Common(group.MapGet("/{documentId:guid}", async (Guid knowledgeBaseId, Guid documentId, AiKnowledgeDocumentService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.GetAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeDocument", AiKnowledgeDocumentPermissions.Read)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePermissions.Read))
            .Produces<AiKnowledgeDocumentResponse>(StatusCodes.Status200OK);
        Common(group.MapPost("/", async (Guid knowledgeBaseId, CreateAiKnowledgeDocumentRequest request, AiKnowledgeDocumentService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            var result = await service.CreateAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/ai/knowledge-bases/{knowledgeBaseId:D}/documents/{result.Value!.Id:D}", result.Value) : mapper.Map(result, context);
        }), "aiCreateKnowledgeDocument", AiKnowledgeDocumentPermissions.Create)
            .Produces<AiKnowledgeDocumentResponse>(StatusCodes.Status201Created);
        Common(group.MapPut("/{documentId:guid}", async (Guid knowledgeBaseId, Guid documentId, UpdateAiKnowledgeDocumentRequest request,
            AiKnowledgeDocumentService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.UpdateAsync(knowledgeBaseId, documentId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiUpdateKnowledgeDocument", AiKnowledgeDocumentPermissions.Update)
            .Produces<AiKnowledgeDocumentResponse>(StatusCodes.Status200OK);
        Common(group.MapDelete("/{documentId:guid}", async (Guid knowledgeBaseId, Guid documentId, [FromBody] DeleteAiKnowledgeDocumentRequest request,
            AiKnowledgeDocumentService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            var result = await service.DeleteAsync(knowledgeBaseId, documentId, actor, request.Version, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.NoContent() : mapper.Map(result, context);
        }), "aiDeleteKnowledgeDocument", AiKnowledgeDocumentPermissions.Delete)
            .Produces(StatusCodes.Status204NoContent);
        Common(group.MapGet("/{documentId:guid}/members", async (Guid knowledgeBaseId, Guid documentId, AiKnowledgeDocumentService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.GetMembersAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeDocumentMembers", AiKnowledgeDocumentPermissions.MembersRead)
            .Produces<AiKnowledgeDocumentMembersResponse>(StatusCodes.Status200OK);
        Common(group.MapPut("/{documentId:guid}/members", async (Guid knowledgeBaseId, Guid documentId, SetAiKnowledgeDocumentMembersRequest request,
            AiKnowledgeDocumentService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.SetMembersAsync(knowledgeBaseId, documentId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiSetKnowledgeDocumentMembers", AiKnowledgeDocumentPermissions.MembersUpdate)
            .Produces<AiKnowledgeDocumentMembersResponse>(StatusCodes.Status200OK);
    }

    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) =>
        endpoint.WithName(name).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(FullNetPermissionPolicies.For(permission));
    private static bool TryActor(HttpContext context, out Guid actor) => Guid.TryParse(context.User.FindFirst("sub")?.Value, out actor) && actor != Guid.Empty;
}
