using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;

/// <summary>精确版本入队只接受路径标识与认证原会话，不允许请求覆盖租户、所有者或模型。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/index",
            async (Guid knowledgeBaseId, Guid documentId, Guid versionId, AiKnowledgeIndexStatusService service,
                IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            {
                if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var actor) || actor == Guid.Empty)
                    return mapper.Map(AiKnowledgeImportStore.Failure<bool>("authorization.permission_denied", "An authenticated reader is required.", ErrorType.Forbidden), context);
                if (context.Request.Query.Count != 0 || (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
                    ?? (context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count != 0)))
                    return mapper.Map(AiKnowledgeImportStore.Failure<bool>(AiKnowledgeErrorCodes.InputInvalid, "Index status does not accept query fields or a request body.", ErrorType.Validation), context);
                return mapper.Map(await service.ReadAsync(knowledgeBaseId, documentId, versionId, actor, token).ConfigureAwait(false), context);
            }).WithName("aiGetKnowledgeIndexStatus").WithTags("AiKnowledgeIndexing")
            .RequireAuthorization(AiKnowledgeIndexStatusService.Permissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeIndexStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        endpoints.MapPost("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents/{documentId:guid}/versions/{versionId:guid}/index",
            async (Guid knowledgeBaseId, Guid documentId, Guid versionId, AiKnowledgeIndexEnqueueService service, ICurrentTenant tenant,
                IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            {
                if (!AiKnowledgeIndexHttpBinding.TryCreate(context, tenant.Id, out var original))
                    return mapper.Map(AiKnowledgeImportStore.Failure<bool>("authorization.permission_denied", "An interactive original session is required.", ErrorType.Forbidden), context);
                if (context.Request.Query.Count != 0 || (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
                    ?? (context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count != 0)))
                    return mapper.Map(AiKnowledgeImportStore.Failure<bool>(AiKnowledgeErrorCodes.InputInvalid, "Index admission does not accept query fields or a request body.", ErrorType.Validation), context);
                var result = await service.EnqueueAsync(knowledgeBaseId, documentId, versionId, original, token).ConfigureAwait(false);
                return result.IsSuccess ? Results.Accepted(value: result.Value) : mapper.Map(result, context);
            }).WithName("aiEnqueueKnowledgeIndex").WithTags("AiKnowledgeIndexing")
            .RequireAuthorization(AiKnowledgeIndexEnqueueService.Permissions.Select(FullNetPermissionPolicies.For).ToArray())
            .Produces<AiKnowledgeIndexEnqueueResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
    }
}
