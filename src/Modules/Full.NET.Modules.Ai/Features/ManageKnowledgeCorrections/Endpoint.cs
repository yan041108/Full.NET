using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeCorrections;

/// <summary>来源准备与逐块维护读取只接受原交互会话；每次响应禁用正文缓存。</summary>
internal static class Endpoint
{
    private const string Root = "/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/corrections";
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Root + "/revisions", async (Guid knowledgeBaseId, Guid documentId, Guid versionId, AiKnowledgeCorrectionEditRequest request,
            AiKnowledgeCorrectionEditService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (context.Request.Query.Count != 0) return Invalid(mapper, context);
            return mapper.Map(await service.EditAsync(knowledgeBaseId, documentId, versionId, request, actor, token).ConfigureAwait(false), context);
        }).WithName("aiEditKnowledgeCorrection").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionEditService.EditPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionRevisionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        endpoints.MapPost(Root + "/restore", async (Guid knowledgeBaseId, Guid documentId, Guid versionId, AiKnowledgeCorrectionRestoreRequest request,
            AiKnowledgeCorrectionEditService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (context.Request.Query.Count != 0) return Invalid(mapper, context);
            return mapper.Map(await service.RestoreAsync(knowledgeBaseId, documentId, versionId, request, actor, token).ConfigureAwait(false), context);
        }).WithName("aiRestoreKnowledgeCorrection").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionEditService.RestorePermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionRevisionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        endpoints.MapGet(Root + "/revisions", async (Guid knowledgeBaseId, Guid documentId, Guid versionId, int? beforeHeadVersion,
            AiKnowledgeCorrectionEditService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (HasBody(context) || context.Request.Query.Any(p => p.Key != "beforeHeadVersion" || p.Value.Count != 1)) return Invalid(mapper, context);
            return mapper.Map(await service.HistoryAsync(knowledgeBaseId, documentId, versionId, beforeHeadVersion, actor, token).ConfigureAwait(false), context);
        }).WithName("aiListKnowledgeCorrectionRevisions").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionWorkbenchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionRevisionPageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        endpoints.MapPost(Root + "/prepare", async (Guid knowledgeBaseId, Guid documentId, Guid versionId,
            AiKnowledgeCorrectionPrepareRequest request, AiKnowledgeCorrectionWorkbenchService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (context.Request.Query.Count != 0) return Invalid(mapper, context);
            var result = await service.PrepareAsync(knowledgeBaseId, documentId, versionId, request, actor, token).ConfigureAwait(false);
            return result.IsSuccess ? Results.Accepted(RootLocation(knowledgeBaseId, documentId, versionId) + "/preparations/" + result.Value!.Id, result.Value) : mapper.Map(result, context);
        }).WithName("aiPrepareKnowledgeCorrection").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionWorkbenchService.PreparePermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionPreparationResponse>(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        endpoints.MapGet(Root + "/preparations/{preparationId:guid}", async (Guid knowledgeBaseId, Guid documentId, Guid versionId,
            Guid preparationId, AiKnowledgeCorrectionWorkbenchService service, ICurrentTenant tenant, IApiResultMapper mapper,
            HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (preparationId == Guid.Empty || context.Request.Query.Count != 0 || HasBody(context)) return Invalid(mapper, context);
            return mapper.Map(await service.ReadPreparationAsync(knowledgeBaseId, documentId, versionId, preparationId, actor, token).ConfigureAwait(false), context);
        }).WithName("aiGetKnowledgeCorrectionPreparation").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionWorkbenchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionPreparationResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        endpoints.MapGet(Root + "/preparations/latest", async (Guid knowledgeBaseId, Guid documentId, Guid versionId,
            AiKnowledgeCorrectionWorkbenchService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (context.Request.Query.Count != 0 || HasBody(context)) return Invalid(mapper, context);
            return mapper.Map(await service.ReadPreparationAsync(knowledgeBaseId, documentId, versionId, Guid.Empty, actor, token).ConfigureAwait(false), context);
        }).WithName("aiGetLatestKnowledgeCorrectionPreparation").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionWorkbenchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionPreparationResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        endpoints.MapGet(Root + "/workspace", async (Guid knowledgeBaseId, Guid documentId, Guid versionId, Guid? sourceId, Guid? revisionId, int? block,
            AiKnowledgeCorrectionWorkbenchService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            NoCache(context);
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor)) return Denied(mapper, context);
            if (HasBody(context) || context.Request.Query.Any(p => p.Key is not ("sourceId" or "revisionId" or "block") || p.Value.Count != 1)) return Invalid(mapper, context);
            return mapper.Map(await service.ReadWorkspaceAsync(knowledgeBaseId, documentId, versionId, sourceId, block ?? 0, actor, token, revisionId).ConfigureAwait(false), context);
        }).WithName("aiGetKnowledgeCorrectionWorkspace").WithTags("AiKnowledgeCorrections")
            .RequireAuthorization(AiKnowledgeCorrectionWorkbenchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeCorrectionWorkspaceResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
    }
    private static string RootLocation(Guid knowledge, Guid document, Guid version) => $"/api/v1/ai/knowledge-bases/{knowledge}/documents/{document}/versions/{version}/corrections";
    private static bool HasBody(HttpContext c) => c.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? (c.Request.ContentLength > 0 || c.Request.Headers.TransferEncoding.Count != 0);
    private static void NoCache(HttpContext c) { c.Response.Headers.CacheControl = "no-store"; c.Response.Headers["X-Content-Type-Options"] = "nosniff"; }
    private static IResult Denied(IApiResultMapper mapper, HttpContext c) => mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive original session is required.", ErrorType.Forbidden)), c);
    private static IResult Invalid(IApiResultMapper mapper, HttpContext c) => mapper.Map(Result<bool>.Failure(new Error("ai.knowledge.input_invalid", "Unexpected correction request fields.", ErrorType.Validation)), c);
}
