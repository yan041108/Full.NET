using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;

/// <summary>收费增量授权需要交互原会话和精确操作权限，请求不得指定主体、模型或可信范围。</summary>
internal static class AiKnowledgeIndexGrantEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/index-grants";
        endpoints.MapGet(path, async (Guid knowledgeBaseId, Guid? afterId, AiKnowledgeIndexGrantService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            AiKnowledgeIndexHttpBinding.TryCreate(http, tenant.Id, out var original)
                ? mapper.Map(await service.ListAsync(knowledgeBaseId, afterId, original, token).ConfigureAwait(false), http) : mapper.Map(Denied(), http))
            .WithTags("AiKnowledgeIndexing").WithName("aiListKnowledgeIndexGrants").Produces<AiKnowledgeIndexGrantResponse[]>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeIndexGrantPermissions.Read));
        endpoints.MapPost(path, async (Guid knowledgeBaseId, CreateAiKnowledgeIndexGrantRequest request, AiKnowledgeIndexGrantService service,
            ICurrentTenant tenant, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            AiKnowledgeIndexHttpBinding.TryCreate(http, tenant.Id, out var original)
                ? mapper.Map(await service.CreateAsync(knowledgeBaseId, original, request, token).ConfigureAwait(false), http) : mapper.Map(Denied(), http))
            .WithTags("AiKnowledgeIndexing").WithName("aiCreateKnowledgeIndexGrant").Produces<AiKnowledgeIndexGrantResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeIndexGrantPermissions.Create));
        endpoints.MapPost(path + "/{grantId:guid}/revoke", async (Guid knowledgeBaseId, Guid grantId, RevokeAiKnowledgeIndexGrantRequest request,
            AiKnowledgeIndexGrantService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            AiKnowledgeIndexHttpBinding.TryCreate(http, tenant.Id, out var original)
                ? mapper.Map(await service.RevokeAsync(knowledgeBaseId, grantId, original, request, token).ConfigureAwait(false), http) : mapper.Map(Denied(), http))
            .WithTags("AiKnowledgeIndexing").WithName("aiRevokeKnowledgeIndexGrant").Produces<AiKnowledgeIndexGrantResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeIndexGrantPermissions.Revoke));
    }
    private static Result<bool> Denied() => Result<bool>.Failure(new("authorization.permission_denied", "An interactive original session is required.", ErrorType.Forbidden));
}
