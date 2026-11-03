using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs;

/// <summary>有界传输使用独立权限、标准错误和静态请求类型，不登记正文日志。</summary>
internal static class TransferEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/faq-transfer").WithTags("AiKnowledgeFaqTransfer");
        Common(group.MapPost("/export", async (Guid knowledgeBaseId, ExportAiKnowledgeFaqs request, AiKnowledgeFaqDraftService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => Actor(context) is { } actor
            ? mapper.Map(await service.ExportAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiExportKnowledgeFaqs", AiKnowledgeFaqPermissions.Export).Produces<AiKnowledgeFaqTransferPackage>();
        Common(group.MapPost("/preview", async (Guid knowledgeBaseId, PreviewAiKnowledgeFaqImport request, AiKnowledgeFaqDraftService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => Actor(context) is { } actor
            ? mapper.Map(await service.PreviewImportAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiPreviewKnowledgeFaqImport", AiKnowledgeFaqPermissions.Import).Produces<AiKnowledgeFaqImportReport>();
        Common(group.MapPost("/import", async (Guid knowledgeBaseId, ImportAiKnowledgeFaqs request, AiKnowledgeFaqDraftService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => Actor(context) is { } actor
            ? mapper.Map(await service.ImportAsync(knowledgeBaseId, actor, request, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiImportKnowledgeFaqs", AiKnowledgeFaqPermissions.Import).Produces<AiKnowledgeFaqImportReport>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission) => endpoint.WithName(name)
        .RequireAuthorization(new[] { AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, permission,
            permission == AiKnowledgeFaqPermissions.Import ? AiKnowledgeFaqPermissions.Write : permission }.Distinct().Select(FullNetPermissionPolicies.For).ToArray())
        .AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context.HttpContext, context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>().Id, out _)) return Results.Forbid();
            return await next(context);
        }).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    private static Guid? Actor(HttpContext context) => Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) && actor != Guid.Empty ? actor : null;
}
