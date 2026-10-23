using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;

/// <summary>文件上传、版本元数据、来源下载与恢复分别授权，不开放任意 FileId 导入。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/documents/{documentId:guid}").WithTags("AiKnowledgeVersions");
        Common(group.MapPost("/versions", async (Guid knowledgeBaseId, Guid documentId, int version, Guid requestKey, string fileName, string? parsingPolicy,
            AiKnowledgeImportCoordinator service, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            if (context.Request.Query.Keys.Any(key => key is not ("version" or "requestKey" or "fileName" or "parsingPolicy")))
                return mapper.Map(AiKnowledgeImportStore.Failure<bool>("ai.knowledge.upload_invalid", "Unknown upload fields are forbidden.", ErrorType.Validation), context);
            var type = context.Request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
            var result = await service.UploadAsync(knowledgeBaseId, documentId, actor, version, requestKey, fileName,
                type, context.Request.Body, context.Request.ContentLength, token, parsingPolicy ?? "default").ConfigureAwait(false);
            return result.IsSuccess ? Results.Accepted($"/api/v1/ai/knowledge-bases/{knowledgeBaseId:D}/documents/{documentId:D}/imports/{result.Value!.Id:D}", result.Value)
                : mapper.Map(result, context);
        }), "aiUploadKnowledgeVersion", AiKnowledgeDocumentPermissions.Upload)
            // 正文由协调器有界读取；OpenAPI 必须声明原始流，不能让客户端误用 JSON 或 multipart。
            .Accepts<Stream>(Domain.AiKnowledgeFilePolicy.ContentTypes[0], Domain.AiKnowledgeFilePolicy.ContentTypes.Skip(1).ToArray())
            .Produces<AiKnowledgeImportResponse>(StatusCodes.Status202Accepted);
        Read(Common(group.MapGet("/versions", async (Guid knowledgeBaseId, Guid documentId, int? page, int? pageSize,
            AiKnowledgeVersionReader reader, IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await reader.ListAsync(knowledgeBaseId, documentId, actor, page ?? 1, pageSize ?? 20, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiListKnowledgeVersions", AiKnowledgeDocumentPermissions.VersionsRead), AiKnowledgeDocumentPermissions.VersionsRead).Produces<PagedResult<AiKnowledgeVersionResponse>>(StatusCodes.Status200OK);
        Read(Common(group.MapGet("/versions/{versionId:guid}/source", async (Guid knowledgeBaseId, Guid documentId, Guid versionId,
            AiKnowledgeVersionReader reader, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            if (!TryActor(context, out var actor)) return Results.Unauthorized();
            var opened = await reader.OpenAsync(knowledgeBaseId, documentId, versionId, actor, token).ConfigureAwait(false);
            if (!opened.IsSuccess) return mapper.Map(opened, context);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            // 下载始终为附件，不把上传内容作为可执行页面或嵌入式 PDF 呈现。
            return Results.File(opened.Value!.Content, "application/octet-stream", opened.Value.FileName, enableRangeProcessing: false);
        }), "aiDownloadKnowledgeSource", AiKnowledgeDocumentPermissions.SourceRead), AiKnowledgeDocumentPermissions.SourceRead).Produces<Stream>(StatusCodes.Status200OK, contentType: "application/octet-stream");
        Common(group.MapGet("/imports/{operationId:guid}", async (Guid knowledgeBaseId, Guid documentId, Guid operationId,
            AiKnowledgeImportCoordinator service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.GetAsync(knowledgeBaseId, documentId, operationId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiGetKnowledgeImport", AiKnowledgeDocumentPermissions.ImportsRead).Produces<AiKnowledgeImportResponse>(StatusCodes.Status200OK);
        Common(group.MapPost("/imports/{operationId:guid}/retry", async (Guid knowledgeBaseId, Guid documentId, Guid operationId,
            AiKnowledgeImportCoordinator service, IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.RetryAsync(knowledgeBaseId, documentId, operationId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiRetryKnowledgeImport", AiKnowledgeDocumentPermissions.ImportsRetry).Produces<AiKnowledgeImportResponse>(StatusCodes.Status200OK);
        Common(group.MapPost("/imports/cleanup", async (Guid knowledgeBaseId, Guid documentId, AiKnowledgeImportCoordinator service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => TryActor(context, out var actor)
                ? mapper.Map(await service.CleanupDocumentAsync(knowledgeBaseId, documentId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()),
            "aiCleanupKnowledgeImports", AiKnowledgeDocumentPermissions.ImportsRetry).Produces<int>(StatusCodes.Status200OK);
    }

    private static RouteHandlerBuilder Common(RouteHandlerBuilder route, string name, string permission) => route.WithName(name)
        .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422)
        .RequireAuthorization(FullNetPermissionPolicies.For(permission));
    private static RouteHandlerBuilder Read(RouteHandlerBuilder route, string permission) => route
        .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePermissions.Read))
        .RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeDocumentPermissions.Read))
        .AddEndpointFilter(new AiKnowledgeReadPermissionFilter([AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, permission]));
    private static bool TryActor(HttpContext context, out Guid actor) => Guid.TryParse(context.User.FindFirst("sub")?.Value, out actor) && actor != Guid.Empty;
}
