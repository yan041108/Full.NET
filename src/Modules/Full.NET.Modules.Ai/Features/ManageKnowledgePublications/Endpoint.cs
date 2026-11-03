using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgePublications;

/// <summary>人工请求、独立决定和正式发布分开授权，响应不暴露内部载荷。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents/{documentId:guid}/publications").WithTags("AiKnowledgePublications");
        Common(group.MapGet("/head", async (Guid knowledgeBaseId, Guid documentId, AiKnowledgePublicationService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ReadHeadAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized();
        }), "aiGetKnowledgePublicationHead", AiKnowledgePublicationPermissions.Read).Produces<AiKnowledgePublicationHeadResponse>();
        Common(group.MapGet("/{publicationId:guid}", async (Guid knowledgeBaseId, Guid documentId, Guid publicationId, AiKnowledgePublicationService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ReadAsync(knowledgeBaseId, documentId, publicationId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized();
        }), "aiGetKnowledgePublication", AiKnowledgePublicationPermissions.Read).Produces<AiKnowledgePublicationResponse>();
        Common(group.MapPost("/", async (Guid knowledgeBaseId, Guid documentId, RequestAiKnowledgePublication request, AiKnowledgePublicationService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Results.Unauthorized();
            var result = await service.RequestAsync(knowledgeBaseId, documentId, request, actor, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/publications/{result.Value!.Id}", result.Value) : mapper.Map(result, context);
        }), "aiRequestKnowledgePublication", AiKnowledgePublicationPermissions.Request).Produces<AiKnowledgePublicationResponse>(201);
        Common(group.MapPost("/{publicationId:guid}/review", async (Guid knowledgeBaseId, Guid documentId, Guid publicationId,
            ReviewAiKnowledgePublication request, AiKnowledgePublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ReviewAsync(knowledgeBaseId, documentId, publicationId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiReviewKnowledgePublication", AiKnowledgePublicationPermissions.Review).Produces<AiKnowledgePublicationResponse>();
        Common(group.MapPost("/{publicationId:guid}/publish", async (Guid knowledgeBaseId, Guid documentId, Guid publicationId,
            PublishAiKnowledgePublication request, AiKnowledgePublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.PublishAsync(knowledgeBaseId, documentId, publicationId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiPublishKnowledgePublication", AiKnowledgePublicationPermissions.Publish).Produces<AiKnowledgePublicationResponse>();
        Common(group.MapPost("/withdraw", async (Guid knowledgeBaseId, Guid documentId, WithdrawAiKnowledgePublication request,
            AiKnowledgePublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.WithdrawAsync(knowledgeBaseId, documentId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiWithdrawKnowledgePublication", AiKnowledgePublicationPermissions.Withdraw).Produces<AiKnowledgePublicationHeadResponse>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) => endpoint.WithName(name)
        .RequireAuthorization(FullNetPermissionPolicies.For(permission), FullNetPermissionPolicies.For(AiKnowledgePublicationPermissions.Read))
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
}
