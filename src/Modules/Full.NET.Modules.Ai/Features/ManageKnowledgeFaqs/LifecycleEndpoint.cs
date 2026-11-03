using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>静态 FAQ 修订生命周期；所有正文与治理响应禁止缓存，只接受交互原会话。</summary>
internal static class LifecycleEndpoint
{
    private const string Root = "/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/faq-drafts/{faqId:guid}";
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        Common(endpoints.MapPost(Root + "/index", async (Guid knowledgeBaseId, Guid faqId, IndexAiKnowledgeFaqRevision request,
            AiKnowledgeFaqIndexService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Results.Unauthorized();
            var result = await service.IndexAsync(knowledgeBaseId, faqId, request, actor, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Accepted(value: result.Value) : mapper.Map(result, context);
        }), "aiIndexKnowledgeFaqRevision", AiKnowledgeFaqPermissions.Index).Produces<AiKnowledgeFaqIndexAdmission>(202);
        Common(endpoints.MapGet(Root + "/lifecycle", async (Guid knowledgeBaseId, Guid faqId, AiKnowledgeFaqPublicationService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.LifecycleAsync(knowledgeBaseId, faqId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeFaqLifecycle", AiKnowledgeFaqPermissions.Read).Produces<AiKnowledgeFaqLifecycle>();
        Common(endpoints.MapPost(Root + "/publication-requests", async (Guid knowledgeBaseId, Guid faqId, RequestAiKnowledgeFaqPublication request,
            AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Results.Unauthorized();
            var result = await service.RequestAsync(knowledgeBaseId, faqId, request, actor, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Created($"/api/v1/ai/knowledge-bases/{knowledgeBaseId}/faq-drafts/{faqId}/publications/{result.Value!.Id}", result.Value) : mapper.Map(result, context);
        }), "aiRequestKnowledgeFaqPublication", AiKnowledgeFaqPermissions.Publish).Produces<AiKnowledgeFaqPublication>(201);
        Common(endpoints.MapGet(Root + "/publications/{publicationId:guid}", async (Guid knowledgeBaseId, Guid faqId, Guid publicationId,
            AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ReadAsync(knowledgeBaseId, faqId, publicationId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeFaqPublication", AiKnowledgeFaqPermissions.Read).Produces<AiKnowledgeFaqPublication>();
        Common(endpoints.MapPost(Root + "/publications/{publicationId:guid}/review", async (Guid knowledgeBaseId, Guid faqId, Guid publicationId,
            ReviewAiKnowledgePublication request, AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ReviewAsync(knowledgeBaseId, faqId, publicationId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiReviewKnowledgeFaqPublication", AiKnowledgeFaqPermissions.Review).Produces<AiKnowledgeFaqPublication>();
        Common(endpoints.MapPost(Root + "/publications/{publicationId:guid}/publish", async (Guid knowledgeBaseId, Guid faqId, Guid publicationId,
            PublishAiKnowledgePublication request, AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.PublishAsync(knowledgeBaseId, faqId, publicationId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiPublishKnowledgeFaqPublication", AiKnowledgeFaqPermissions.Publish).Produces<AiKnowledgeFaqPublication>();
        Common(endpoints.MapPost(Root + "/publications/withdraw", async (Guid knowledgeBaseId, Guid faqId, WithdrawAiKnowledgePublication request,
            AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.WithdrawAsync(knowledgeBaseId, faqId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiWithdrawKnowledgeFaqPublication", AiKnowledgeFaqPermissions.Publish).Produces<AiKnowledgePublicationHeadResponse>();
        Common(endpoints.MapGet("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/faqs/{faqId:guid}", async (Guid knowledgeBaseId, Guid faqId,
            AiKnowledgeFaqPublicationService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.PublishedAsync(knowledgeBaseId, faqId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetPublishedKnowledgeFaq", AiKnowledgeFaqPermissions.Read).Produces<AiKnowledgeFaqPublishedContent>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) => endpoint.WithName(name).WithTags("AiKnowledgeFaqs")
        .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePermissions.Read), FullNetPermissionPolicies.For(AiKnowledgeFaqPermissions.Read), FullNetPermissionPolicies.For(permission))
        .AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store"; context.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
            return await next(context).ConfigureAwait(false);
        }).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
}
