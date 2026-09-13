using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageMcpRemoteConnections;

/// <summary>MCP 远端连接管理 HTTP 端点。</summary>
internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/mcp/remote-connections")
            .WithTags("AiMcpRemoteConnections");

        group.MapGet("/", async (
            AiMcpRemoteConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListMcpRemoteConnections")
        .Produces<IReadOnlyList<AiMcpRemoteConnectionListItem>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapGet("/{connectionId:guid}", async (
            Guid connectionId,
            AiMcpRemoteConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetAsync(connectionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiGetMcpRemoteConnection")
        .Produces<AiMcpRemoteConnectionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapPost("/", async (
            CreateAiMcpRemoteConnectionRequest request,
            AiMcpRemoteConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiCreateMcpRemoteConnection")
        .Produces<AiMcpRemoteConnectionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapPut("/{connectionId:guid}", async (
            Guid connectionId,
            UpdateAiMcpRemoteConnectionRequest request,
            AiMcpRemoteConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(connectionId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiUpdateMcpRemoteConnection")
        .Produces<AiMcpRemoteConnectionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapPost("/{connectionId:guid}/discover-tools", async (
            Guid connectionId,
            AiMcpRemoteConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DiscoverToolsAsync(connectionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiDiscoverMcpRemoteTools")
        .Produces<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapGet("/{connectionId:guid}/approvals", async (
            Guid connectionId,
            AiMcpRemoteConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListApprovalsAsync(connectionId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiListMcpRemoteToolApprovals")
        .Produces<IReadOnlyList<AiMcpRemoteToolApprovalItem>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));

        group.MapPost("/{connectionId:guid}/approve-tool", async (
            Guid connectionId,
            ApproveAiMcpRemoteToolRequest request,
            AiMcpRemoteConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveToolAsync(connectionId, request, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("aiApproveMcpRemoteTool")
        .Produces<AiMcpRemoteToolApprovalItem>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(AiMcpPermissions.Manage));
    }
}
