using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;

/// <summary>运营入口仅接受交互会话与闭合筛选，不接受客户端租户或正文。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet("/api/v1/ai/knowledge-operations",
        async (Guid? knowledgeBaseId, Guid? afterId, string? view, DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
            AiKnowledgeOperationsService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var original))
                return mapper.Map(Result<AiKnowledgeOperationsPage>.Failure(new("authorization.permission_denied", "An interactive original session is required.", ErrorType.Forbidden)), context);
            if ((context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? context.Request.ContentLength > 0)
                || context.Request.Query.Any(pair => pair.Value.Count != 1 || pair.Key is not ("knowledgeBaseId" or "afterId" or "view" or "fromUtc" or "toUtc")))
                return mapper.Map(Result<AiKnowledgeOperationsPage>.Failure(new(AiKnowledgeErrorCodes.InputInvalid, "The knowledge operations filters are invalid.", ErrorType.Validation)), context);
            return mapper.Map(await service.ReadAsync(original.UserId, knowledgeBaseId, afterId, view, fromUtc, toUtc, token).ConfigureAwait(false), context);
        }).WithName("aiListKnowledgeOperations").WithTags("AiKnowledgeOperations")
        .RequireAuthorization(AiKnowledgeOperationsService.Permissions.Select(FullNetPermissionPolicies.For).ToArray())
        .Produces<AiKnowledgeOperationsPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
}
