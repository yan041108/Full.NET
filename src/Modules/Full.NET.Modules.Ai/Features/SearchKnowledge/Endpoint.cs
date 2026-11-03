using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.SearchKnowledge;

/// <summary>搜索不创建生成操作；原交互会话、精确权限与当前资源读取分别核对。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/ai/knowledge-search/refine", async (RefineAiKnowledgeSearchRequest request, AiKnowledgeSearchRefinementService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor))
                return mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive session is required.", ErrorType.Forbidden)), context);
            if (context.Request.Query.Count != 0)
                return mapper.Map(Result<bool>.Failure(new Error(AiKnowledgeErrorCodes.SearchInputInvalid, "Unexpected query fields.", ErrorType.Validation)), context);
            return mapper.Map(await service.RefineAsync(request, actor.UserId, token).ConfigureAwait(false), context);
        }).WithName("aiRefineKnowledgeSearch").WithTags("AiKnowledgeSearch")
            .RequireAuthorization(AiKnowledgeSearchRefinementService.RequiredPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeSearchRefinementResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        endpoints.MapPost("/api/v1/ai/knowledge-search/relations", async (ReadAiKnowledgeSearchRelationsRequest request,
            AiKnowledgeSearchRelationsService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor))
                return mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive session is required.", ErrorType.Forbidden)), context);
            if (context.Request.Query.Count != 0)
                return mapper.Map(Result<bool>.Failure(new Error(AiKnowledgeErrorCodes.SearchInputInvalid, "Unexpected query fields.", ErrorType.Validation)), context);
            return mapper.Map(await service.ReadAsync(request, actor.UserId, token).ConfigureAwait(false), context);
        }).WithName("aiReadKnowledgeSearchRelations").WithTags("AiKnowledgeSearch")
            .RequireAuthorization(AiKnowledgeSearchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeSearchRelationsResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        endpoints.MapPost("/api/v1/ai/knowledge-search/compare", async (CompareAiKnowledgeSearchRequest request, AiKnowledgeSearchComparisonService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor))
                return mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive session is required.", ErrorType.Forbidden)), context);
            if (context.Request.Query.Count != 0)
                return mapper.Map(Result<bool>.Failure(new Error(AiKnowledgeErrorCodes.SearchInputInvalid, "Unexpected query fields.", ErrorType.Validation)), context);
            return mapper.Map(await service.CompareAsync(request, actor.UserId, token).ConfigureAwait(false), context);
        }).WithName("aiCompareKnowledgeSearch").WithTags("AiKnowledgeSearch")
            .RequireAuthorization(AiKnowledgeSearchComparisonService.RequiredPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeSearchComparisonResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        endpoints.MapPost("/api/v1/ai/knowledge-search", async (
        SearchAiKnowledgeRequest request, AiKnowledgeSearchService service, ICurrentTenant tenant, IApiResultMapper mapper,
        HttpContext context, CancellationToken token) =>
    {
        context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor))
            return mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive session is required.", ErrorType.Forbidden)), context);
        if (context.Request.Query.Count != 0)
            return mapper.Map(Result<bool>.Failure(new Error(AiKnowledgeErrorCodes.SearchInputInvalid, "Unexpected query fields.", ErrorType.Validation)), context);
        return mapper.Map(await service.SearchAsync(request, actor.UserId, token).ConfigureAwait(false), context);
    }).WithName("aiSearchKnowledge").WithTags("AiKnowledgeSearch")
        .RequireAuthorization(AiKnowledgeSearchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
        .Produces<AiKnowledgeSearchResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        endpoints.MapPost("/api/v1/ai/knowledge-search/context", async (ReadAiKnowledgeSearchContextRequest request,
            AiKnowledgeSearchContextService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var actor))
                return mapper.Map(Result<bool>.Failure(new Error("authorization.permission_denied", "An interactive session is required.", ErrorType.Forbidden)), context);
            if (context.Request.Query.Count != 0)
                return mapper.Map(Result<bool>.Failure(new Error(AiKnowledgeErrorCodes.SearchInputInvalid, "Unexpected query fields.", ErrorType.Validation)), context);
            return mapper.Map(await service.ReadAsync(request, actor.UserId, token).ConfigureAwait(false), context);
        }).WithName("aiReadKnowledgeSearchContext").WithTags("AiKnowledgeSearch")
            .RequireAuthorization(AiKnowledgeSearchService.ReadPermissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeSearchContextResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
    }
}
