using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgePublications;

/// <summary>目录入口独立授权，批量维护不接受租户、所有者和读取授权字段。</summary>
internal static class DirectoryEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}").WithTags("AiKnowledgeDirectory");
        Common(group.MapGet("/folders", async (Guid knowledgeBaseId, AiKnowledgeDirectoryService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.FoldersAsync(knowledgeBaseId, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiListKnowledgeFolders", false)
            .Produces<IReadOnlyList<AiKnowledgeFolderResponse>>();
        Common(group.MapPost("/folders", async (Guid knowledgeBaseId, SaveAiKnowledgeFolder request, AiKnowledgeDirectoryService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.SaveFolderAsync(knowledgeBaseId, null, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiCreateKnowledgeFolder", true)
            .Produces<AiKnowledgeFolderResponse>();
        Common(group.MapPut("/folders/{folderId:guid}", async (Guid knowledgeBaseId, Guid folderId, SaveAiKnowledgeFolder request, AiKnowledgeDirectoryService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.SaveFolderAsync(knowledgeBaseId, folderId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiUpdateKnowledgeFolder", true)
            .Produces<AiKnowledgeFolderResponse>();
        Common(group.MapDelete("/folders/{folderId:guid}", async (Guid knowledgeBaseId, Guid folderId, [FromBody] DeleteAiKnowledgeFolder request, AiKnowledgeDirectoryService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.DeleteFolderAsync(knowledgeBaseId, folderId, request.Version, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiDeleteKnowledgeFolder", true)
            .Produces<bool>();
        Common(group.MapGet("/directory", async (Guid knowledgeBaseId, Guid? folderId, string? tag, Guid? responsibleUserId, string? state, int? page, int? pageSize,
            AiKnowledgeDirectoryService service, ICurrentTenant tenant, IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.ListAsync(knowledgeBaseId, folderId, tag, responsibleUserId, state, page ?? 1, pageSize ?? 20, actor, token).ConfigureAwait(false), context) : Results.Unauthorized();
        }), "aiListKnowledgeDirectory", false).Produces<PagedResult<AiKnowledgeDirectoryResponse>>();
        Common(group.MapPost("/directory/batch", async (Guid knowledgeBaseId, SetAiKnowledgeDirectoryBatch request, AiKnowledgeDirectoryService service, ICurrentTenant tenant,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => AgentRunHttpBinding.TryCreate(context, tenant.Id, out var actor)
                ? mapper.Map(await service.BatchAsync(knowledgeBaseId, request, actor, token).ConfigureAwait(false), context) : Results.Unauthorized()), "aiSetKnowledgeDirectoryBatch", true)
            .Produces<IReadOnlyList<AiKnowledgeDirectoryItemResult>>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, bool write)
    {
        endpoint.WithName(name).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePermissions.Read), FullNetPermissionPolicies.For(AiKnowledgeDocumentPermissions.Read))
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        return write ? endpoint.RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgePublicationPermissions.DirectoryManage)) : endpoint;
    }
}
