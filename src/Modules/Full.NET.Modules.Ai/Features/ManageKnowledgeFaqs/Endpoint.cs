using Full.NET.Hosting.Api;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>FAQ 草稿使用精确权限与标准错误；没有发布入口，敏感正文禁止缓存且不注册正文日志投影。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/faq-drafts").WithTags("AiKnowledgeFaqDrafts");
        Common(group.MapGet("/", async (Guid knowledgeBaseId, Guid? afterId, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.ListAsync(knowledgeBaseId, afterId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiListKnowledgeFaqDrafts", false).Produces<AiKnowledgeFaqDraftPage>();
        Common(group.MapGet("/{faqId:guid}", async (Guid knowledgeBaseId, Guid faqId, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.GetAsync(knowledgeBaseId, faqId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiGetKnowledgeFaqDraft", false).Produces<AiKnowledgeFaqDraftWorkspace>();
        Common(group.MapPost("/independent", async (Guid knowledgeBaseId, CreateAiKnowledgeFaqIndependentDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.IndependentAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCreateIndependentKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPost("/from-documents", async (Guid knowledgeBaseId, CreateAiKnowledgeFaqDocumentDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.DocumentsAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCreateDocumentKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPost("/from-sources", async (Guid knowledgeBaseId, CreateAiKnowledgeFaqDerivedDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.DerivedAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCreateDerivedKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPost("/feedback-preview", async (Guid knowledgeBaseId, PreviewAiKnowledgeFaqFeedback request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.PreviewFeedbackAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiPreviewKnowledgeFaqFeedback", true)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeFaqPermissions.Feedback), FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackReviewPermissions.Read)).Produces<AiKnowledgeFaqFeedbackPreview>();
        Common(group.MapPost("/from-feedback", async (Guid knowledgeBaseId, CreateAiKnowledgeFaqFeedbackDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.FeedbackAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCreateFeedbackKnowledgeFaqDraft", true)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeFaqPermissions.Feedback), FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackReviewPermissions.Read)).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPut("/{faqId:guid}", async (Guid knowledgeBaseId, Guid faqId, SaveAiKnowledgeFaqDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.SaveAsync(knowledgeBaseId, faqId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiSaveKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPost("/{faqId:guid}/restore", async (Guid knowledgeBaseId, Guid faqId, RestoreAiKnowledgeFaqDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.RestoreAsync(knowledgeBaseId, faqId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiRestoreKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapPost("/{faqId:guid}/copy", async (Guid knowledgeBaseId, Guid faqId, CopyAiKnowledgeFaqDraft request, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.CopyAsync(knowledgeBaseId, faqId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCopyKnowledgeFaqDraft", true).Produces<AiKnowledgeFaqDraftMutation>();
        Common(group.MapGet("/{faqId:guid}/revisions", async (Guid knowledgeBaseId, Guid faqId, Guid? afterId, AiKnowledgeFaqDraftService service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            Actor(context) is { } actor ? mapper.Map(await service.HistoryAsync(knowledgeBaseId, faqId, afterId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiListKnowledgeFaqDraftRevisions", false).Produces<AiKnowledgeFaqDraftRevisionPage>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, bool write) => endpoint.WithName(name)
        .RequireAuthorization(new[] { AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, write ? AiKnowledgeFaqPermissions.Write : AiKnowledgeFaqPermissions.Read }.Select(FullNetPermissionPolicies.For).ToArray())
        .AddEndpointFilter(async (context, next) => { context.HttpContext.Response.Headers.CacheControl = "no-store"; context.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff"; if (!AiKnowledgeIndexHttpBinding.TryCreate(context.HttpContext, context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>().Id, out _)) return Results.Forbid(); return await next(context); })
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    private static Guid? Actor(HttpContext context) => Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) && actor != Guid.Empty ? actor : null;
}
